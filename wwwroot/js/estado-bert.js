// Estado del servicio BERT en el panel: consulta /EstadoBert/Datos cada 5 segundos y dibuja la memoria.
// La actualización se pausa sola a los 10 minutos para no mantener abierta la sesión indefinidamente.
(() => {
    const script = document.getElementById("estado-bert");
    if (!script) return;

    const URL_DATOS = script.dataset.url;
    const INTERVALO = 5000;
    const MUESTRAS = 60;                        // 60 × 5 s = 5 minutos
    const PAUSA_AUTOMATICA = 10 * 60 * 1000;

    const COLOR = {
        serie: "#2a78d6",
        relleno: "rgba(42, 120, 214, 0.10)",
        superficie: "#ffffff",
        tinta: "#0b0b0b",
        tinta2: "#52514e",
        apagado: "#898781",
        cuadricula: "#e1e0d9",
        eje: "#c3c2b7"
    };
    const FUENTE = 'Inter, system-ui, -apple-system, "Segoe UI", sans-serif';
    const numero = new Intl.NumberFormat("es-DO", { maximumFractionDigits: 1 });
    const $ = (id) => document.getElementById(id);

    const historial = [];      // { hora, mb }
    let temporizador = null;
    let pausado = false;
    let inicio = Date.now();

    // ---------- formatos ----------
    const memoria = (mb) => mb == null ? "—" : mb >= 1024 ? `${numero.format(mb / 1024)} GB` : `${numero.format(Math.round(mb))} MB`;
    const porcentaje = (v) => v == null || !isFinite(v) ? "—" : `${numero.format(v)} %`;
    const hora = (fecha) => fecha.toLocaleTimeString("es-DO", { hour: "2-digit", minute: "2-digit", second: "2-digit", hourCycle: "h23" });

    function duracion(segundos) {
        if (segundos == null) return "—";
        const h = Math.floor(segundos / 3600), m = Math.floor((segundos % 3600) / 60);
        if (h > 0) return `${h} h ${m} min`;
        if (m > 0) return `${m} min`;
        return `${segundos} s`;
    }

    function fechaLarga(iso) {
        if (!iso) return "—";
        const f = new Date(iso);
        return isNaN(f) ? iso : f.toLocaleString("es-DO", { dateStyle: "medium", timeStyle: "short" });
    }

    function medidor(id, valor) {
        const el = $(id);
        const v = Math.max(0, Math.min(100, valor || 0));
        el.setAttribute("aria-valuenow", String(Math.round(v)));
        el.firstElementChild.style.width = `${v}%`;
    }

    function conexion(estado) {
        const el = $("bert-conexion");
        const textos = {
            ok: ["fa-check-circle estado--bien", "Conectado"],
            cargando: ["fa-circle-notch fa-spin text-secondary", "Cargando el modelo…"],
            caido: ["fa-times-circle estado--critico", "Sin conexión"],
            pausa: ["fa-pause-circle text-secondary", "Actualización en pausa"]
        }[estado];
        el.querySelector("i").className = `fa ${textos[0]}`;
        el.querySelector("span").textContent = textos[1];
        $("bert-sin-conexion").hidden = estado !== "caido";
    }

    // ---------- gráfico de memoria ----------
    let grafico = null;
    if (typeof Chart !== "undefined") {
        Chart.defaults.font.family = FUENTE;
        Chart.defaults.font.size = 12;
        Chart.defaults.color = COLOR.tinta2;
        Chart.defaults.plugins.legend.display = false;

        const cruz = {
            id: "cruz",
            afterDatasetsDraw(chart) {
                const activos = chart.tooltip?.getActiveElements?.() ?? [];
                if (!activos.length) return;
                const x = Math.round(activos[0].element.x) + 0.5;
                const { top, bottom } = chart.chartArea;
                chart.ctx.save();
                chart.ctx.strokeStyle = COLOR.eje;
                chart.ctx.lineWidth = 1;
                chart.ctx.beginPath();
                chart.ctx.moveTo(x, top);
                chart.ctx.lineTo(x, bottom);
                chart.ctx.stroke();
                chart.ctx.restore();
            }
        };

        // Solo se rotula el último valor (no un número en cada punto)
        const valorFinal = {
            id: "valorFinal",
            afterDatasetsDraw(chart) {
                const datos = chart.data.datasets[0].data;
                const punto = chart.getDatasetMeta(0).data[datos.length - 1];
                if (!punto) return;
                chart.ctx.save();
                chart.ctx.font = `600 12px ${FUENTE}`;
                chart.ctx.fillStyle = COLOR.tinta;
                chart.ctx.textAlign = "right";
                chart.ctx.textBaseline = "bottom";
                chart.ctx.fillText(memoria(datos[datos.length - 1]), punto.x - 8, punto.y - 8);
                chart.ctx.restore();
            }
        };

        grafico = new Chart($("grafico-memoria"), {
            type: "line",
            data: {
                labels: [],
                datasets: [{
                    label: "Memoria",
                    data: [],
                    borderColor: COLOR.serie,
                    backgroundColor: COLOR.relleno,
                    fill: true,
                    borderWidth: 2,
                    borderCapStyle: "round",
                    borderJoinStyle: "round",
                    tension: 0.25,
                    pointRadius: (ctx) => ctx.dataIndex === ctx.dataset.data.length - 1 ? 4 : 0,
                    pointBackgroundColor: COLOR.serie,
                    pointBorderColor: COLOR.superficie,
                    pointBorderWidth: 2,
                    pointHoverRadius: 5,
                    pointHitRadius: 14
                }]
            },
            options: {
                maintainAspectRatio: false,
                animation: false,
                layout: { padding: { top: 18, right: 8 } },
                interaction: { mode: "index", intersect: false },
                scales: {
                    x: {
                        grid: { display: false },
                        border: { color: COLOR.eje },
                        ticks: { color: COLOR.apagado, maxRotation: 0, autoSkip: true, maxTicksLimit: 6 }
                    },
                    y: {
                        beginAtZero: true,
                        grace: "15%",
                        grid: { color: COLOR.cuadricula, lineWidth: 1 },
                        border: { display: false },
                        ticks: { color: COLOR.apagado, maxTicksLimit: 5, callback: (v) => memoria(v) }
                    }
                },
                plugins: {
                    tooltip: {
                        backgroundColor: COLOR.superficie,
                        borderColor: "rgba(11, 11, 11, 0.10)",
                        borderWidth: 1,
                        cornerRadius: 8,
                        padding: 10,
                        titleColor: COLOR.tinta2,
                        bodyColor: COLOR.tinta,
                        bodyFont: { weight: "600" },
                        usePointStyle: true,
                        boxPadding: 6,
                        callbacks: {
                            labelPointStyle: () => ({ pointStyle: "line", rotation: 0 }),
                            label: (c) => ` ${memoria(c.parsed.y)} en uso`
                        }
                    }
                }
            },
            plugins: [cruz, valorFinal]
        });
    }

    function dibujarHistorial() {
        if (grafico) {
            grafico.data.labels = historial.map((m) => hora(m.hora));
            grafico.data.datasets[0].data = historial.map((m) => m.mb);
            grafico.update("none");
        }

        // Vista de tabla (la más reciente primero)
        const cuerpo = $("bert-tabla-memoria");
        cuerpo.replaceChildren(...historial.slice().reverse().map((m) => {
            const fila = document.createElement("tr");
            const celdaHora = document.createElement("td");
            const celdaValor = document.createElement("td");
            celdaHora.textContent = hora(m.hora);
            celdaValor.textContent = numero.format(m.mb);
            fila.append(celdaHora, celdaValor);
            return fila;
        }));
    }

    // ---------- datos ----------
    function mostrar(d) {
        const mem = d.memoria ?? {};
        const total = mem.equipoTotalMb, disponible = mem.equipoDisponibleMb;
        const usoEquipo = total && disponible != null ? total - disponible : null;
        const partePorcentaje = total && mem.procesoMb != null ? mem.procesoMb / total * 100 : null;
        const usoPorcentaje = total && usoEquipo != null ? usoEquipo / total * 100 : null;

        $("bert-memoria").textContent = memoria(mem.procesoMb);
        medidor("bert-medidor-proceso", partePorcentaje);
        $("bert-memoria-detalle").textContent =
            `${porcentaje(partePorcentaje)} de la memoria del equipo · pico ${memoria(mem.picoMb)}`;

        $("bert-modelo").textContent = memoria(mem.modeloMb);
        $("bert-modelo-detalle").textContent =
            `archivo de ${memoria(mem.archivoModeloMb)} · ${d.modelo?.intenciones ?? 0} intenciones`;

        $("bert-cpu").textContent = porcentaje(d.cpu?.porcentaje);
        $("bert-cpu-detalle").textContent =
            `${d.cpu?.nucleos ?? "—"} núcleos · ${d.cpu?.hilosTorch ?? "—"} hilos de PyTorch`;

        $("bert-encendido").textContent = duracion(d.encendidoSegundos);
        $("bert-encendido-detalle").textContent =
            `${numero.format(d.actividad?.predicciones ?? 0)} preguntas clasificadas`;

        $("equipo-porcentaje").textContent = porcentaje(usoPorcentaje);
        medidor("equipo-medidor", usoPorcentaje);
        $("equipo-aviso").hidden = !(usoPorcentaje >= 90);
        $("equipo-total").textContent = memoria(total);
        $("equipo-uso").textContent = memoria(usoEquipo);
        $("equipo-disponible").textContent = memoria(disponible);
        $("equipo-bert").textContent = memoria(mem.procesoMb);

        $("modelo-base").textContent = d.modelo?.base ?? "—";
        $("modelo-entrenado").textContent = fechaLarga(d.modelo?.entrenado);
        $("modelo-exactitud").textContent = d.modelo?.exactitudValidacion != null
            ? porcentaje(d.modelo.exactitudValidacion * 100) : "—";
        $("modelo-intenciones").textContent = d.modelo?.intenciones ?? "—";
        $("modelo-carpeta").textContent = d.modelo?.carpeta ?? "—";

        $("act-predicciones").textContent = numero.format(d.actividad?.predicciones ?? 0);
        $("act-promedio").textContent = d.actividad?.msPromedio != null ? `${numero.format(d.actividad.msPromedio)} ms por pregunta` : "—";
        $("act-ultima").textContent = fechaLarga(d.actividad?.ultimaPrediccion);
        $("act-versiones").textContent = d.versiones
            ? `Python ${d.versiones.python} · PyTorch ${d.versiones.torch} · Transformers ${d.versiones.transformers}` : "—";

        if (mem.procesoMb != null) {
            historial.push({ hora: new Date(), mb: mem.procesoMb });
            if (historial.length > MUESTRAS) historial.shift();
            dibujarHistorial();
        }
    }

    async function actualizar() {
        if (document.hidden || pausado) return;

        if (Date.now() - inicio > PAUSA_AUTOMATICA) {
            pausar(true);
            return;
        }

        try {
            const respuesta = await fetch(URL_DATOS, { headers: { Accept: "application/json" } });
            if (respuesta.redirected || respuesta.status === 401) {
                window.location.reload();   // la sesión caducó: el panel lleva al acceso
                return;
            }
            if (!respuesta.ok) throw new Error(String(respuesta.status));
            const datos = await respuesta.json();
            conexion(datos.estado === "ok" ? "ok" : "cargando");
            mostrar(datos);
        } catch {
            conexion("caido");
        }
    }

    // ---------- pausa y reanudación ----------
    const botonPausa = $("bert-pausar");

    function pausar(valor) {
        pausado = valor;
        botonPausa.querySelector("i").className = `fa ${valor ? "fa-play" : "fa-pause"} me-1`;
        botonPausa.querySelector("span").textContent = valor ? "Reanudar" : "Pausar";
        if (valor) {
            conexion("pausa");
        } else {
            inicio = Date.now();
            actualizar();
        }
    }

    botonPausa.addEventListener("click", () => pausar(!pausado));

    // Vista de gráfico / vista de tabla
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

    document.addEventListener("visibilitychange", () => { if (!document.hidden) actualizar(); });

    actualizar();
    temporizador = setInterval(actualizar, INTERVALO);
})();
