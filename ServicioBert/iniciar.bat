@echo off
rem Inicia el servicio BERT en http://127.0.0.1:8000 (solo accesible desde este equipo).
cd /d "%~dp0"

if not exist ".venv\Scripts\python.exe" (
    echo No existe el entorno virtual. Ejecute instalar.bat primero.
    exit /b 1
)

if not exist "modelo\config.json" (
    echo No hay un modelo entrenado. Ejecute entrenar.bat primero.
    exit /b 1
)

set PYTHONIOENCODING=utf-8
".venv\Scripts\python.exe" -m uvicorn servicio:app --host 127.0.0.1 --port 8000
