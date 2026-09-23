"""Servicio REST de clasificación de intenciones con BERT (BETO) para el asistente turístico.

ASP.NET envía el mensaje del usuario y recibe la intención detectada con su confianza.
Este servicio NO tiene datos turísticos ni acceso a la base de datos.

Ejecutar:  iniciar.bat   (o: .venv\\Scripts\\python -m uvicorn servicio:app --host 127.0.0.1 --port 8000)

Endpoints:
    GET  /salud     -> estado del servicio y del modelo
    POST /predecir  {"texto": "Quiero un hotel"}
                    -> {"intencion": "BuscarAlojamiento", "confianza": 0.98, "alternativas": [...], "milisegundos": 35.2}
"""
import json
import logging
import os
import time
from contextlib import asynccontextmanager
from pathlib import Path

import torch
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field
from transformers import AutoModelForSequenceClassification, AutoTokenizer

CARPETA_MODELO = Path(os.environ.get("BERT_MODELO", Path(__file__).resolve().parent / "modelo"))
LONGITUD_MAXIMA = 64

registro = logging.getLogger("servicio_bert")
estado: dict = {}


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
    return Prediccion(
        intencion=alternativas[0].intencion,
        confianza=alternativas[0].confianza,
        alternativas=alternativas,
        milisegundos=round((time.perf_counter() - inicio) * 1000, 1),
    )
