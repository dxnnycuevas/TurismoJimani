// Chat del asistente turístico: envía cada mensaje a /Asistente/EnviarMensaje con fetch
// y muestra la respuesta sin recargar la página.
(() => {
    const formulario = document.getElementById("chat-formulario");
    if (!formulario) return;

    const mensajes = document.getElementById("chat-mensajes");
    const entrada = document.getElementById("chat-texto");
    const boton = document.getElementById("chat-enviar");
    const estado = document.getElementById("chat-estado");
    const servicio = document.getElementById("chat-servicio");
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? "";

    // En el sitio público no se muestra la intención detectada (es información técnica)
    const publico = formulario.dataset.mostrarDetalle === "false";

    // Identificador de la conversación (lo asigna el servidor en la primera respuesta)
    let idSesion = null;

    function agregarMensaje(autor, texto, detalle, esError) {
        const burbuja = document.createElement("div");
        burbuja.className = "chat-mensaje " + (autor === "Tú" ? "chat-usuario" : "chat-asistente") + (esError ? " chat-error" : "");

        const nombre = document.createElement("div");
        nombre.className = "chat-autor";
        nombre.textContent = autor + ":";

        const contenido = document.createElement("div");
        contenido.className = "chat-texto";
        contenido.textContent = texto; // textContent evita inyectar HTML

        burbuja.append(nombre, contenido);

        if (detalle) {
            const pie = document.createElement("div");
            pie.className = "chat-detalle";
            pie.textContent = detalle;
            burbuja.append(pie);
        }

        mensajes.append(burbuja);
        mensajes.scrollTop = mensajes.scrollHeight;
        return burbuja;
    }

    // Solo se usan enlaces del propio sitio, https, WhatsApp o teléfono
    function enlaceSeguro(url) {
        if (typeof url !== "string") return null;
        if (url.startsWith("/") && !url.startsWith("//")) return url;
        if (url.startsWith("https://") || url.startsWith("tel:")) return url;
        return null;
    }

    function crear(etiqueta, clase, texto) {
        const elemento = document.createElement(etiqueta);
        if (clase) elemento.className = clase;
        if (texto) elemento.textContent = texto;
        return elemento;
    }

    function icono(clases) {
        const i = crear("i", clases);
        i.setAttribute("aria-hidden", "true");
        return i;
    }

    function crearBoton(url, clase, claseIcono, texto) {
        const a = crear("a", "chat-ficha__boton " + clase);
        a.href = url;
        a.target = "_blank";
        a.rel = "noopener";
        a.append(icono(claseIcono), document.createTextNode(" " + texto));
        return a;
    }

    // Una ficha por registro: foto de perfil, nombre, tipo, estado y botones de contacto y perfil
    function crearFicha(resultado) {
        const url = enlaceSeguro(resultado.url);
        const ficha = crear("article", "chat-ficha");

        const foto = crear(url ? "a" : "div", "chat-ficha__foto");
        if (url) {
            foto.href = url;
            foto.target = "_blank";
            foto.rel = "noopener";
            foto.setAttribute("aria-label", "Ver perfil de " + resultado.nombre);
        }
        const imagen = enlaceSeguro(resultado.imagen);
        if (imagen) {
            const img = crear("img");
            img.src = imagen;
            img.alt = "";
            img.loading = "lazy";
            foto.append(img);
        } else {
            foto.append(icono("fa " + (/^fa-[a-z0-9-]+$/.test(resultado.icono ?? "") ? resultado.icono : "fa-concierge-bell")));
        }
        if (resultado.numero) foto.append(crear("span", "chat-ficha__numero", String(resultado.numero)));

        const cuerpo = crear("div", "chat-ficha__cuerpo");
        const estado = crear("span", "chat-ficha__estado " + (resultado.activo ? "activo" : "inactivo"),
            resultado.activo ? "Activo" : "No disponible");
        cuerpo.append(estado, crear("h4", "chat-ficha__nombre", resultado.nombre));
        if (resultado.subtitulo) cuerpo.append(crear("div", "chat-ficha__tipo", resultado.subtitulo));

        const acciones = crear("div", "chat-ficha__acciones");
        const whatsapp = enlaceSeguro(resultado.whatsApp);
        const telefono = enlaceSeguro(resultado.telefono);
        if (whatsapp) acciones.append(crearBoton(whatsapp, "chat-ficha__boton--whatsapp", "fab fa-whatsapp", "Contactar"));
        else if (telefono) acciones.append(crearBoton(telefono, "chat-ficha__boton--whatsapp", "fa fa-phone-alt", "Llamar"));
        if (url) acciones.append(crearBoton(url, "chat-ficha__boton--perfil", "fa fa-id-card", resultado.tipo === "Prestador" ? "Ver perfil" : "Ver ficha"));
        cuerpo.append(acciones);

        ficha.append(foto, cuerpo);
        return ficha;
    }

    function agregarFichas(resultados) {
        const conFicha = (resultados ?? []).filter(r => enlaceSeguro(r.url));
        if (conFicha.length === 0) return;

        const contenedor = crear("div", "chat-fichas");
        contenedor.setAttribute("role", "list");
        conFicha.forEach(r => {
            const ficha = crearFicha(r);
            ficha.setAttribute("role", "listitem");
            contenedor.append(ficha);
        });
        mensajes.append(contenedor);
        mensajes.scrollTop = mensajes.scrollHeight;
    }

    function procesando(activo) {
        entrada.disabled = activo;
        boton.disabled = activo;
        estado.textContent = activo ? "El asistente está escribiendo…" : "";
    }

    async function comprobarServicio() {
        try {
            const respuesta = await fetch(formulario.dataset.urlEstado);
            const datos = await respuesta.json();
            servicio.textContent = datos.disponible ? "Servicio de IA: conectado" : "Servicio de IA: no disponible";
            servicio.className = "small " + (publico ? "text-white" : datos.disponible ? "text-success" : "text-danger");
        } catch {
            servicio.textContent = "";
        }
    }

    formulario.addEventListener("submit", async (evento) => {
        evento.preventDefault();

        const mensaje = entrada.value.trim();
        if (!mensaje) {
            estado.textContent = "Escribe un mensaje antes de enviarlo.";
            entrada.focus();
            return;
        }

        agregarMensaje("Tú", mensaje);
        entrada.value = "";
        procesando(true);

        try {
            const respuesta = await fetch(formulario.dataset.urlEnviar, {
                method: "POST",
                headers: { "Content-Type": "application/json", "RequestVerificationToken": token },
                body: JSON.stringify({ mensaje, idSesion })
            });

            let datos = null;
            try { datos = await respuesta.json(); } catch { /* respuesta sin JSON */ }

            if (respuesta.status === 429) {
                agregarMensaje("Asistente", "Estás enviando mensajes muy rápido. Espera un momento e inténtalo de nuevo.", null, true);
            } else if (!respuesta.ok || !datos) {
                agregarMensaje("Asistente", datos?.error ?? "No pude procesar tu mensaje. Intenta de nuevo.", null, true);
            } else {
                idSesion = datos.idSesion;
                const detalle = datos.intencion && !publico
                    ? `Intención detectada: ${datos.intencion} (${Math.round(datos.confianza * 100)}%)`
                    : null;
                const esError = ["ErrorIA", "ErrorBD", "Error"].includes(datos.tipoRespuesta);
                const hayFichas = (datos.resultados ?? []).some(r => enlaceSeguro(r.url));
                // Con fichas se muestra el texto breve; el detalle de cada registro va en su ficha
                agregarMensaje("Asistente", hayFichas && datos.textoCorto ? datos.textoCorto : datos.respuesta, detalle, esError);
                agregarFichas(datos.resultados);
                if (datos.tipoRespuesta === "ErrorIA") comprobarServicio();
            }
        } catch {
            agregarMensaje("Asistente", "No se pudo conectar con el servidor. Revisa tu conexión e inténtalo de nuevo.", null, true);
        } finally {
            window.renovarTemporizadorSesion?.(); // el mensaje también cuenta como actividad
            procesando(false);
            entrada.focus();
        }
    });

    // Enter envía el mensaje
    entrada.addEventListener("keydown", (evento) => {
        if (evento.key === "Enter" && !evento.shiftKey) {
            evento.preventDefault();
            formulario.requestSubmit();
        }
    });

    // Botones de preguntas sugeridas
    document.querySelectorAll("[data-pregunta]").forEach((boton) => {
        boton.addEventListener("click", () => {
            if (entrada.disabled) return;
            entrada.value = boton.dataset.pregunta;
            formulario.requestSubmit();
        });
    });

    agregarMensaje("Asistente",
        "¡Hola! Soy el asistente turístico de Jimaní. Pregúntame por atractivos, alojamientos, dónde comer, transporte, guías turísticos, otros servicios o rutas.");
    comprobarServicio();

    // Pregunta enviada desde otra página del sitio (/Asistente?pregunta=...): se envía una sola vez
    const parametros = new URLSearchParams(window.location.search);
    const preguntaInicial = parametros.get("pregunta")?.trim().slice(0, 500);
    if (preguntaInicial) {
        parametros.delete("pregunta");
        const consulta = parametros.toString();
        history.replaceState(null, "", window.location.pathname + (consulta ? "?" + consulta : ""));
        entrada.value = preguntaInicial;
        formulario.requestSubmit();
    } else {
        entrada.focus();
    }
})();
