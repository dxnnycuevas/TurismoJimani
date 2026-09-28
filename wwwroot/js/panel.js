// Botón hamburguesa del panel:
// - en pantallas grandes compacta el menú lateral a solo iconos (y lo recuerda en este navegador);
// - en teléfonos y tabletas abre o cierra el menú deslizable (offcanvas de Bootstrap).
(() => {
    const boton = document.getElementById("botonMenu");
    const menu = document.getElementById("panelMenu");
    if (!boton || !menu) return;

    const raiz = document.documentElement;
    const pantallaGrande = window.matchMedia("(min-width: 992px)");
    const CLAVE = "panel-menu-compacto";

    function guardar(compacto) {
        try { localStorage.setItem(CLAVE, compacto ? "1" : "0"); } catch { /* almacenamiento no disponible */ }
    }

    function actualizarEstado() {
        const abierto = pantallaGrande.matches
            ? !raiz.classList.contains("menu-compacto")
            : menu.classList.contains("show");
        boton.setAttribute("aria-expanded", String(abierto));
        boton.classList.toggle("abierto", abierto && !pantallaGrande.matches);
    }

    boton.addEventListener("click", () => {
        if (pantallaGrande.matches) {
            const compacto = raiz.classList.toggle("menu-compacto");
            guardar(compacto);
        } else {
            bootstrap.Offcanvas.getOrCreateInstance(menu).toggle();
        }
        actualizarEstado();
    });

    menu.addEventListener("shown.bs.offcanvas", actualizarEstado);
    menu.addEventListener("hidden.bs.offcanvas", actualizarEstado);
    pantallaGrande.addEventListener("change", actualizarEstado);
    actualizarEstado();
})();
