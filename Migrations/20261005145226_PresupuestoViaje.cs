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
            // @Modo = 'normal' elige la opción de precio más cercano al promedio de su grupo;
            // @Modo = 'barato' elige la opción más económica de cada grupo.
            migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE dbo.sp_PresupuestoViaje
    @Noches INT,
    @Origen NVARCHAR(100),
    @Modo   NVARCHAR(10) = N'normal'
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

    -- Una opción por grupo: hospedaje, comida y transporte local
    WITH Opciones AS (
        SELECT p.IdPrestador, p.Nombre, t.Grupo, p.PrecioDesde AS Precio,
               ROW_NUMBER() OVER (
                   PARTITION BY t.Grupo
                   ORDER BY CASE WHEN @Modo = N'barato' THEN p.PrecioDesde
                                 ELSE ABS(p.PrecioDesde - v.PrecioPromedio) END,
                            p.Destacado DESC,
                            p.PrecioDesde) AS Puesto
        FROM dbo.Prestadores p
        JOIN dbo.TiposPrestador t ON t.IdTipoPrestador = p.IdTipoPrestador
        JOIN dbo.vw_PreciosPorGrupo v ON v.Grupo = t.Grupo
        WHERE p.Activo = 1
          AND p.PrecioDesde IS NOT NULL
          AND t.Grupo IN (N'Alojamiento', N'Gastronomia', N'Transporte')
    )
    INSERT INTO @Lineas
    SELECT CASE Grupo WHEN N'Alojamiento' THEN 2 WHEN N'Gastronomia' THEN 3 ELSE 4 END,
           CASE Grupo WHEN N'Alojamiento' THEN N'Hospedaje (' + CAST(@Noches AS NVARCHAR(5)) + CASE WHEN @Noches = 1 THEN N' noche)' ELSE N' noches)' END
                      WHEN N'Gastronomia' THEN N'Comida (3 comidas al día)'
                      ELSE N'Transporte local (2 viajes al día)' END,
           Nombre, IdPrestador, Precio,
           Cantidad,
           Precio * Cantidad
    FROM (
        SELECT *, CASE Grupo WHEN N'Alojamiento' THEN @Noches
                             WHEN N'Gastronomia' THEN @Noches * 3
                             ELSE @Noches * 2 END AS Cantidad
        FROM Opciones
        WHERE Puesto = 1
    ) o;

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
