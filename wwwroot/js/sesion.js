// Cierre de sesión por inactividad en el panel de administración.
// El servidor invalida la sesión tras N minutos sin peticiones; este temporizador cuenta desde
// la última petición (carga de página o mensaje del chat) y lleva al acceso con el aviso.
(() => {
    const script = document.getElementById("sesion-inactividad");
    if (!script) return;

    const minutos = Number(script.dataset.minutos) || 20;
    const urlExpirada = script.dataset.urlExpirada;
    let temporizador = null;

    function reiniciar() {
        clearTimeout(temporizador);
        // Unos segundos de margen para que la cookie ya haya caducado en el servidor
        temporizador = setTimeout(() => window.location.assign(urlExpirada), minutos * 60 * 1000 + 5000);
    }

    // Las peticiones en segundo plano (p. ej. el chat) también renuevan la sesión
    window.renovarTemporizadorSesion = reiniciar;
    reiniciar();
})();
