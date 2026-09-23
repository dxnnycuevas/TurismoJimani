"""Entrena el clasificador de intenciones del asistente turístico (BERT en español - BETO).

Pasos:
  1. Si una intención de datos/intenciones.json no existe en la tabla Intenciones, la crea
     junto con sus ejemplos en EjemplosIntencion. Las intenciones que ya existen no se tocan,
     así se respetan los ejemplos agregados o borrados desde el panel de administración.
  2. Lee de SQL Server los ejemplos de las intenciones activas.
  3. Ajusta (fine-tuning) el modelo BETO para clasificar esas intenciones.
  4. Guarda el modelo en ./modelo junto con sus métricas.

BERT solo aprende a reconocer QUÉ quiere el usuario. Los hoteles, restaurantes, atractivos,
etc. se consultan siempre en SQL Server desde ASP.NET, por eso no hace falta reentrenar
cuando se agregan lugares nuevos.

Uso (con el servicio detenido):
    .venv\\Scripts\\python entrenar.py
    .venv\\Scripts\\python entrenar.py --solo-archivo      (no usa la base de datos)
    .venv\\Scripts\\python entrenar.py --resembrar         (agrega ejemplos base que falten)
"""
import argparse
import json
import random
import time
import unicodedata
from collections import Counter, defaultdict
from datetime import datetime
from pathlib import Path

import torch
from transformers import (AutoModelForSequenceClassification, AutoTokenizer,
                          get_linear_schedule_with_warmup)

CARPETA = Path(__file__).resolve().parent
ARCHIVO_DATOS = CARPETA / "datos" / "intenciones.json"
CARPETA_MODELO = CARPETA / "modelo"
MODELO_BASE = "dccuchile/bert-base-spanish-wwm-uncased"  # BETO
LONGITUD_MAXIMA = 64
SEMILLA = 42


# ---------------------------------------------------------------- datos

def expandir_ejemplos(intencion: dict, datos: dict) -> list[str]:
    """Ejemplos fijos + plantillas completadas con nombres de relleno."""
    ejemplos = list(intencion.get("ejemplos", []))
    plantillas = intencion.get("plantillas", [])
    if plantillas:
        rellenos = datos["rellenos"][intencion["relleno"]]
        azar = random.Random(f"{SEMILLA}-{intencion['nombre']}")
        for plantilla in plantillas:
            for nombre in azar.sample(rellenos, k=min(datos["ejemplosPorPlantilla"], len(rellenos))):
                ejemplos.append(plantilla.replace("{" + intencion["relleno"] + "}", nombre))
    # sin duplicados, conservando el orden
    return list(dict.fromkeys(e.strip() for e in ejemplos if e.strip()))


def sincronizar_bd(conexion, datos: dict, resembrar: bool) -> None:
    cursor = conexion.cursor()
    for intencion in datos["intenciones"]:
        cursor.execute("SELECT IdIntencion FROM Intenciones WHERE Nombre = ?", intencion["nombre"])
        fila = cursor.fetchone()
        nueva = fila is None

        if nueva:
            cursor.execute(
                "INSERT INTO Intenciones (Nombre, Descripcion, Activa) OUTPUT INSERTED.IdIntencion VALUES (?, ?, 1)",
                intencion["nombre"], intencion.get("descripcion"))
            id_intencion = cursor.fetchone()[0]
        else:
            id_intencion = fila[0]

        cursor.execute("SELECT COUNT(*) FROM EjemplosIntencion WHERE IdIntencion = ?", id_intencion)
        sin_ejemplos = cursor.fetchone()[0] == 0

        if nueva or sin_ejemplos or resembrar:
            existentes = {r[0] for r in cursor.execute(
                "SELECT Texto FROM EjemplosIntencion WHERE IdIntencion = ?", id_intencion).fetchall()}
            faltantes = [t for t in expandir_ejemplos(intencion, datos) if t not in existentes]
            if faltantes:
                cursor.executemany(
                    "INSERT INTO EjemplosIntencion (IdIntencion, Texto, Idioma) VALUES (?, ?, 'es')",
                    [(id_intencion, t) for t in faltantes])
            print(f"  {intencion['nombre']}: {'creada, ' if nueva else ''}{len(faltantes)} ejemplos agregados")
    conexion.commit()


