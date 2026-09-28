using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppDonnyCuevas20210074.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categorias",
                columns: table => new
                {
                    IdCategoria = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categorias", x => x.IdCategoria);
                });

            migrationBuilder.CreateTable(
                name: "ConsultasAsistente",
                columns: table => new
                {
                    IdConsulta = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdSesion = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MensajeUsuario = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IntencionDetectada = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RespuestaAsistente = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TipoRespuesta = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RespuestaCorrecta = table.Column<bool>(type: "bit", nullable: true),
                    TiempoRespuestaMilisegundos = table.Column<int>(type: "int", nullable: true),
                    FechaConsulta = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsultasAsistente", x => x.IdConsulta);
                });

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
                name: "DocumentosConocimiento",
                columns: table => new
                {
                    IdDocumento = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Titulo = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Contenido = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TipoFuente = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IdReferencia = table.Column<int>(type: "int", nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentosConocimiento", x => x.IdDocumento);
                });

            migrationBuilder.CreateTable(
                name: "Intenciones",
                columns: table => new
                {
                    IdIntencion = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Activa = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Intenciones", x => x.IdIntencion);
                });

            migrationBuilder.CreateTable(
                name: "Lugares",
                columns: table => new
                {
                    IdLugar = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    DescripcionCorta = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Direccion = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Municipio = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Provincia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Latitud = table.Column<decimal>(type: "decimal(10,8)", nullable: true),
                    Longitud = table.Column<decimal>(type: "decimal(11,8)", nullable: true),
                    Telefono = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Correo = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    SitioWeb = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lugares", x => x.IdLugar);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    IdRol = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.IdRol);
                });

            migrationBuilder.CreateTable(
                name: "Servicios",
                columns: table => new
                {
                    IdServicio = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Servicios", x => x.IdServicio);
                });

            migrationBuilder.CreateTable(
                name: "TiposPrestador",
                columns: table => new
                {
                    IdTipoPrestador = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Clase = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Grupo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Icono = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TiposPrestador", x => x.IdTipoPrestador);
                });

            migrationBuilder.CreateTable(
                name: "EjemplosIntencion",
                columns: table => new
                {
                    IdEjemplo = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdIntencion = table.Column<int>(type: "int", nullable: false),
                    Texto = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Idioma = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EjemplosIntencion", x => x.IdEjemplo);
                    table.ForeignKey(
                        name: "FK_EjemplosIntencion_Intenciones_IdIntencion",
                        column: x => x.IdIntencion,
                        principalTable: "Intenciones",
                        principalColumn: "IdIntencion",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Atractivos",
                columns: table => new
                {
                    IdAtractivo = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdLugar = table.Column<int>(type: "int", nullable: false),
                    TipoAtractivo = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    EstadoConservacion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InformacionNatural = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InformacionCultural = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InformacionAcceso = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DuracionVisitaMinutos = table.Column<int>(type: "int", nullable: true),
                    NivelDificultad = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Destacado = table.Column<bool>(type: "bit", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Atractivos", x => x.IdAtractivo);
                    table.ForeignKey(
                        name: "FK_Atractivos_Lugares_IdLugar",
                        column: x => x.IdLugar,
                        principalTable: "Lugares",
                        principalColumn: "IdLugar",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Contactos",
                columns: table => new
                {
                    IdContacto = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdLugar = table.Column<int>(type: "int", nullable: false),
                    TipoContacto = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ValorContacto = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    EsPrincipal = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contactos", x => x.IdContacto);
                    table.ForeignKey(
                        name: "FK_Contactos_Lugares_IdLugar",
                        column: x => x.IdLugar,
                        principalTable: "Lugares",
                        principalColumn: "IdLugar",
                        onDelete: ReferentialAction.Cascade);
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

            migrationBuilder.CreateTable(
                name: "Horarios",
                columns: table => new
                {
                    IdHorario = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdLugar = table.Column<int>(type: "int", nullable: false),
                    DiaSemana = table.Column<byte>(type: "tinyint", nullable: false),
                    HoraApertura = table.Column<TimeSpan>(type: "time", nullable: true),
                    HoraCierre = table.Column<TimeSpan>(type: "time", nullable: true),
                    Cerrado = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Horarios", x => x.IdHorario);
                    table.CheckConstraint("CK_Horarios_DiaSemana", "DiaSemana BETWEEN 1 AND 7");
                    table.ForeignKey(
                        name: "FK_Horarios_Lugares_IdLugar",
                        column: x => x.IdLugar,
                        principalTable: "Lugares",
                        principalColumn: "IdLugar",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LugarCategoria",
                columns: table => new
                {
                    CategoriasIdCategoria = table.Column<int>(type: "int", nullable: false),
                    LugaresIdLugar = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LugarCategoria", x => new { x.CategoriasIdCategoria, x.LugaresIdLugar });
                    table.ForeignKey(
                        name: "FK_LugarCategoria_Categorias_CategoriasIdCategoria",
                        column: x => x.CategoriasIdCategoria,
                        principalTable: "Categorias",
                        principalColumn: "IdCategoria",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LugarCategoria_Lugares_LugaresIdLugar",
                        column: x => x.LugaresIdLugar,
                        principalTable: "Lugares",
                        principalColumn: "IdLugar",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Rutas",
                columns: table => new
                {
                    IdRuta = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IdLugarOrigen = table.Column<int>(type: "int", nullable: true),
                    IdLugarDestino = table.Column<int>(type: "int", nullable: true),
                    DistanciaKilometros = table.Column<decimal>(type: "decimal(8,2)", nullable: true),
                    DuracionMinutos = table.Column<int>(type: "int", nullable: true),
                    NivelDificultad = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TipoTransporte = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Instrucciones = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Activa = table.Column<bool>(type: "bit", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rutas", x => x.IdRuta);
                    table.ForeignKey(
                        name: "FK_Rutas_Lugares_IdLugarDestino",
                        column: x => x.IdLugarDestino,
                        principalTable: "Lugares",
                        principalColumn: "IdLugar",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Rutas_Lugares_IdLugarOrigen",
                        column: x => x.IdLugarOrigen,
                        principalTable: "Lugares",
                        principalColumn: "IdLugar",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    IdUsuario = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdRol = table.Column<int>(type: "int", nullable: false),
                    NombreCompleto = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Correo = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ContrasenaHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.IdUsuario);
                    table.ForeignKey(
                        name: "FK_Usuarios_Roles_IdRol",
                        column: x => x.IdRol,
                        principalTable: "Roles",
                        principalColumn: "IdRol",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LugarServicio",
                columns: table => new
                {
                    LugaresIdLugar = table.Column<int>(type: "int", nullable: false),
                    ServiciosIdServicio = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LugarServicio", x => new { x.LugaresIdLugar, x.ServiciosIdServicio });
                    table.ForeignKey(
                        name: "FK_LugarServicio_Lugares_LugaresIdLugar",
                        column: x => x.LugaresIdLugar,
                        principalTable: "Lugares",
                        principalColumn: "IdLugar",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LugarServicio_Servicios_ServiciosIdServicio",
                        column: x => x.ServiciosIdServicio,
                        principalTable: "Servicios",
                        principalColumn: "IdServicio",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Prestadores",
                columns: table => new
                {
                    IdPrestador = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdTipoPrestador = table.Column<int>(type: "int", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    DescripcionCorta = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Telefono = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    WhatsApp = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Correo = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    SitioWeb = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RedSocial = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Direccion = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Municipio = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Provincia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Latitud = table.Column<decimal>(type: "decimal(10,8)", nullable: true),
                    Longitud = table.Column<decimal>(type: "decimal(11,8)", nullable: true),
                    ZonaCobertura = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    HorarioAtencion = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PrecioDesde = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    PrecioHasta = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    InformacionPrecio = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Especialidad = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Capacidad = table.Column<int>(type: "int", nullable: true),
                    RequiereReserva = table.Column<bool>(type: "bit", nullable: false),
                    ServicioDomicilio = table.Column<bool>(type: "bit", nullable: false),
                    EnlaceExterno = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Destacado = table.Column<bool>(type: "bit", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prestadores", x => x.IdPrestador);
                    table.ForeignKey(
                        name: "FK_Prestadores_TiposPrestador_IdTipoPrestador",
                        column: x => x.IdTipoPrestador,
                        principalTable: "TiposPrestador",
                        principalColumn: "IdTipoPrestador",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Imagenes",
                columns: table => new
                {
                    IdImagen = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdLugar = table.Column<int>(type: "int", nullable: true),
                    IdPrestador = table.Column<int>(type: "int", nullable: true),
                    UrlImagen = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TextoAlternativo = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EsPrincipal = table.Column<bool>(type: "bit", nullable: false),
                    OrdenVisualizacion = table.Column<int>(type: "int", nullable: false),
                    Autor = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    FuenteUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Licencia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Imagenes", x => x.IdImagen);
                    table.CheckConstraint("CK_Imagenes_Propietario", "(IdLugar IS NOT NULL AND IdPrestador IS NULL) OR (IdLugar IS NULL AND IdPrestador IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Imagenes_Lugares_IdLugar",
                        column: x => x.IdLugar,
                        principalTable: "Lugares",
                        principalColumn: "IdLugar",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Imagenes_Prestadores_IdPrestador",
                        column: x => x.IdPrestador,
                        principalTable: "Prestadores",
                        principalColumn: "IdPrestador",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrestadorServicio",
                columns: table => new
                {
                    PrestadoresIdPrestador = table.Column<int>(type: "int", nullable: false),
                    ServiciosIdServicio = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrestadorServicio", x => new { x.PrestadoresIdPrestador, x.ServiciosIdServicio });
                    table.ForeignKey(
                        name: "FK_PrestadorServicio_Prestadores_PrestadoresIdPrestador",
                        column: x => x.PrestadoresIdPrestador,
                        principalTable: "Prestadores",
                        principalColumn: "IdPrestador",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PrestadorServicio_Servicios_ServiciosIdServicio",
                        column: x => x.ServiciosIdServicio,
                        principalTable: "Servicios",
                        principalColumn: "IdServicio",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Atractivos_IdLugar",
                table: "Atractivos",
                column: "IdLugar",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categorias_Nombre",
                table: "Categorias",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contactos_IdLugar",
                table: "Contactos",
                column: "IdLugar");

            migrationBuilder.CreateIndex(
                name: "IX_EjemplosIntencion_IdIntencion",
                table: "EjemplosIntencion",
                column: "IdIntencion");

            migrationBuilder.CreateIndex(
                name: "IX_Eventos_FechaInicio",
                table: "Eventos",
                column: "FechaInicio");

            migrationBuilder.CreateIndex(
                name: "IX_Eventos_IdLugar",
                table: "Eventos",
                column: "IdLugar");

            migrationBuilder.CreateIndex(
                name: "IX_Horarios_IdLugar",
                table: "Horarios",
                column: "IdLugar");

            migrationBuilder.CreateIndex(
                name: "IX_Imagenes_IdLugar",
                table: "Imagenes",
                column: "IdLugar");

            migrationBuilder.CreateIndex(
                name: "IX_Imagenes_IdPrestador",
                table: "Imagenes",
                column: "IdPrestador");

            migrationBuilder.CreateIndex(
                name: "IX_Intenciones_Nombre",
                table: "Intenciones",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LugarCategoria_LugaresIdLugar",
                table: "LugarCategoria",
                column: "LugaresIdLugar");

            migrationBuilder.CreateIndex(
                name: "IX_LugarServicio_ServiciosIdServicio",
                table: "LugarServicio",
                column: "ServiciosIdServicio");

            migrationBuilder.CreateIndex(
                name: "IX_Prestadores_IdTipoPrestador",
                table: "Prestadores",
                column: "IdTipoPrestador");

            migrationBuilder.CreateIndex(
                name: "IX_Prestadores_Nombre",
                table: "Prestadores",
                column: "Nombre");

            migrationBuilder.CreateIndex(
                name: "IX_PrestadorServicio_ServiciosIdServicio",
                table: "PrestadorServicio",
                column: "ServiciosIdServicio");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Nombre",
                table: "Roles",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rutas_IdLugarDestino",
                table: "Rutas",
                column: "IdLugarDestino");

            migrationBuilder.CreateIndex(
                name: "IX_Rutas_IdLugarOrigen",
                table: "Rutas",
                column: "IdLugarOrigen");

            migrationBuilder.CreateIndex(
                name: "IX_Servicios_Nombre",
                table: "Servicios",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TiposPrestador_Nombre",
                table: "TiposPrestador",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_Correo",
                table: "Usuarios",
                column: "Correo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_IdRol",
                table: "Usuarios",
                column: "IdRol");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Atractivos");

            migrationBuilder.DropTable(
                name: "ConsultasAsistente");

            migrationBuilder.DropTable(
                name: "Contactos");

            migrationBuilder.DropTable(
                name: "DatosCuriosos");

            migrationBuilder.DropTable(
                name: "DocumentosConocimiento");

            migrationBuilder.DropTable(
                name: "EjemplosIntencion");

            migrationBuilder.DropTable(
                name: "Eventos");

            migrationBuilder.DropTable(
                name: "Horarios");

            migrationBuilder.DropTable(
                name: "Imagenes");

            migrationBuilder.DropTable(
                name: "LugarCategoria");

            migrationBuilder.DropTable(
                name: "LugarServicio");

            migrationBuilder.DropTable(
                name: "PrestadorServicio");

            migrationBuilder.DropTable(
                name: "Rutas");

            migrationBuilder.DropTable(
                name: "Usuarios");

            migrationBuilder.DropTable(
                name: "Intenciones");

            migrationBuilder.DropTable(
                name: "Categorias");

            migrationBuilder.DropTable(
                name: "Prestadores");

            migrationBuilder.DropTable(
                name: "Servicios");

            migrationBuilder.DropTable(
                name: "Lugares");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "TiposPrestador");
        }
    }
}
