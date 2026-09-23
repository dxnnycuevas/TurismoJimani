# Servicio BERT del asistente turístico

Servicio en Python que clasifica la **intención** de cada mensaje del chat usando
**BETO** (`dccuchile/bert-base-spanish-wwm-uncased`), un modelo BERT entrenado en español.

BERT **no guarda** hoteles, restaurantes ni atractivos: solo decide qué quiere el usuario
(por ejemplo `BuscarAlojamiento`). ASP.NET consulta después SQL Server y arma la respuesta.
Por eso, al agregar lugares nuevos no hace falta volver a entrenar.

```
Chat (navegador) ──fetch──> ASP.NET /Asistente/EnviarMensaje
                               │  HTTP POST /predecir {"texto": "..."}
                               ▼
                         Servicio BERT (este proyecto, http://127.0.0.1:8000)
                               │  {"intencion": "BuscarAlojamiento", "confianza": 0.98}
                               ▼
                         ASP.NET ChatbotService ──EF Core──> SQL Server (TurismoJimani)
                               │
                               ▼
                         Respuesta en el chat + registro en ConsultasAsistente
```

## Requisitos

- Python 3.14 (probado con 3.14.6, 64 bits)
- ODBC Driver 17 o 18 for SQL Server (solo para entrenar)
- ~2 GB libres (PyTorch + modelo)

## Primera vez

```bat
cd ServicioBert
instalar.bat      :: crea .venv e instala dependencias (requirements.txt)
entrenar.bat      :: carga intenciones/ejemplos en SQL Server y entrena BETO (varios minutos)
```

`entrenar.bat`:
1. Si una intención de `datos/intenciones.json` no existe en la tabla `Intenciones`, la crea con sus
   ejemplos en `EjemplosIntencion`. Las intenciones que ya existen no se tocan.
2. Lee de SQL Server los ejemplos de las intenciones **activas**.
3. Ajusta BETO y guarda el modelo en `modelo/` (con `metricas.json`).

La cadena de conexión se toma de `../appsettings.json` (la misma que usa ASP.NET).

## Uso diario

1. Iniciar el servicio BERT (dejar la ventana abierta):
   ```bat
   ServicioBert\iniciar.bat
   ```
2. Iniciar ASP.NET en otra terminal:
   ```bat
   dotnet run
   ```
3. Abrir `/Asistente` en el navegador.

## Mejorar el modelo

1. Agregar o corregir ejemplos desde el panel: **Asistente ▸ Ejemplos de intención**.
2. Detener el servicio (Ctrl+C en su ventana).
3. Ejecutar `entrenar.bat` y volver a iniciar el servicio.

Las consultas reales quedan en **Asistente ▸ Consultas** (tabla `ConsultasAsistente`) y sirven para
encontrar frases mal clasificadas.

Opciones de `entrenar.bat`:
- `--solo-archivo`: entrena con `datos/intenciones.json` sin conectarse a SQL Server.
- `--resembrar`: agrega a la base de datos los ejemplos base que falten.
- `--epocas N` (6 por defecto).

## Endpoints

- `GET /salud` – estado del servicio y del modelo.
- `POST /predecir` – `{"texto": "Quiero un hotel"}` →
  `{"intencion": "BuscarAlojamiento", "confianza": 0.98, "alternativas": [...], "milisegundos": 40}`

El servicio escucha solo en `127.0.0.1`, no es accesible desde otros equipos.

## Documentos de conocimiento

Un documento de **Asistente ▸ Documentos de conocimiento** se incluye en la respuesta sobre un lugar
cuando su `IdReferencia` es el `IdLugar` y su `TipoFuente` es `Lugar`, `Atractivo`, `Alojamiento`,
`Restaurante`, `Transporte` o está vacío.