def leer_ejemplos_bd(conexion) -> list[tuple[str, str]]:
    cursor = conexion.cursor()
    cursor.execute("""
        SELECT i.Nombre, e.Texto
        FROM EjemplosIntencion e
        INNER JOIN Intenciones i ON i.IdIntencion = e.IdIntencion
        WHERE i.Activa = 1
        ORDER BY i.Nombre, e.IdEjemplo""")
    return [(fila[0], fila[1]) for fila in cursor.fetchall()]


def leer_ejemplos_archivo(datos: dict) -> list[tuple[str, str]]:
    return [(i["nombre"], t) for i in datos["intenciones"] for t in expandir_ejemplos(i, datos)]


def sin_acentos(texto: str) -> str:
    return "".join(c for c in unicodedata.normalize("NFD", texto) if unicodedata.category(c) != "Mn")


def aumentar(texto: str) -> list[str]:
    """Variantes como las que escriben los usuarios: sin tildes y sin signos."""
    variantes = {texto, sin_acentos(texto), texto.strip("¿?¡!. ").lower()}
    variantes.add(sin_acentos(texto.strip("¿?¡!. ").lower()))
    return list(variantes)


def dividir(ejemplos: list[tuple[int, str]], proporcion: float) -> tuple[list, list]:
    """División estratificada: cada intención aporta ejemplos a validación."""
    azar = random.Random(SEMILLA)
    por_etiqueta = defaultdict(list)
    for etiqueta, texto in ejemplos:
        por_etiqueta[etiqueta].append(texto)
    entrenamiento, validacion = [], []
    for etiqueta, textos in por_etiqueta.items():
        azar.shuffle(textos)
        corte = max(1, round(len(textos) * proporcion)) if len(textos) > 4 else 0
        validacion += [(etiqueta, t) for t in textos[:corte]]
        entrenamiento += [(etiqueta, t) for t in textos[corte:]]
    return entrenamiento, validacion


# ---------------------------------------------------------------- entrenamiento

def lotes(ejemplos, tamano, tokenizer, mezclar):
    orden = list(range(len(ejemplos)))
    if mezclar:
        random.shuffle(orden)
    for inicio in range(0, len(orden), tamano):
        grupo = [ejemplos[i] for i in orden[inicio:inicio + tamano]]
        entradas = tokenizer([t for _, t in grupo], padding=True, truncation=True,
                             max_length=LONGITUD_MAXIMA, return_tensors="pt")
        yield entradas, torch.tensor([e for e, _ in grupo])


def evaluar(modelo, tokenizer, ejemplos, nombres):
    modelo.eval()
    aciertos, errores, procesados = 0, [], 0
    por_intencion = defaultdict(lambda: [0, 0])
    with torch.inference_mode():
        for entradas, etiquetas in lotes(ejemplos, 32, tokenizer, mezclar=False):
            predicciones = modelo(**entradas).logits.argmax(-1)
            textos = [t for _, t in ejemplos[procesados:procesados + len(etiquetas)]]
            procesados += len(etiquetas)
            for real, pred, texto in zip(etiquetas.tolist(), predicciones.tolist(), textos):
                por_intencion[nombres[real]][1] += 1
                if real == pred:
                    aciertos += 1
                    por_intencion[nombres[real]][0] += 1
                else:
                    errores.append({"texto": texto, "esperada": nombres[real], "predicha": nombres[pred]})
    total = max(1, len(ejemplos))
    return aciertos / total, errores, {k: round(v[0] / v[1], 3) for k, v in por_intencion.items()}


