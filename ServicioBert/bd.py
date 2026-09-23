"""Conexión a SQL Server para el entrenamiento.

La cadena de conexión se toma de ../appsettings.json (ConnectionStrings:TurismoJimani),
la misma que usa ASP.NET, y se convierte al formato ODBC. Se puede sobrescribir con la
variable de entorno TURISMO_BD_ODBC.
"""
import json
import os
from pathlib import Path

import pyodbc

RUTA_APPSETTINGS = Path(__file__).resolve().parent.parent / "appsettings.json"
NOMBRE_CADENA = "TurismoJimani"


def _elegir_driver() -> str:
    disponibles = pyodbc.drivers()
    for nombre in ("ODBC Driver 18 for SQL Server", "ODBC Driver 17 for SQL Server", "SQL Server"):
        if nombre in disponibles:
            return nombre
    raise RuntimeError("No hay un driver ODBC para SQL Server instalado.")


def _convertir_a_odbc(cadena_net: str) -> str:
    partes = {}
    for segmento in cadena_net.split(";"):
        if "=" in segmento:
            clave, valor = segmento.split("=", 1)
            partes[clave.strip().lower()] = valor.strip()

    servidor = partes.get("server") or partes.get("data source")
    base = partes.get("database") or partes.get("initial catalog")
    if not servidor or not base:
        raise RuntimeError("La cadena de conexión de appsettings.json no tiene Server/Database.")

    odbc = [f"DRIVER={{{_elegir_driver()}}}", f"SERVER={servidor}", f"DATABASE={base}"]

    confiable = (partes.get("trusted_connection") or partes.get("integrated security") or "").lower()
    if confiable in ("true", "yes", "sspi"):
        odbc.append("Trusted_Connection=yes")
    else:
        odbc.append(f"UID={partes.get('user id') or partes.get('uid', '')}")
        odbc.append(f"PWD={partes.get('password') or partes.get('pwd', '')}")

    if (partes.get("trustservercertificate") or "").lower() in ("true", "yes"):
        odbc.append("TrustServerCertificate=yes")

    return ";".join(odbc) + ";"


def obtener_cadena() -> str:
    if os.environ.get("TURISMO_BD_ODBC"):
        return os.environ["TURISMO_BD_ODBC"]

    with open(RUTA_APPSETTINGS, encoding="utf-8-sig") as archivo:
        config = json.load(archivo)
    return _convertir_a_odbc(config["ConnectionStrings"][NOMBRE_CADENA])


def conectar() -> pyodbc.Connection:
    return pyodbc.connect(obtener_cadena(), timeout=10)
