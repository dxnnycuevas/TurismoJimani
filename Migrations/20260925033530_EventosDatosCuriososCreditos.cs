using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppDonnyCuevas20210074.Migrations
{
    /// <inheritdoc />
    public partial class EventosDatosCuriososCreditos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Autor",
                table: "Imagenes",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FuenteUrl",
                table: "Imagenes",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Licencia",
                table: "Imagenes",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DatosCuriosos",
                columns: table => new
                {
                    IdDato = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Texto = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Categoria = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Fuente = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatosCuriosos", x => x.IdDato);
                });

            migrationBuilder.CreateTable(
                name: "Eventos",
                columns: table => new
                {
                    IdEvento = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Titulo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Resumen = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Contenido = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FechaInicio = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaFin = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IdLugar = table.Column<int>(type: "int", nullable: true),
                    LugarTexto = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Organizador = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    EnlaceExterno = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ImagenUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ImagenAutor = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ImagenFuenteUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Destacado = table.Column<bool>(type: "bit", nullable: false),
                    Publicado = table.Column<bool>(type: "bit", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Eventos", x => x.IdEvento);
                    table.ForeignKey(
                        name: "FK_Eventos_Lugares_IdLugar",
                        column: x => x.IdLugar,
                        principalTable: "Lugares",
                        principalColumn: "IdLugar",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Eventos_FechaInicio",
                table: "Eventos",
                column: "FechaInicio");

            migrationBuilder.CreateIndex(
                name: "IX_Eventos_IdLugar",
                table: "Eventos",
                column: "IdLugar");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DatosCuriosos");

            migrationBuilder.DropTable(
                name: "Eventos");

            migrationBuilder.DropColumn(
                name: "Autor",
                table: "Imagenes");

            migrationBuilder.DropColumn(
                name: "FuenteUrl",
                table: "Imagenes");

            migrationBuilder.DropColumn(
                name: "Licencia",
                table: "Imagenes");
        }
    }
}
