# Seguridad de Turismo Jimaní

Controles aplicados en respuesta a las observaciones S-01 a S-08 de la revisión del asesor (octubre de 2026).

## Resumen por observación

| Código | Control implementado | Archivos |
|---|---|---|
| S-01 | Contraseñas de 15 a 128 caracteres, sin reglas de composición, comparadas con una lista de contraseñas comunes o esperables (incluido el correo del usuario) y con la base de contraseñas filtradas de Have I Been Pwned (k-anonimato: solo se envían 5 caracteres del hash SHA-1). Se aplica en el registro, en el alta y edición de usuarios y en el cambio de contraseña. | `Helpers/PoliticaContrasena.cs`, `Services/Seguridad/ValidadorContrasenas.cs` |
| S-02 | Límite de 10 envíos por minuto y por IP en los formularios de acceso, registro y cambio de contraseña (política `acceso`), y bloqueo de 15 minutos tras 5 contraseñas incorrectas seguidas para el mismo correo. El conteo se hace exista o no la cuenta, y la respuesta tarda lo mismo en ambos casos, para no revelar qué correos están registrados. | `Program.cs`, `Services/Seguridad/ProteccionAcceso.cs`, `Controllers/CuentaController.cs` |
| S-03 | La contraseña inicial del administrador ya no está en el código: se lee de `AdminInicial:Contrasena` (user-secrets o variable de entorno). Al iniciar sesión con la contraseña inicial, o con una que ya no cumple la política (por ejemplo, la antigua `Admin123*`), la aplicación obliga a cambiarla antes de permitir cualquier otra acción. | `Data/DbInitializer.cs`, `Helpers/CambioContrasenaObligatorio.cs`, `Views/Cuenta/CambiarContrasena.cshtml` |
| S-04 | Cookies con `Secure` siempre fuera de desarrollo (autenticación, antifalsificación y TempData), además de `HttpOnly` y `SameSite=Lax`. La cadena de conexión salió de `appsettings.json`. | `Program.cs`, `appsettings.json`, `appsettings.Development.json` (no se sube a GitHub) |
| S-05 | `UseForwardedHeaders` con `X-Forwarded-For` y `X-Forwarded-Proto`, solo desde proxies conocidos (`Proxy:IpsConocidas`). Es lo primero del pipeline, de modo que el límite por IP, la redirección HTTPS, HSTS y las cookies seguras usan la IP y el esquema reales. | `Program.cs` |
| S-06 | Las fotos se validan por su firma real (magic bytes: JPEG `FF D8 FF`, PNG `89 50 4E 47 0D 0A 1A 0A`, WEBP `RIFF....WEBP`), además de la extensión y el Content-Type. Se retiró GIF. Se guardan con nombre aleatorio (GUID) y con la extensión que corresponde a su firma. | `Helpers/AlmacenImagenes.cs` |
| S-07 | `chat.js` construye las respuestas y fichas con `createElement` y `textContent` (no usa `innerHTML`). Se agregaron los encabezados `Content-Security-Policy` (con nonce por petición y `strict-dynamic`), `X-Content-Type-Options: nosniff`, `X-Frame-Options: SAMEORIGIN` / `frame-ancestors 'self'`, `Referrer-Policy` y `Permissions-Policy`. Se quitaron los dos manejadores de eventos en línea (`onclick`, `onsubmit`). | `Helpers/EncabezadosSeguridad.cs`, `Views/_ViewImports.cshtml`, `wwwroot/js/site.js`, `wwwroot/sitio/js/jimani.js` |
| S-08 | Referencia actualizada a OWASP Top 10:2025 (tabla siguiente). | Este documento |

## Relación con OWASP Top 10:2025

