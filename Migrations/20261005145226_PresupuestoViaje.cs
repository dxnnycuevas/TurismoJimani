using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppDonnyCuevas20210074.Migrations
{
    /// <inheritdoc />
    // Planificador de presupuesto del asistente: tabla TarifasViaje (pasajes hasta Jimaní),
    // vista vw_PreciosPorGrupo y procedimiento sp_PresupuestoViaje. No modifica las tablas existentes.
    public partial class PresupuestoViaje : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TarifasViaje",
                columns: table => new
                {
                    IdTarifa = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Origen = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Medio = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PrecioIda = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    HorasViaje = table.Column<decimal>(type: "decimal(4,1)", nullable: true),
                    Nota = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TarifasViaje", x => x.IdTarifa);
                });

            // Precios de los prestadores publicados, por grupo (alojamiento, comida, transporte...)
            migrationBuilder.Sql(@"
CREATE OR ALTER VIEW dbo.vw_PreciosPorGrupo
AS
SELECT t.Grupo,
       COUNT(*)                                     AS Prestadores,
       MIN(p.PrecioDesde)                           AS PrecioMinimo,
       CAST(AVG(p.PrecioDesde) AS decimal(10, 2))   AS PrecioPromedio,
       MAX(COALESCE(p.PrecioHasta, p.PrecioDesde))  AS PrecioMaximo
FROM dbo.Prestadores p
JOIN dbo.TiposPrestador t ON t.IdTipoPrestador = p.IdTipoPrestador
WHERE p.Activo = 1 AND p.PrecioDesde IS NOT NULL
GROUP BY t.Grupo;");

            // Presupuesto de un viaje a Jimaní: pasaje de ida y vuelta, hospedaje por noche,
            // 3 comidas y 2 viajes de transporte local por día.
            // Se prueban todas las combinaciones de hospedaje, comida y transporte:
            // @Modo = 'normal' elige la combinación más cercana a los precios promedio de cada grupo;
            // @Modo = 'barato' elige la combinación más económica.
            // Con @Monto solo se aceptan las combinaciones que caben en ese dinero; si ninguna cabe,
            // se devuelve la más económica (el asistente avisa cuánto falta).
            migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE dbo.sp_PresupuestoViaje
    @Noches INT,
    @Origen NVARCHAR(100),
    @Modo   NVARCHAR(10) = N'normal',
    @Monto  DECIMAL(12, 2) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Lineas TABLE (
        Orden          INT,
        Concepto       NVARCHAR(100),
        Opcion         NVARCHAR(150),
        IdPrestador    INT NULL,
        PrecioUnitario DECIMAL(10, 2),
        Cantidad       INT,
        Subtotal       DECIMAL(12, 2));

    -- Pasaje de ida y vuelta desde la ciudad de origen (el más económico registrado)
    INSERT INTO @Lineas
    SELECT TOP 1 1, N'Pasaje ida y vuelta (' + Medio + N')', Origen + N' – Jimaní', NULL,
           PrecioIda, 2, PrecioIda * 2
    FROM dbo.TarifasViaje
    WHERE Activo = 1 AND Origen = @Origen
    ORDER BY PrecioIda;

    DECLARE @Pasaje DECIMAL(12, 2) = ISNULL((SELECT SUM(Subtotal) FROM @Lineas), 0);

    -- Cuántas veces se paga cada grupo durante el viaje
    DECLARE @Grupos TABLE (Grupo NVARCHAR(50), Orden INT, Cantidad INT);
    INSERT INTO @Grupos VALUES (N'Alojamiento', 2, @Noches),
                               (N'Gastronomia', 3, @Noches * 3),
                               (N'Transporte',  4, @Noches * 2);

    DECLARE @IdHospedaje INT, @IdComida INT, @IdTransporte INT;

    WITH Opciones AS (
        -- Una opción por precio en cada grupo (la destacada) para no repetir combinaciones iguales
        SELECT p.IdPrestador, t.Grupo, p.PrecioDesde AS Precio, CAST(p.Destacado AS INT) AS Destacado,
               ABS(p.PrecioDesde - v.PrecioPromedio) AS Desvio,
               ROW_NUMBER() OVER (PARTITION BY t.Grupo, p.PrecioDesde
                                  ORDER BY p.Destacado DESC, p.IdPrestador) AS Repetida
        FROM dbo.Prestadores p
        JOIN dbo.TiposPrestador t ON t.IdTipoPrestador = p.IdTipoPrestador
        JOIN dbo.vw_PreciosPorGrupo v ON v.Grupo = t.Grupo
        WHERE p.Activo = 1
          AND p.PrecioDesde IS NOT NULL
          AND t.Grupo IN (N'Alojamiento', N'Gastronomia', N'Transporte')
    ),
    PorGrupo AS (
        -- Un grupo sin precios queda como fila vacía para no perder las demás combinaciones
        SELECT g.Grupo, g.Cantidad, o.IdPrestador, o.Precio, o.Destacado, o.Desvio
        FROM @Grupos g
        LEFT JOIN Opciones o ON o.Grupo = g.Grupo AND o.Repetida = 1
    ),
    Combinaciones AS (
        SELECT h.IdPrestador AS IdHospedaje, c.IdPrestador AS IdComida, t.IdPrestador AS IdTransporte,
               @Pasaje + ISNULL(h.Precio * h.Cantidad, 0) + ISNULL(c.Precio * c.Cantidad, 0)
                       + ISNULL(t.Precio * t.Cantidad, 0) AS Total,
               ISNULL(h.Desvio * h.Cantidad, 0) + ISNULL(c.Desvio * c.Cantidad, 0)
                       + ISNULL(t.Desvio * t.Cantidad, 0) AS Desvio,
               ISNULL(h.Destacado, 0) + ISNULL(c.Destacado, 0) + ISNULL(t.Destacado, 0) AS Destacados
        FROM PorGrupo h
        CROSS JOIN PorGrupo c
        CROSS JOIN PorGrupo t
        WHERE h.Grupo = N'Alojamiento' AND c.Grupo = N'Gastronomia' AND t.Grupo = N'Transporte'
    )
    SELECT TOP 1 @IdHospedaje = IdHospedaje, @IdComida = IdComida, @IdTransporte = IdTransporte
    FROM Combinaciones
    ORDER BY CASE WHEN @Monto IS NULL OR Total <= @Monto THEN 0 ELSE 1 END,
             CASE WHEN @Modo = N'barato' OR Total > @Monto THEN Total ELSE Desvio END,
             Destacados DESC,
             Total;

    INSERT INTO @Lineas
    SELECT g.Orden,
           CASE g.Grupo WHEN N'Alojamiento' THEN N'Hospedaje (' + CAST(@Noches AS NVARCHAR(5)) + CASE WHEN @Noches = 1 THEN N' noche)' ELSE N' noches)' END
                        WHEN N'Gastronomia' THEN N'Comida (3 comidas al día)'
                        ELSE N'Transporte local (2 viajes al día)' END,
           p.Nombre, p.IdPrestador, p.PrecioDesde, g.Cantidad, p.PrecioDesde * g.Cantidad
    FROM dbo.Prestadores p
    JOIN dbo.TiposPrestador t ON t.IdTipoPrestador = p.IdTipoPrestador
    JOIN @Grupos g ON g.Grupo = t.Grupo
    WHERE p.IdPrestador IN (@IdHospedaje, @IdComida, @IdTransporte);

    SELECT Orden, Concepto, Opcion, IdPrestador, PrecioUnitario, Cantidad, Subtotal
    FROM @Lineas
    ORDER BY Orden;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.sp_PresupuestoViaje;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.vw_PreciosPorGrupo;");

            migrationBuilder.DropTable(
                name: "TarifasViaje");
        }
    }
}
