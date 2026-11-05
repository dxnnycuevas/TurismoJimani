// Agrega a cada casilla de contraseña un botón con un ojo para ver u ocultar lo que se está escribiendo.
(() => {
    document.querySelectorAll('input[type="password"]').forEach((input) => {
        // En .form-floating la etiqueta depende de ir justo después del input: se usa esa misma caja
        let caja = input.closest(".form-floating");
        if (!caja) {
            caja = document.createElement("span");
            caja.className = "ver-contrasena-caja";
            input.before(caja);
            caja.append(input);
        }
        input.classList.add("ver-contrasena-input");

        const boton = document.createElement("button");
        boton.type = "button";
        boton.className = "ver-contrasena";
        const icono = document.createElement("i");
        icono.setAttribute("aria-hidden", "true");
        boton.append(icono);

        const mostrar = (visible) => {
            input.type = visible ? "text" : "password";
            icono.className = "fa " + (visible ? "fa-eye-slash" : "fa-eye");
            const texto = visible ? "Ocultar contraseña" : "Mostrar contraseña";
            boton.title = texto;
            boton.setAttribute("aria-label", texto);
            boton.setAttribute("aria-pressed", String(visible));
        };

        boton.addEventListener("click", () => {
            mostrar(input.type === "password");
            input.focus();
        });

        // Al enviar vuelve a ocultarse, para que el navegador no la trate como texto normal
        input.form?.addEventListener("submit", () => mostrar(false));

        mostrar(false);
        caja.append(boton);
    });
})();