| Categoría | Controles de la plataforma |
|---|---|
| A01:2025 Broken Access Control | Filtro global de autorización por roles (Administrador, Editor; Visitante sin acceso al panel), tokens antifalsificación en todos los formularios POST, redirecciones solo a URL locales. |
| A02:2025 Security Misconfiguration | Encabezados de seguridad y CSP, HSTS, cookies seguras, secretos fuera del repositorio, página de error genérica fuera de desarrollo. |
| A03:2025 Software Supply Chain Failures | Paquetes NuGet con versión fija; bibliotecas de cliente locales (Bootstrap, jQuery); Chart.js con versión fija desde jsDelivr (pendiente: agregar el hash SRI `integrity`). |
| A04:2025 Cryptographic Failures | Hash de contraseñas con `PasswordHasher` (PBKDF2), HTTPS obligatorio y cookies cifradas por Data Protection. |
| A05:2025 Injection | Consultas con Entity Framework Core (parametrizadas), salida codificada por Razor, `textContent` en el chat y CSP con nonce. |
| A06:2025 Insecure Design | Límite de peticiones en el chat y en el acceso, bloqueo por intentos fallidos, validación de archivos por firma. |
| A07:2025 Authentication Failures | Política de contraseñas NIST SP 800-63B-4, lista de contraseñas filtradas, bloqueo temporal, cambio obligatorio de la contraseña inicial y cierre de sesión por inactividad. |
| A08:2025 Software or Data Integrity Failures | Cookies firmadas; el cambio de contraseña pendiente viaja en la cookie cifrada y no puede alterarse desde el navegador. |
| A09:2025 Security Logging and Alerting Failures | Registro de advertencias (creación del administrador inicial, fallos del servicio de contraseñas filtradas). Pendiente: registro de inicios de sesión fallidos y bloqueos. |
| A10:2025 Mishandling of Exceptional Conditions | Manejador global de excepciones, respuestas 429 controladas, el servicio de contraseñas filtradas falla de forma segura (aplica la política local). |

## Configuración

### Desarrollo (Visual Studio o `dotnet run`)

La cadena de conexión está en `appsettings.Development.json`, que `.gitignore` excluye del repositorio. Otra opción es user-secrets:

```
dotnet user-secrets set "ConnectionStrings:TurismoJimani" "Server=...;Database=TurismoJimani;Trusted_Connection=True;TrustServerCertificate=True;"
```

Solo si la base de datos está vacía (sin administrador), defina la contraseña inicial antes de arrancar:

```
dotnet user-secrets set "AdminInicial:Contrasena" "<contraseña temporal de 15 caracteres o más>"
```

### Servidor (Linux con Nginx)

Variables de entorno del servicio:

```
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__TurismoJimani=Server=localhost;Database=TurismoJimani;User Id=...;Password=...;TrustServerCertificate=True;
AdminInicial__Contrasena=<solo para la primera ejecución; luego se elimina>
```

Bloque de Nginx (el proxy envía la IP real y el esquema):

```
location / {
    proxy_pass         http://127.0.0.1:5000;
    proxy_http_version 1.1;
    proxy_set_header   Host $host;
    proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header   X-Forwarded-Proto $scheme;
}
```

### Opciones (`appsettings.json`, sección `Seguridad`)

| Clave | Valor por defecto | Uso |
|---|---|---|
| `ConsultarContrasenasFiltradas` | `true` | Consulta Pwned Passwords al crear o cambiar una contraseña. |
| `IntentosFallidosMaximos` | `5` | Contraseñas incorrectas seguidas antes del bloqueo. |
| `MinutosBloqueo` | `15` | Duración del bloqueo. |
| `EnviosAccesoPorMinuto` | `10` | Límite por IP de los formularios de acceso. |
| `CspSoloReporte` | `false` | `true` envía la CSP en modo de solo reporte (no bloquea); útil si una función deja de cargar. |

## Pruebas manuales

1. Iniciar sesión en `/admin` con la contraseña actual del administrador: si no cumple la política, la aplicación debe pedir el cambio y no permitir otra página hasta hacerlo.
2. Registrar un visitante con `contraseña12345` o con una contraseña de 10 caracteres: debe rechazarse.
3. Escribir mal la contraseña 5 veces seguidas: el sexto intento debe indicar el bloqueo de 15 minutos.
4. Enviar más de 10 formularios de acceso en un minuto: debe aparecer el aviso de demasiados intentos (HTTP 429).
5. Subir como foto un archivo `.txt` renombrado a `.jpg`: debe rechazarse.
6. Abrir las herramientas del navegador (pestaña Red) y comprobar los encabezados `Content-Security-Policy`, `X-Content-Type-Options` y `X-Frame-Options`; revisar en la consola que no haya bloqueos de CSP en el sitio, el panel, los mapas, los gráficos y el selector de idioma.
