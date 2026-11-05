// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// Confirmación antes de enviar formularios marcados con data-confirmar="mensaje".
// Sustituye a los atributos onsubmit="..." en línea, que la política CSP del sitio no permite.
document.addEventListener("submit", (evento) => {
    const formulario = evento.target;
    if (!(formulario instanceof HTMLFormElement)) return;
    const mensaje = formulario.dataset.confirmar;
    if (mensaje && !window.confirm(mensaje)) evento.preventDefault();
});