def entrenar(ejemplos: list[tuple[str, str]], args) -> None:
    random.seed(SEMILLA)
    torch.manual_seed(SEMILLA)

    nombres = sorted({intencion for intencion, _ in ejemplos})
    indice = {n: i for i, n in enumerate(nombres)}
    etiquetados = [(indice[n], t) for n, t in ejemplos]

    entrenamiento, validacion = dividir(etiquetados, args.validacion)
    entrenamiento = [(e, v) for e, t in entrenamiento for v in aumentar(t)]
    print(f"Intenciones: {len(nombres)} | ejemplos: {len(ejemplos)} "
          f"| entrenamiento (con variantes): {len(entrenamiento)} | validación: {len(validacion)}")

    print(f"Cargando modelo base {MODELO_BASE} (la primera vez se descarga, ~440 MB)...")
    tokenizer = AutoTokenizer.from_pretrained(MODELO_BASE)
    modelo = AutoModelForSequenceClassification.from_pretrained(
        MODELO_BASE, num_labels=len(nombres),
        id2label=dict(enumerate(nombres)), label2id=indice)

    pasos = args.epocas * ((len(entrenamiento) + args.lote - 1) // args.lote)
    optimizador = torch.optim.AdamW(modelo.parameters(), lr=args.tasa, weight_decay=0.01)
    programador = get_linear_schedule_with_warmup(optimizador, int(pasos * 0.1), pasos)

    inicio = time.time()
    for epoca in range(1, args.epocas + 1):
        modelo.train()
        perdida_total, n = 0.0, 0
        for entradas, etiquetas in lotes(entrenamiento, args.lote, tokenizer, mezclar=True):
            salida = modelo(**entradas, labels=etiquetas)
            salida.loss.backward()
            torch.nn.utils.clip_grad_norm_(modelo.parameters(), 1.0)
            optimizador.step()
            programador.step()
            optimizador.zero_grad()
            perdida_total += salida.loss.item()
            n += 1
        exactitud, _, _ = evaluar(modelo, tokenizer, validacion, nombres) if validacion else (0, [], {})
        print(f"  Época {epoca}/{args.epocas} - pérdida {perdida_total / n:.4f} - "
              f"exactitud validación {exactitud:.1%} - {time.time() - inicio:.0f}s")

    exactitud, errores, por_intencion = evaluar(modelo, tokenizer, validacion, nombres) if validacion else (0, [], {})

    try:
        CARPETA_MODELO.mkdir(exist_ok=True)
        modelo.save_pretrained(CARPETA_MODELO)
        tokenizer.save_pretrained(CARPETA_MODELO)
    except PermissionError:
        raise SystemExit("No se pudo guardar el modelo: detenga el servicio BERT (iniciar.bat) y vuelva a entrenar.")

    metricas = {
        "fecha": datetime.now().isoformat(timespec="seconds"),
        "modeloBase": MODELO_BASE,
        "intenciones": nombres,
        "ejemplosPorIntencion": dict(Counter(n for n, _ in ejemplos)),
        "exactitudValidacion": round(exactitud, 4),
        "exactitudPorIntencion": por_intencion,
        "erroresValidacion": errores,
        "parametros": {"epocas": args.epocas, "lote": args.lote, "tasa": args.tasa, "semilla": SEMILLA},
    }
    (CARPETA_MODELO / "metricas.json").write_text(json.dumps(metricas, ensure_ascii=False, indent=2), encoding="utf-8")

    print(f"\nModelo guardado en {CARPETA_MODELO}")
    print(f"Exactitud en validación: {exactitud:.1%}")
    for error in errores:
        print(f"  Error: \"{error['texto']}\" -> {error['predicha']} (esperada {error['esperada']})")


def main() -> None:
    parser = argparse.ArgumentParser(description="Entrena el clasificador de intenciones (BETO).")
    parser.add_argument("--solo-archivo", action="store_true", help="Entrenar con datos/intenciones.json sin usar SQL Server")
    parser.add_argument("--resembrar", action="store_true", help="Agregar a la BD los ejemplos base que falten")
    parser.add_argument("--epocas", type=int, default=6)
    parser.add_argument("--lote", type=int, default=16)
    parser.add_argument("--tasa", type=float, default=4e-5)
    parser.add_argument("--validacion", type=float, default=0.15)
    args = parser.parse_args()

    datos = json.loads(ARCHIVO_DATOS.read_text(encoding="utf-8"))

    if args.solo_archivo:
        ejemplos = leer_ejemplos_archivo(datos)
    else:
        from bd import conectar
        print("Sincronizando intenciones con SQL Server...")
        with conectar() as conexion:
            sincronizar_bd(conexion, datos, args.resembrar)
            ejemplos = leer_ejemplos_bd(conexion)

    if len({n for n, _ in ejemplos}) < 2:
        raise SystemExit("Se necesitan al menos dos intenciones activas con ejemplos para entrenar.")

    entrenar(ejemplos, args)


if __name__ == "__main__":
    main()
