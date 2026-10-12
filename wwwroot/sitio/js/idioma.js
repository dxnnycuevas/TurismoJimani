// Selector de idioma del sitio público. El sitio está escrito en español; para inglés, francés y
// criollo haitiano se usa el traductor web de Google, que también traduce lo que aparece después
// (por ejemplo, las respuestas del chat). Google solo se carga si el visitante eligió otro idioma.
// El idioma se guarda en la cookie "googtrans" (la que lee el traductor) y se recuerda entre páginas.
(() => {
    const IDIOMAS = ["es", "en", "fr", "ht"];

    function idiomaActual() {
        const coincidencia = document.cookie.match(/(?:^|;\s*)googtrans=\/es\/([a-z-]+)/i);
        return coincidencia && IDIOMAS.includes(coincidencia[1]) ? coincidencia[1] : "es";
    }

    // Google puede guardar la cookie con o sin dominio: se escriben y borran todas las variantes
    function dominios() {
        const host = location.hostname;
        const lista = [""];
        if (host.includes(".") && !/^[\d.]+$/.test(host)) {
            lista.push(host, "." + host.split(".").slice(-2).join("."));
        }
        return lista;
    }

    function guardarIdioma(codigo) {
        const valor = codigo === "es" ? "" : `/es/${codigo}`;
        const vigencia = codigo === "es" ? "expires=Thu, 01 Jan 1970 00:00:00 GMT" : "max-age=31536000";
        for (const dominio of dominios()) {
            document.cookie = `googtrans=${valor}; ${vigencia}; path=/${dominio ? `; domain=${dominio}` : ""}; SameSite=Lax`;
        }
    }

    function cargarTraductor() {
        const contenedor = document.createElement("div");
        contenedor.id = "traductor-google";
        contenedor.hidden = true;
        document.body.append(contenedor);

        window.iniciarTraductorGoogle = () => {
            new google.translate.TranslateElement(
                { pageLanguage: "es", includedLanguages: "en,fr,ht", autoDisplay: false },
                "traductor-google");
        };

        const script = document.createElement("script");
        script.src = "https://translate.google.com/translate_a/element.js?cb=iniciarTraductorGoogle";
        script.async = true;
        document.head.append(script);
    }

    const actual = idiomaActual();

    document.querySelectorAll("[data-idioma-actual]").forEach((e) => { e.textContent = actual.toUpperCase(); });

    document.querySelectorAll(".jm-idioma__opcion").forEach((boton) => {
        const esActual = boton.dataset.idioma === actual;
        boton.classList.toggle("activo", esActual);
        if (esActual) boton.setAttribute("aria-current", "true");

        boton.addEventListener("click", () => {
            if (boton.dataset.idioma === idiomaActual()) return;
            guardarIdioma(boton.dataset.idioma);
            location.reload();
        });
    });

    if (actual !== "es") cargarTraductor();
})();
