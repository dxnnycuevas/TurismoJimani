// Gráficos del tablero del panel (Chart.js). Los datos vienen del servidor en #datos-graficos.
// Estilo: una sola serie por gráfico (sin leyenda: el título dice qué se muestra), marcas finas,
// cuadrícula tenue, etiquetas en tinta (nunca del color de la serie) y cada gráfico con su vista de tabla.
(() => {
    const COLOR = {
        serie: "#2a78d6",          // azul validado (contraste >= 3:1 sobre la tarjeta blanca)
        serieHover: "#3987e5",
        relleno: "rgba(42, 120, 214, 0.10)",
        superficie: "#ffffff",
        tinta: "#0b0b0b",
        tinta2: "#52514e",
        apagado: "#898781",
        cuadricula: "#e1e0d9",
        eje: "#c3c2b7"
    };
    const FUENTE = 'Inter, system-ui, -apple-system, "Segoe UI", sans-serif';
    const numero = new Intl.NumberFormat("es-DO");

    // Vista de gráfico / vista de tabla (la tabla es la versión accesible de cada gráfico)
    document.querySelectorAll("[data-alternar]").forEach((boton) => {
        boton.addEventListener("click", () => {
            const nombre = boton.dataset.alternar;
            const mostrarTabla = boton.getAttribute("aria-expanded") !== "true";
            document.querySelector(`[data-vista="grafico"][data-grafico="${nombre}"]`).hidden = mostrarTabla;
            document.querySelector(`[data-vista="tabla"][data-grafico="${nombre}"]`).hidden = !mostrarTabla;
            boton.setAttribute("aria-expanded", String(mostrarTabla));
            boton.textContent = mostrarTabla ? "Ver gráfico" : "Ver tabla";
        });
    });

    const fuenteDatos = document.getElementById("datos-graficos");
    if (!fuenteDatos || typeof Chart === "undefined") return;
    const datos = JSON.parse(fuenteDatos.textContent);

    Chart.defaults.font.family = FUENTE;
    Chart.defaults.font.size = 12;
    Chart.defaults.color = COLOR.tinta2;
    Chart.defaults.plugins.legend.display = false;
    Chart.defaults.animation.duration = window.matchMedia("(prefers-reduced-motion: reduce)").matches ? 0 : 500;

    // Tooltip: el valor manda (tinta fuerte) y la etiqueta acompaña (tinta secundaria)
    const tooltip = {
        backgroundColor: COLOR.superficie,
        borderColor: "rgba(11, 11, 11, 0.10)",
        borderWidth: 1,
        cornerRadius: 8,
        padding: 10,
        titleColor: COLOR.tinta2,
        titleFont: { weight: "500" },
        bodyColor: COLOR.tinta,
        bodyFont: { weight: "600" },
        displayColors: true,
        usePointStyle: true,
        boxPadding: 6,
        callbacks: {
            labelPointStyle: () => ({ pointStyle: "line", rotation: 0 })
        }
    };

    // Línea vertical que sigue al puntero en el gráfico de línea
    const cruz = {
        id: "cruz",
        afterDatasetsDraw(chart) {
            const activos = chart.tooltip?.getActiveElements?.() ?? [];
            if (!activos.length) return;
            const x = Math.round(activos[0].element.x) + 0.5;
            const { top, bottom } = chart.chartArea;
            const ctx = chart.ctx;
            ctx.save();
            ctx.strokeStyle = COLOR.eje;
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.moveTo(x, top);
            ctx.lineTo(x, bottom);
            ctx.stroke();
            ctx.restore();
        }
    };

    // Valor al final de cada barra (en tinta, fuera de la barra)
    const valorEnPunta = {
        id: "valorEnPunta",
        afterDatasetsDraw(chart) {
            const ctx = chart.ctx;
            const valores = chart.data.datasets[0].data;
            ctx.save();
            ctx.font = `600 12px ${FUENTE}`;
            ctx.fillStyle = COLOR.tinta;
            ctx.textBaseline = "middle";
            chart.getDatasetMeta(0).data.forEach((barra, i) => {
                ctx.fillText(numero.format(valores[i]), barra.x + 6, barra.y);
            });
            ctx.restore();
        }
    };

    // Etiqueta del último valor de la línea (solo el extremo, no un número en cada punto)
    const valorFinal = {
        id: "valorFinal",
        afterDatasetsDraw(chart) {
            const meta = chart.getDatasetMeta(0);
            const ultimo = meta.data[meta.data.length - 1];
            if (!ultimo) return;
            const valores = chart.data.datasets[0].data;
            const ctx = chart.ctx;
            ctx.save();
            ctx.font = `600 12px ${FUENTE}`;
            ctx.fillStyle = COLOR.tinta;
            ctx.textAlign = "right";
            ctx.textBaseline = "bottom";
            ctx.fillText(numero.format(valores[valores.length - 1]), ultimo.x - 8, ultimo.y - 8);
            ctx.restore();
        }
    };

    // ---- Consultas por día (línea con área suave) ----
    const lienzoConsultas = document.getElementById("grafico-consultas");
    if (lienzoConsultas) {
        const serie = datos.consultasPorDia;
        const ultimo = serie.length - 1;
        new Chart(lienzoConsultas, {
            type: "line",
            data: {
                labels: serie.map((p) => p.etiqueta),
                datasets: [{
                    label: "Consultas",
                    data: serie.map((p) => p.valor),
                    borderColor: COLOR.serie,
                    backgroundColor: COLOR.relleno,
                    fill: true,
                    borderWidth: 2,
                    borderCapStyle: "round",
                    borderJoinStyle: "round",
                    tension: 0.3,
                    // Solo se marca el último día; al pasar el puntero se marca el día señalado
                    pointRadius: serie.map((_, i) => (i === ultimo ? 4 : 0)),
                    pointBackgroundColor: COLOR.serie,
                    pointBorderColor: COLOR.superficie,
                    pointBorderWidth: 2,
                    pointHoverRadius: 5,
                    pointHoverBackgroundColor: COLOR.serie,
                    pointHoverBorderColor: COLOR.superficie,
                    pointHoverBorderWidth: 2,
                    pointHitRadius: 14
                }]
            },
            options: {
                maintainAspectRatio: false,
                layout: { padding: { top: 18, right: 8 } },
                interaction: { mode: "index", intersect: false },
                scales: {
                    x: {
                        grid: { display: false },
                        border: { color: COLOR.eje },
                        ticks: { color: COLOR.apagado, maxRotation: 0, autoSkip: true, maxTicksLimit: 7 }
                    },
                    y: {
                        beginAtZero: true,
                        grace: "10%",
                        grid: { color: COLOR.cuadricula, lineWidth: 1 },
                        border: { display: false },
                        ticks: { color: COLOR.apagado, precision: 0, maxTicksLimit: 5, callback: (v) => numero.format(v) }
                    }
                },
                plugins: {
                    tooltip: {
                        ...tooltip,
                        callbacks: {
                            ...tooltip.callbacks,
                            label: (c) => ` ${numero.format(c.parsed.y)} ${c.parsed.y === 1 ? "consulta" : "consultas"}`
                        }
                    }
                }
            },
            plugins: [cruz, valorFinal]
        });
    }

    // ---- Barras horizontales (una serie, mismo color en todas las barras) ----
    function barras(id, serie, unidad) {
        const lienzo = document.getElementById(id);
        if (!lienzo) return;
        new Chart(lienzo, {
            type: "bar",
            data: {
                labels: serie.map((p) => p.etiqueta),
                datasets: [{
                    label: unidad,
                    data: serie.map((p) => p.valor),
                    backgroundColor: COLOR.serie,
                    hoverBackgroundColor: COLOR.serieHover,
                    borderRadius: 4,           // punta redondeada...
                    borderSkipped: "start",    // ...y recta en la línea base
                    maxBarThickness: 22,
                    categoryPercentage: 0.8,
                    barPercentage: 0.9
                }]
            },
            options: {
                indexAxis: "y",
                maintainAspectRatio: false,
                layout: { padding: { right: 40 } },
                // Toda la fila responde al puntero (área más grande que la barra)
                interaction: { mode: "index", axis: "y", intersect: false },
                scales: {
                    x: { display: false, beginAtZero: true, grace: "8%" },
                    y: {
                        grid: { display: false },
                        border: { color: COLOR.eje },
                        ticks: { color: COLOR.tinta2, autoSkip: false }
                    }
                },
                plugins: {
                    tooltip: {
                        ...tooltip,
                        callbacks: {
                            ...tooltip.callbacks,
                            labelPointStyle: () => ({ pointStyle: "rect", rotation: 0 }),
                            label: (c) => ` ${numero.format(c.parsed.x)} ${unidad}`
                        }
                    }
                }
            },
            plugins: [valorEnPunta]
        });
    }

    barras("grafico-prestadores", datos.prestadoresPorGrupo, "prestadores");
    barras("grafico-intenciones", datos.intenciones, "consultas");
})();
