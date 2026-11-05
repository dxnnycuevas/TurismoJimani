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
        const esEvento = resultado.tipo === "Evento";
        const estado = crear("span", "chat-ficha__estado " + (resultado.activo ? "activo" : "inactivo"),
            esEvento ? "Evento" : resultado.activo ? "Activo" : "No disponible");
        cuerpo.append(estado, crear("h4", "chat-ficha__nombre", resultado.nombre));
        if (resultado.subtitulo) cuerpo.append(crear("div", "chat-ficha__tipo", resultado.subtitulo));

        const acciones = crear("div", "chat-ficha__acciones");
        const whatsapp = enlaceSeguro(resultado.whatsApp);
        const telefono = enlaceSeguro(resultado.telefono);
        if (whatsapp) acciones.append(crearBoton(whatsapp, "chat-ficha__boton--whatsapp", "fab fa-whatsapp", "Contactar"));
        else if (telefono) acciones.append(crearBoton(telefono, "chat-ficha__boton--whatsapp", "fa fa-phone-alt", "Llamar"));
        if (url) {
            const textoBoton = esEvento ? "Ver evento" : resultado.tipo === "Prestador" ? "Ver perfil" : "Ver ficha";
            acciones.append(crearBoton(url, "chat-ficha__boton--perfil", esEvento ? "fa fa-calendar-alt" : "fa fa-id-card", textoBoton));
        }
        cuerpo.append(acciones);

        ficha.append(foto, cuerpo);
        return ficha;
    }

    // Respuestas rápidas ("Más barato", "Menos noches"...): al tocar una se envía como mensaje
    function agregarSugerencias(sugerencias) {
        mensajes.querySelectorAll(".chat-sugeridas").forEach((s) => s.remove());
        if (!Array.isArray(sugerencias) || sugerencias.length === 0) return;

        const contenedor = crear("div", "chat-sugeridas");
        sugerencias.forEach((texto) => {
            const b = crear("button", "chat-sugerida", texto);
            b.type = "button";
            b.addEventListener("click", () => {
                if (entrada.disabled) return;
                contenedor.remove();
                entrada.value = texto;
                formulario.requestSubmit();
            });
            contenedor.append(b);
        });
        mensajes.append(contenedor);
        mensajes.scrollTop = mensajes.scrollHeight;
    }

    // ---------- Tarjeta del planificador de presupuesto ----------

    const formatoDinero = new Intl.NumberFormat("es-DO", { maximumFractionDigits: 0 });
    const dinero = (valor) => "RD$ " + formatoDinero.format(valor);

    const partesPresupuesto = {
        pasaje: { icono: "fa-bus", nombre: "Pasaje" },
        hospedaje: { icono: "fa-bed", nombre: "Hospedaje" },
        comida: { icono: "fa-utensils", nombre: "Comida" },
        transporte: { icono: "fa-taxi", nombre: "Transporte local" }
    };

    function chip(claseIcono, texto) {
        const c = crear("span", "chat-presupuesto__chip");
        c.append(icono("fa " + claseIcono), document.createTextNode(" " + texto));
        return c;
    }

    // Barra que reparte el dinero: un tramo por parte del viaje, lo que sobra en verde
    // y, si no alcanza, una marca donde se acaba el presupuesto y el exceso rayado en rojo
    function crearBarra(p) {
        const base = Math.max(p.total, p.monto) || 1;
        const porcentaje = (valor) => (valor / base * 100).toFixed(2) + "%";

        const barra = crear("div", "chat-presupuesto__barra");
        barra.setAttribute("role", "img");
        barra.setAttribute("aria-label", `Plan de ${dinero(p.total)} con un presupuesto de ${dinero(p.monto)}`);

        const pista = crear("div", "chat-presupuesto__pista");
        const tramos = p.lineas.map((l) => ({ clase: l.clase, valor: l.subtotal, titulo: `${partesPresupuesto[l.clase]?.nombre ?? l.concepto}: ${dinero(l.subtotal)}` }));
        if (p.cabe && p.monto > p.total) tramos.push({ clase: "sobra", valor: p.monto - p.total, titulo: `Te sobran ${dinero(p.monto - p.total)}` });

        tramos.forEach((t) => {
            const tramo = crear("span", "chat-presupuesto__tramo chat-presupuesto__tramo--" + t.clase);
            tramo.title = t.titulo;
            tramo.style.setProperty("--ancho", porcentaje(t.valor));
            pista.append(tramo);
        });
        barra.append(pista);

        if (!p.cabe) {
            const exceso = crear("span", "chat-presupuesto__exceso");
            exceso.style.left = porcentaje(p.monto);
            pista.append(exceso);

            const limite = crear("span", "chat-presupuesto__limite");
            limite.style.left = porcentaje(p.monto);
            limite.title = "Tu presupuesto: " + dinero(p.monto);
            barra.append(limite);
        }

        // Los tramos crecen al aparecer (el CSS lo desactiva con movimiento reducido)
        requestAnimationFrame(() => requestAnimationFrame(() => barra.classList.add("chat-presupuesto__barra--lista")));
        return barra;
    }

    function crearLineaPresupuesto(l) {
        const parte = partesPresupuesto[l.clase] ?? partesPresupuesto.transporte;
        const fila = crear("li", "chat-presupuesto__linea chat-presupuesto__linea--" + l.clase);

        const marca = crear("span", "chat-presupuesto__icono");
        marca.append(icono("fa " + parte.icono));

        const texto = crear("div", "chat-presupuesto__descripcion");
        texto.append(crear("span", "chat-presupuesto__concepto", l.concepto));
        const url = enlaceSeguro(l.url);
        if (url) {
            const enlace = crear("a", "chat-presupuesto__opcion", l.opcion);
            enlace.href = url;
            enlace.target = "_blank";
            enlace.rel = "noopener";
            texto.append(enlace);
        } else {
            texto.append(crear("span", "chat-presupuesto__opcion", l.opcion));
        }
        texto.append(crear("span", "chat-presupuesto__detalle", l.detalle));

        fila.append(marca, texto, crear("span", "chat-presupuesto__subtotal", dinero(l.subtotal)));
        return fila;
    }

    // Botón para guardar el plan en Mi perfil (visitante con sesión) o invitación a iniciar sesión.
    // El servidor guarda el último plan que calculó en esta conversación (idSesion).
    function crearGuardarPlan() {
        const urlGuardar = formulario.dataset.urlGuardarPlan;
        const urlIngresar = formulario.dataset.urlIngresar;
        const pie = crear("div", "chat-presupuesto__guardar");

        if (urlGuardar) {
            const boton = crear("button", "jm-btn jm-btn--sm jm-btn--contorno");
            boton.type = "button";
            boton.append(icono("fa fa-heart"), document.createTextNode(" Guardar en mi perfil"));
            const aviso = crear("span", "chat-presupuesto__aviso small");
            aviso.setAttribute("role", "status");
            boton.addEventListener("click", async () => {
                boton.disabled = true;
                try {
                    const respuesta = await fetch(urlGuardar, {
                        method: "POST",
                        headers: { "Content-Type": "application/json", "RequestVerificationToken": token },
                        body: JSON.stringify({ idSesion })
                    });
                    let datos = null;
                    try { datos = await respuesta.json(); } catch { /* sin JSON */ }
                    if (respuesta.ok) {
                        aviso.replaceChildren(document.createTextNode((datos?.mensaje ?? "Plan guardado.") + " "));
                        if (datos?.url) aviso.append(crear("a", null, "Ver mis planes"));
                        aviso.lastChild instanceof HTMLAnchorElement && (aviso.lastChild.href = datos.url);
                        boton.remove();
                    } else {
                        aviso.textContent = datos?.error ?? "No se pudo guardar el plan.";
                        boton.disabled = false;
                    }
                } catch {
                    aviso.textContent = "No se pudo guardar el plan. Revisa tu conexión.";
                    boton.disabled = false;
                }
            });
            pie.append(boton, aviso);
            return pie;
        }

        if (urlIngresar) {
            const enlace = crear("a", "chat-presupuesto__aviso small", "Inicia sesión para guardar tus planes de viaje");
            enlace.href = urlIngresar;
            pie.append(icono("fa fa-heart"), document.createTextNode(" "), enlace);
            return pie;
        }
        return null;
    }

    function agregarPresupuesto(p) {
        if (!p || !Array.isArray(p.lineas) || p.lineas.length === 0) return;

        const tarjeta = crear("article", "chat-presupuesto " + (p.cabe ? "chat-presupuesto--cabe" : "chat-presupuesto--falta"));

        const cabecera = crear("header", "chat-presupuesto__cabecera");
        const titulo = crear("h4", "chat-presupuesto__titulo");
        titulo.append(icono("fa fa-suitcase-rolling"), document.createTextNode(" Tu plan de viaje"));
        const chips = crear("div", "chat-presupuesto__chips");
        chips.append(
            chip("fa-moon", p.noches === 1 ? "1 noche" : p.noches + " noches"),
            chip("fa-map-marker-alt", p.origen ? "Desde " + p.origen : "Sin pasaje"),
            chip("fa-wallet", dinero(p.monto)));
        if (p.economico || p.ajustado) chips.append(chip("fa-piggy-bank", p.economico ? "Lo más económico" : "Ajustado a tu dinero"));
        cabecera.append(titulo, chips);

        const uso = crear("div", "chat-presupuesto__uso");
        uso.append(
            crear("span", null, (p.cabe ? "Usas " : "Necesitas ") + dinero(p.total)),
            crear("span", null, (p.cabe ? "de " : "tienes ") + dinero(p.monto)));

        const lineas = crear("ul", "chat-presupuesto__lineas");
        p.lineas.forEach((l) => lineas.append(crearLineaPresupuesto(l)));

        const total = crear("div", "chat-presupuesto__total");
        total.append(crear("span", null, "Total del viaje"), crear("strong", null, dinero(p.total)));

        const diferencia = Math.abs(p.monto - p.total);
        const resultado = crear("div", "chat-presupuesto__resultado");
        resultado.append(
            icono("fa " + (p.cabe ? "fa-check-circle" : "fa-exclamation-triangle")),
            crear("span", null, !p.cabe ? `Te faltan ${dinero(diferencia)} para este plan.`
                : diferencia < 1 ? "Usas justo todo tu presupuesto."
                : `Te sobran ${dinero(diferencia)} para excursiones, guías y recuerdos.`));

        tarjeta.append(cabecera, crearBarra(p), uso, lineas, total, resultado);
        const guardar = crearGuardarPlan();
        if (guardar) tarjeta.append(guardar);
        mensajes.append(tarjeta);
        mensajes.scrollTop = mensajes.scrollHeight;
    }

    // ---------- Tarjeta de detalle (ruta, evento, lugar, prestador, ubicación, dato curioso) ----------

    const accionesDetalle = {
        ficha: "fa fa-id-card",
        whatsapp: "fab fa-whatsapp",
        telefono: "fa fa-phone-alt",
        mapa: "fa fa-map-marked-alt",
        enlace: "fa fa-external-link-alt"
    };

    const iconoValido = (nombre, otro) => "fa " + (/^fa-[a-z0-9-]+$/.test(nombre ?? "") ? nombre : otro);

    function crearParada(nombre, clase) {
        const parada = crear("div", "chat-tarjeta__parada chat-tarjeta__parada--" + clase);
        const textos = crear("span", "chat-tarjeta__textos");
        textos.append(crear("span", "chat-tarjeta__etiqueta", clase === "origen" ? "Sale de" : "Llega a"), crear("span", "chat-tarjeta__lugar", nombre));
        parada.append(crear("span", "chat-tarjeta__punto"), textos);
        return parada;
    }

    function crearRecorrido(origen, destino) {
        const recorrido = crear("div", "chat-tarjeta__recorrido");
        const linea = crear("div", "chat-tarjeta__linea");
        linea.append(icono("fa fa-route"));
        recorrido.append(crearParada(origen ?? "—", "origen"), linea, crearParada(destino ?? "—", "destino"));
        return recorrido;
    }

    // Lista de rutas: cada tarjeta marca de dónde sale y a dónde llega; al tocarla se piden sus detalles
    function agregarRutas(resultados) {
        const rutas = (resultados ?? []).filter((r) => r.tipo === "Ruta" && r.numero && (r.origen || r.destino));
        if (rutas.length === 0) return false;

        const lista = crear("div", "chat-rutas");
        rutas.forEach((r) => {
            const tarjeta = crear("button", "chat-tarjeta chat-tarjeta--ruta chat-ruta");
            tarjeta.type = "button";
            const titulo = crear("span", "chat-ruta__titulo", r.numero + ". " + r.nombre);
            const datos = r.detalle?.split(", ").slice(1).join(" · ");
            tarjeta.append(titulo, crearRecorrido(r.origen, r.destino));
            if (datos) tarjeta.append(crear("span", "chat-ruta__datos", datos));
            tarjeta.addEventListener("click", () => {
                if (entrada.disabled) return;
                entrada.value = "la " + r.numero;
                formulario.requestSubmit();
            });
            lista.append(tarjeta);
        });
        mensajes.append(lista);
        mensajes.scrollTop = mensajes.scrollHeight;
        return true;
    }

    function agregarDetalle(d) {
        if (!d || !d.titulo) return;
        const clase = /^[a-z]+$/.test(d.clase ?? "") ? d.clase : "lugar";
        const tarjeta = crear("article", "chat-tarjeta chat-tarjeta--" + clase);

        // Cabecera: foto con el título encima, o banda de color con el icono (o el calendario de un evento)
        const imagen = enlaceSeguro(d.imagen);
        const cabecera = crear("header", "chat-tarjeta__cabecera" + (imagen ? " chat-tarjeta__cabecera--foto" : ""));
        if (imagen) {
            const img = crear("img", "chat-tarjeta__foto");
            img.src = imagen;
            img.alt = "";
            img.loading = "lazy";
            cabecera.append(img);
        }
        if (d.fecha) {
            const calendario = crear("div", "chat-tarjeta__calendario");
            calendario.setAttribute("aria-hidden", "true");
            calendario.append(crear("span", "chat-tarjeta__semana", d.fecha.diaSemana), crear("strong", null, d.fecha.dia), crear("span", null, d.fecha.mes));
            cabecera.append(calendario);
        } else {
            const marca = crear("span", "chat-tarjeta__icono");
            marca.append(icono(iconoValido(d.icono, "fa-info-circle")));
            cabecera.append(marca);
        }
        const titulos = crear("div", "chat-tarjeta__titulos");
        if (d.subtitulo) titulos.append(crear("span", "chat-tarjeta__subtitulo", d.subtitulo));
        titulos.append(crear("h4", "chat-tarjeta__titulo", d.titulo));
        cabecera.append(titulos);

        const cuerpo = crear("div", "chat-tarjeta__cuerpo");

        // Recorrido de una ruta: origen ●——● destino
        if (d.origen || d.destino) {
            cuerpo.append(crearRecorrido(d.origen, d.destino));
        }

        // Mapa de Google con la línea azul del recorrido
        if (typeof d.mapaRuta === "string" && d.mapaRuta.startsWith("https://maps.google.com/")) {
            const mapa = crear("iframe", "chat-tarjeta__mapa");
            mapa.src = d.mapaRuta;
            mapa.title = "Recorrido de " + d.titulo;
            mapa.loading = "lazy";
            mapa.referrerPolicy = "no-referrer-when-downgrade";
            cuerpo.append(mapa);
        }

        if (d.resumen) cuerpo.append(crear("p", "chat-tarjeta__resumen", d.resumen));
        (d.secciones ?? []).filter((s) => !s.titulo).forEach((s) => cuerpo.append(crear("p", "chat-tarjeta__texto", s.texto)));

        if (Array.isArray(d.datos) && d.datos.length) {
            const lista = crear("dl", "chat-tarjeta__datos");
            d.datos.forEach((x) => {
                const dato = crear("div", "chat-tarjeta__dato");
                const etiqueta = crear("dt");
                etiqueta.append(icono(iconoValido(x.icono, "fa-info-circle")), document.createTextNode(" " + x.etiqueta));
                dato.append(etiqueta, crear("dd", null, x.valor));
                lista.append(dato);
            });
            cuerpo.append(lista);
        }

        // Párrafos con título: el dato curioso siempre visible; los demás se abren al tocarlos (abiertos si son pocos)
        const conTitulo = (d.secciones ?? []).filter((s) => s.titulo);
        const pocas = conTitulo.filter((s) => s.titulo !== "Dato curioso").length <= 2;
        conTitulo.forEach((s) => {
            if (s.titulo === "Dato curioso") {
                const caja = crear("div", "chat-tarjeta__curioso");
                const titulo = crear("strong");
                titulo.append(icono("fa fa-lightbulb"), document.createTextNode(" Dato curioso"));
                caja.append(titulo, crear("p", null, s.texto));
                cuerpo.append(caja);
                return;
            }
            const seccion = crear("details", "chat-tarjeta__seccion");
            seccion.open = pocas;
            seccion.append(crear("summary", null, s.titulo), crear("p", null, s.texto));
            cuerpo.append(seccion);
        });

        const acciones = crear("div", "chat-tarjeta__acciones");
        (d.acciones ?? []).forEach((a) => {
            const url = enlaceSeguro(a.url);
            if (!url) return;
            const tipo = accionesDetalle[a.tipo] ? a.tipo : "enlace";
            const boton = crear("a", "chat-tarjeta__accion chat-tarjeta__accion--" + tipo);
            boton.href = url;
            if (!url.startsWith("tel:")) {
                boton.target = "_blank";
                boton.rel = "noopener";
            }
            boton.append(icono(accionesDetalle[tipo]), document.createTextNode(" " + a.texto));
            acciones.append(boton);
        });
        if (acciones.childElementCount) cuerpo.append(acciones);

        tarjeta.append(cabecera, cuerpo);
        mensajes.append(tarjeta);
        mensajes.scrollTop = mensajes.scrollHeight;
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
                const hayTarjetas = (datos.resultados ?? []).some(r => enlaceSeguro(r.url) || r.tipo === "Ruta") || !!datos.presupuesto || !!datos.detalle;
                // Con tarjetas se muestra el texto breve; el resto de la información va en ellas
                agregarMensaje("Asistente", hayTarjetas && datos.textoCorto ? datos.textoCorto : datos.respuesta, detalle, esError);
                agregarPresupuesto(datos.presupuesto);
                agregarDetalle(datos.detalle);
                // La tarjeta de detalle ya trae foto y botones de contacto: no se repite la ficha
                if (!datos.detalle && !agregarRutas(datos.resultados)) agregarFichas(datos.resultados);
                agregarSugerencias(datos.sugerencias);
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

    // Botón para agrandar el chat (sitio público): mismo diseño, casi a pantalla completa.
    // Se reduce con el mismo botón, con Esc o tocando fuera del chat.
    const botonAgrandar = document.getElementById("chat-agrandar");
    const caja = formulario.closest(".chat-caja");
    if (botonAgrandar && caja) {
        let fondo = null;

        const agrandar = (grande) => {
            caja.classList.toggle("chat-caja--grande", grande);
            document.body.classList.toggle("chat-grande-abierto", grande);

            const texto = grande ? "Reducir el chat" : "Agrandar el chat";
            botonAgrandar.setAttribute("aria-pressed", String(grande));
            botonAgrandar.setAttribute("aria-label", texto);
            botonAgrandar.title = texto;
            botonAgrandar.querySelector("i").className = "fa " + (grande ? "fa-compress" : "fa-expand");

            if (grande) {
                fondo = crear("div", "chat-fondo");
                fondo.addEventListener("click", () => agrandar(false));
                caja.before(fondo);
            } else {
                fondo?.remove();
                fondo = null;
            }

            mensajes.scrollTop = mensajes.scrollHeight;
            entrada.focus();
        };

        botonAgrandar.addEventListener("click", () => agrandar(!caja.classList.contains("chat-caja--grande")));
        document.addEventListener("keydown", (evento) => {
            if (evento.key === "Escape" && caja.classList.contains("chat-caja--grande")) agrandar(false);
        });
    }

    // Botones de preguntas sugeridas
    document.querySelectorAll("[data-pregunta]").forEach((boton) => {
        boton.addEventListener("click", () => {
            if (entrada.disabled) return;
            entrada.value = boton.dataset.pregunta;
            formulario.requestSubmit();
        });
    });

    agregarMensaje("Asistente",
        "¡Hola! Soy el asistente turístico de Jimaní. Pregúntame por atractivos, alojamientos, dónde comer, transporte, guías turísticos, otros servicios, rutas o eventos. También puedo armarte un presupuesto de viaje o contarte un dato curioso.");
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
