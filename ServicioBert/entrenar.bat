@echo off
rem Entrena el clasificador de intenciones con los ejemplos de la tabla EjemplosIntencion.
rem Detenga el servicio (iniciar.bat) antes de entrenar.
cd /d "%~dp0"

if not exist ".venv\Scripts\python.exe" (
    echo No existe el entorno virtual. Ejecute instalar.bat primero.
    exit /b 1
)

rem Evita avisos de la caché de Hugging Face en Windows (no afectan el entrenamiento)
set HF_HUB_DISABLE_SYMLINKS_WARNING=1
set PYTHONIOENCODING=utf-8

".venv\Scripts\python.exe" entrenar.py %*
