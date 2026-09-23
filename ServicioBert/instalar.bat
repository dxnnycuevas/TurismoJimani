@echo off
rem Crea el entorno virtual de Python e instala las dependencias del servicio BERT.
cd /d "%~dp0"

if not exist ".venv\Scripts\python.exe" (
    echo Creando entorno virtual...
    python -m venv .venv || goto error
)

".venv\Scripts\python.exe" -m pip install --upgrade pip || goto error
".venv\Scripts\python.exe" -m pip install -r requirements.txt || goto error

echo.
echo Dependencias instaladas. Siguiente paso: entrenar.bat
exit /b 0

:error
echo.
echo Hubo un error durante la instalacion.
exit /b 1
