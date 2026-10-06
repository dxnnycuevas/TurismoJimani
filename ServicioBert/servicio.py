"""Servicio REST de clasificación de intenciones con BERT (BETO) para el asistente turístico.

ASP.NET envía el mensaje del usuario y recibe la intención detectada con su confianza.
Este servicio NO tiene datos turísticos ni acceso a la base de datos.

Ejecutar:  iniciar.bat   (o: .venv\\Scripts\\python -m uvicorn servicio:app --host 127.0.0.1 --port 8000)

Endpoints:
    GET  /salud     -> estado del servicio y del modelo
    GET  /estado    -> memoria, CPU y actividad del servicio (panel de administración)
    POST /predecir  {"texto": "Quiero un hotel"}
                    -> {"intencion": "BuscarAlojamiento", "confianza": 0.98, "alternativas": [...], "milisegundos": 35.2}
"""
import ctypes
import json
import logging
import os
import platform
import threading
import time
from contextlib import asynccontextmanager
from datetime import datetime
from pathlib import Path

import torch
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field
import transformers
from transformers import AutoModelForSequenceClassification, AutoTokenizer

CARPETA_MODELO = Path(os.environ.get("BERT_MODELO", Path(__file__).resolve().parent / "modelo"))
LONGITUD_MAXIMA = 64

registro = logging.getLogger("servicio_bert")
estado: dict = {}

# Actividad del servicio para el panel (las rutas síncronas de FastAPI corren en varios hilos)
INICIO = time.time()
_bloqueo = threading.Lock()
actividad = {"predicciones": 0, "ms_total": 0.0, "ultima": None}
_cpu_anterior = {"proceso": time.process_time(), "reloj": time.perf_counter()}


@asynccontextmanager
async def ciclo_de_vida(_: FastAPI):
    if not (CARPETA_MODELO / "config.json").exists():
        raise RuntimeError(f"No se encontró el modelo entrenado en {CARPETA_MODELO}. Ejecute entrenar.bat primero.")

    registro.warning("Cargando modelo desde %s ...", CARPETA_MODELO)
    tokenizer = AutoTokenizer.from_pretrained(CARPETA_MODELO)
    modelo = AutoModelForSequenceClassification.from_pretrained(CARPETA_MODELO)
    modelo.eval()

    metricas_ruta = CARPETA_MODELO / "metricas.json"
    estado.update(
        tokenizer=tokenizer,
        modelo=modelo,
        etiquetas={int(k): v for k, v in modelo.config.id2label.items()},
        metricas=json.loads(metricas_ruta.read_text(encoding="utf-8")) if metricas_ruta.exists() else {},
    )
    archivo_modelo = CARPETA_MODELO / "model.safetensors"
    estado.update(
        bytes_modelo=sum(t.numel() * t.element_size() for t in [*modelo.parameters(), *modelo.buffers()]),
        bytes_archivo=archivo_modelo.stat().st_size if archivo_modelo.exists() else None,
    )
    clasificar("hola")  # calentamiento: la primera inferencia es más lenta
    registro.warning("Modelo listo. Intenciones: %s", ", ".join(estado["etiquetas"].values()))
    yield
    estado.clear()


app = FastAPI(title="Servicio BERT - Asistente turístico de Jimaní", lifespan=ciclo_de_vida)


class Solicitud(BaseModel):
    texto: str = Field(min_length=1, max_length=500)


class Alternativa(BaseModel):
    intencion: str
    confianza: float


class Prediccion(BaseModel):
    intencion: str
    confianza: float
    alternativas: list[Alternativa]
    milisegundos: float


def clasificar(texto: str) -> list[Alternativa]:
    tokenizer, modelo = estado["tokenizer"], estado["modelo"]
    entradas = tokenizer(texto, return_tensors="pt", truncation=True, max_length=LONGITUD_MAXIMA)
    with torch.inference_mode():
        probabilidades = torch.softmax(modelo(**entradas).logits[0], dim=-1)
    valores, indices = torch.topk(probabilidades, k=min(3, probabilidades.numel()))
    return [Alternativa(intencion=estado["etiquetas"][i], confianza=round(v, 4))
            for v, i in zip(valores.tolist(), indices.tolist())]


@app.get("/salud")
def salud():
    return {
        "estado": "ok" if "modelo" in estado else "cargando",
        "modeloBase": estado.get("metricas", {}).get("modeloBase"),
        "entrenado": estado.get("metricas", {}).get("fecha"),
        "exactitudValidacion": estado.get("metricas", {}).get("exactitudValidacion"),
        "intenciones": list(estado.get("etiquetas", {}).values()),
    }


@app.post("/predecir", response_model=Prediccion)
def predecir(solicitud: Solicitud) -> Prediccion:
    texto = solicitud.texto.strip()
    if not texto:
        raise HTTPException(status_code=422, detail="El texto está vacío.")

    inicio = time.perf_counter()
    alternativas = clasificar(texto)
    milisegundos = round((time.perf_counter() - inicio) * 1000, 1)

    with _bloqueo:
        actividad["predicciones"] += 1
        actividad["ms_total"] += milisegundos
        actividad["ultima"] = datetime.now().isoformat(timespec="seconds")

    return Prediccion(
        intencion=alternativas[0].intencion,
        confianza=alternativas[0].confianza,
        alternativas=alternativas,
        milisegundos=milisegundos,
    )


# ---------------------------------------------------------------- estado del servicio (memoria y CPU)

