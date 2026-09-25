// Sitio público de Turismo Jimaní: interacción (datos curiosos al azar, símbolos que giran,
// filtros, contadores, navegación flotante y brillo del vidrio líquido).
(() => {
    const reducirMovimiento = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    // ---------- Navegación flotante y botón "volver arriba" ----------
    const nav = document.querySelector(".jm-nav");
    const arriba = document.querySelector(".jm-arriba");

    function alDesplazar() {
        nav?.classList.toggle("jm-nav--flotante", window.scrollY > 24);
        arriba?.classList.toggle("visible", window.scrollY > 500);
    }

    window.addEventListener("scroll", alDesplazar, { passive: true });
    alDesplazar();

    arriba?.addEventListener("click", (evento) => {
        evento.preventDefault();
        window.scrollTo({ top: 0, behavior: reducirMovimiento ? "auto" : "smooth" });
    });

    // ---------- Aparición de secciones al desplazarse ----------
    const revelar = document.querySelectorAll(".jm-revelar");
    if (!reducirMovimiento && "IntersectionObserver" in window) {
        const observador = new IntersectionObserver((entradas) => {
            entradas.forEach((entrada) => {
                if (entrada.isIntersecting) {
                    entrada.target.classList.add("visible");
                    observador.unobserve(entrada.target);
                }
            });
        }, { threshold: 0.12 });
        revelar.forEach((el) => observador.observe(el));
    } else {
        revelar.forEach((el) => el.classList.add("visible"));
    }

    // ---------- Brillo del vidrio líquido que sigue al puntero ----------
    if (!reducirMovimiento) {
        document.querySelectorAll(".jm-liquido").forEach((el) => {
            el.addEventListener("pointermove", (evento) => {
                const caja = el.getBoundingClientRect();
                el.style.setProperty("--mx", `${((evento.clientX - caja.left) / caja.width) * 100}%`);
                el.style.setProperty("--my", `${((evento.clientY - caja.top) / caja.height) * 100}%`);
            });
        });
    }

    // ---------- Datos curiosos al azar ----------
    // Cualquier elemento con [data-dato] pide un dato. Si tiene data-dato-destino, se muestra ahí;
    // si no, en el aviso flotante (#jm-toast).
    const urlDato = document.body.dataset.urlDato;
    const toast = document.getElementById("jm-toast");
    let ultimoDato = null;
    let temporizadorToast = null;

    async function pedirDato() {
        const url = ultimoDato ? `${urlDato}?excluir=${ultimoDato}` : urlDato;
        const respuesta = await fetch(url, { headers: { Accept: "application/json" } });
        if (!respuesta.ok) throw new Error("Sin respuesta");
        const dato = await respuesta.json();
        ultimoDato = dato.id;
        return dato;
    }

    function pintarDato(contenedor, dato) {
        const categoria = contenedor.querySelector("[data-dato-categoria]");
        const texto = contenedor.querySelector("[data-dato-texto]");
        const fuente = contenedor.querySelector("[data-dato-fuente]");
        if (categoria) categoria.textContent = dato.categoria;
        if (texto) texto.textContent = dato.texto;
        if (fuente) fuente.textContent = dato.fuente ? `Fuente: ${dato.fuente}` : "";
        contenedor.classList.remove("jm-aparecer");
        void contenedor.offsetWidth; // reinicia la animación
        contenedor.classList.add("jm-aparecer");
    }

    function mostrarToast(dato) {
        if (!toast) return;
        pintarDato(toast, dato);
        toast.hidden = false;
        requestAnimationFrame(() => toast.classList.add("visible"));
        clearTimeout(temporizadorToast);
        temporizadorToast = setTimeout(cerrarToast, 9000);
    }

    function cerrarToast() {
        if (!toast) return;
        toast.classList.remove("visible");
        setTimeout(() => { toast.hidden = true; }, 500);
    }

    toast?.querySelector("[data-toast-cerrar]")?.addEventListener("click", cerrarToast);

    document.querySelectorAll("[data-dato]").forEach((boton) => {
        boton.addEventListener("click", async () => {
            boton.classList.remove("jm-rebote");
            void boton.offsetWidth;
            boton.classList.add("jm-rebote");

            let dato;
            try {
                dato = await pedirDato();
            } catch {
                dato = { categoria: "Ups", texto: "No pudimos cargar un dato ahora. Inténtalo de nuevo en un momento." };
            }

            const destino = boton.dataset.datoDestino && document.querySelector(boton.dataset.datoDestino);
            if (destino) pintarDato(destino, dato);
            else mostrarToast(dato);
        });
    });

    // ---------- Símbolos taínos que giran ----------
    document.querySelectorAll(".jm-simbolo").forEach((boton) => {
        boton.addEventListener("click", () => {
            const volteado = boton.classList.toggle("volteado");
            boton.setAttribute("aria-pressed", volteado ? "true" : "false");
        });
    });

    // ---------- Filtro de destinos por tipo (portada) ----------
    const filtros = document.querySelectorAll(".jm-filtros [data-filtro]");
    const destinos = document.querySelectorAll(".destino[data-tipo]");
    filtros.forEach((boton) => {
        boton.addEventListener("click", () => {
            const filtro = boton.dataset.filtro;
            filtros.forEach((b) => {
                const activo = b === boton;
                b.classList.toggle("activo", activo);
                b.setAttribute("aria-pressed", activo ? "true" : "false");
            });
            destinos.forEach((destino) => {
                destino.hidden = filtro !== "*" && destino.dataset.tipo !== filtro;
            });
        });
    });

    // ---------- Contadores animados ----------
    const contadores = document.querySelectorAll("[data-contar]");
    function animarContador(el) {
        const final = Number(el.dataset.contar) || 0;
        if (reducirMovimiento || final === 0) { el.textContent = final; return; }
        const inicio = performance.now();
        const duracion = 1200;
        function paso(ahora) {
            const avance = Math.min((ahora - inicio) / duracion, 1);
            el.textContent = Math.round(final * (1 - Math.pow(1 - avance, 3)));
            if (avance < 1) requestAnimationFrame(paso);
        }
        requestAnimationFrame(paso);
    }

    if ("IntersectionObserver" in window) {
        const observador = new IntersectionObserver((entradas) => {
            entradas.forEach((entrada) => {
                if (entrada.isIntersecting) {
                    animarContador(entrada.target);
                    observador.unobserve(entrada.target);
                }
            });
        }, { threshold: 0.5 });
        contadores.forEach((el) => observador.observe(el));
    } else {
        contadores.forEach((el) => { el.textContent = el.dataset.contar; });
    }
})();
