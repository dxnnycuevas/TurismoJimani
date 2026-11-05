using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppDonnyCuevas20210074.Migrations
{
    /// <inheritdoc />
    public partial class RegistroDetallado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Ciudad",
                table: "Usuarios",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Direccion",
                table: "Usuarios",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaNacimiento",
                table: "Usuarios",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Nacionalidad",
                table: "Usuarios",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NombreEmpresa",
                table: "Usuarios",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NumeroDocumento",
                table: "Usuarios",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Pais",
                table: "Usuarios",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Telefono",
                table: "Usuarios",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TipoCuenta",
                table: "Usuarios",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TipoDocumento",
                table: "Usuarios",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_TipoDocumento_NumeroDocumento",
                table: "Usuarios",
                columns: new[] { "TipoDocumento", "NumeroDocumento" },
                unique: true,
                filter: "[NumeroDocumento] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Usuarios_TipoDocumento_NumeroDocumento",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "Ciudad",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "Direccion",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "FechaNacimiento",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "Nacionalidad",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "NombreEmpresa",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "NumeroDocumento",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "Pais",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "Telefono",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "TipoCuenta",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "TipoDocumento",
                table: "Usuarios");
        }
    }
}