MB = 1024 * 1024


def _memoria() -> dict:
    """Memoria del proceso (en uso y pico) y del equipo, en bytes. Sin dependencias extra."""
    if os.name == "nt":
        class ContadoresProceso(ctypes.Structure):
            _fields_ = [("cb", ctypes.c_ulong), ("PageFaultCount", ctypes.c_ulong),
                        ("PeakWorkingSetSize", ctypes.c_size_t), ("WorkingSetSize", ctypes.c_size_t),
                        ("QuotaPeakPagedPoolUsage", ctypes.c_size_t), ("QuotaPagedPoolUsage", ctypes.c_size_t),
                        ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t), ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
                        ("PagefileUsage", ctypes.c_size_t), ("PeakPagefileUsage", ctypes.c_size_t)]

        class EstadoMemoria(ctypes.Structure):
            _fields_ = [("dwLength", ctypes.c_ulong), ("dwMemoryLoad", ctypes.c_ulong),
                        ("ullTotalPhys", ctypes.c_ulonglong), ("ullAvailPhys", ctypes.c_ulonglong),
                        ("ullTotalPageFile", ctypes.c_ulonglong), ("ullAvailPageFile", ctypes.c_ulonglong),
                        ("ullTotalVirtual", ctypes.c_ulonglong), ("ullAvailVirtual", ctypes.c_ulonglong),
                        ("ullAvailExtendedVirtual", ctypes.c_ulonglong)]

        kernel32 = ctypes.windll.kernel32
        kernel32.GetCurrentProcess.restype = ctypes.c_void_p
        kernel32.K32GetProcessMemoryInfo.argtypes = [ctypes.c_void_p, ctypes.POINTER(ContadoresProceso), ctypes.c_ulong]

        proceso = ContadoresProceso()
        proceso.cb = ctypes.sizeof(proceso)
        kernel32.K32GetProcessMemoryInfo(kernel32.GetCurrentProcess(), ctypes.byref(proceso), proceso.cb)

        equipo = EstadoMemoria()
        equipo.dwLength = ctypes.sizeof(equipo)
        kernel32.GlobalMemoryStatusEx(ctypes.byref(equipo))

        return {"proceso": proceso.WorkingSetSize, "pico": proceso.PeakWorkingSetSize,
                "equipo_total": equipo.ullTotalPhys, "equipo_disponible": equipo.ullAvailPhys}

    # Linux: /proc (en otros sistemas se devuelve vacío)
    datos = {}
    try:
        for linea in Path("/proc/self/status").read_text().splitlines():
            if linea.startswith("VmRSS:"):
                datos["proceso"] = int(linea.split()[1]) * 1024
            elif linea.startswith("VmHWM:"):
                datos["pico"] = int(linea.split()[1]) * 1024
        for linea in Path("/proc/meminfo").read_text().splitlines():
            if linea.startswith("MemTotal:"):
                datos["equipo_total"] = int(linea.split()[1]) * 1024
            elif linea.startswith("MemAvailable:"):
                datos["equipo_disponible"] = int(linea.split()[1]) * 1024
    except OSError:
        pass
    return datos


def _cpu() -> float:
    """Porcentaje de CPU que usó el proceso desde la consulta anterior (100 % = todos los núcleos)."""
    ahora_proceso, ahora_reloj = time.process_time(), time.perf_counter()
    with _bloqueo:
        usado = ahora_proceso - _cpu_anterior["proceso"]
        transcurrido = ahora_reloj - _cpu_anterior["reloj"]
        _cpu_anterior.update(proceso=ahora_proceso, reloj=ahora_reloj)
    if transcurrido <= 0:
        return 0.0
    return round(min(100.0, usado / transcurrido / (os.cpu_count() or 1) * 100), 1)


def _mb(valor):
    return None if valor is None else round(valor / MB, 1)


@app.get("/estado")
def estado_servicio():
    memoria = _memoria()
    metricas = estado.get("metricas", {})
    with _bloqueo:
        predicciones = actividad["predicciones"]
        promedio = round(actividad["ms_total"] / predicciones, 1) if predicciones else None
        ultima = actividad["ultima"]

    return {
        "estado": "ok" if "modelo" in estado else "cargando",
        "modelo": {
            "base": metricas.get("modeloBase"),
            "entrenado": metricas.get("fecha"),
            "exactitudValidacion": metricas.get("exactitudValidacion"),
            "intenciones": len(estado.get("etiquetas", {})),
            "carpeta": CARPETA_MODELO.name,
        },
        "memoria": {
            "procesoMb": _mb(memoria.get("proceso")),
            "picoMb": _mb(memoria.get("pico")),
            "modeloMb": _mb(estado.get("bytes_modelo")),
            "archivoModeloMb": _mb(estado.get("bytes_archivo")),
            "equipoTotalMb": _mb(memoria.get("equipo_total")),
            "equipoDisponibleMb": _mb(memoria.get("equipo_disponible")),
        },
        "cpu": {
            "porcentaje": _cpu(),
            "nucleos": os.cpu_count(),
            "hilosTorch": torch.get_num_threads(),
        },
        "actividad": {
            "predicciones": predicciones,
            "msPromedio": promedio,
            "ultimaPrediccion": ultima,
        },
        "encendidoSegundos": int(time.time() - INICIO),
        "versiones": {
            "python": platform.python_version(),
            "torch": torch.__version__,
            "transformers": transformers.__version__,
        },
    }
