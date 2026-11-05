using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppDonnyCuevas20210074.Migrations
{
    /// <inheritdoc />
    public partial class PerfilVisitante : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IdUsuario",
                table: "ConsultasAsistente",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Favoritos",
                columns: table => new
                {
                    IdFavorito = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdUsuario = table.Column<int>(type: "int", nullable: false),
                    IdLugar = table.Column<int>(type: "int", nullable: true),
                    IdPrestador = table.Column<int>(type: "int", nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Favoritos", x => x.IdFavorito);
                    table.CheckConstraint("CK_Favoritos_Elemento", "(IdLugar IS NOT NULL AND IdPrestador IS NULL) OR (IdLugar IS NULL AND IdPrestador IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Favoritos_Lugares_IdLugar",
                        column: x => x.IdLugar,
                        principalTable: "Lugares",
                        principalColumn: "IdLugar",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Favoritos_Prestadores_IdPrestador",
                        column: x => x.IdPrestador,
                        principalTable: "Prestadores",
                        principalColumn: "IdPrestador",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Favoritos_Usuarios_IdUsuario",
                        column: x => x.IdUsuario,
                        principalTable: "Usuarios",
                        principalColumn: "IdUsuario",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanesGuardados",
                columns: table => new
                {
                    IdPlan = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdUsuario = table.Column<int>(type: "int", nullable: false),
                    Origen = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Monto = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    Noches = table.Column<int>(type: "int", nullable: false),
                    Cabe = table.Column<bool>(type: "bit", nullable: false),
                    Detalle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanesGuardados", x => x.IdPlan);
                    table.ForeignKey(
                        name: "FK_PlanesGuardados_Usuarios_IdUsuario",
                        column: x => x.IdUsuario,
                        principalTable: "Usuarios",
                        principalColumn: "IdUsuario",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsultasAsistente_IdUsuario_FechaConsulta",
                table: "ConsultasAsistente",
                columns: new[] { "IdUsuario", "FechaConsulta" });

            migrationBuilder.CreateIndex(
                name: "IX_Favoritos_IdLugar",
                table: "Favoritos",
                column: "IdLugar");

            migrationBuilder.CreateIndex(
                name: "IX_Favoritos_IdPrestador",
                table: "Favoritos",
                column: "IdPrestador");

            migrationBuilder.CreateIndex(
                name: "IX_Favoritos_IdUsuario_IdLugar",
                table: "Favoritos",
                columns: new[] { "IdUsuario", "IdLugar" },
                unique: true,
                filter: "IdLugar IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Favoritos_IdUsuario_IdPrestador",
                table: "Favoritos",
                columns: new[] { "IdUsuario", "IdPrestador" },
                unique: true,
                filter: "IdPrestador IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlanesGuardados_IdUsuario",
                table: "PlanesGuardados",
                column: "IdUsuario");

            migrationBuilder.AddForeignKey(
                name: "FK_ConsultasAsistente_Usuarios_IdUsuario",
                table: "ConsultasAsistente",
                column: "IdUsuario",
                principalTable: "Usuarios",
                principalColumn: "IdUsuario",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConsultasAsistente_Usuarios_IdUsuario",
                table: "ConsultasAsistente");

            migrationBuilder.DropTable(
                name: "Favoritos");

            migrationBuilder.DropTable(
                name: "PlanesGuardados");

            migrationBuilder.DropIndex(
                name: "IX_ConsultasAsistente_IdUsuario_FechaConsulta",
                table: "ConsultasAsistente");

            migrationBuilder.DropColumn(
                name: "IdUsuario",
                table: "ConsultasAsistente");
        }
    }
}
