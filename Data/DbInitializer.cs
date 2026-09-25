using AppDonnyCuevas20210074.Helpers;
using AppDonnyCuevas20210074.Models;
using Microsoft.AspNetCore.Identity;

namespace AppDonnyCuevas20210074.Data
{
    public static class DbInitializer
    {
        public static void Inicializar(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TurismoJimaniContext>();

            foreach (var nombreRol in new[] { RolesSistema.Administrador, RolesSistema.Editor, RolesSistema.Visitante })
            {
                if (!db.Roles.Any(r => r.Nombre == nombreRol))
                {
                    db.Roles.Add(new Rol { Nombre = nombreRol });
                }
            }
            db.SaveChanges();

            if (!db.Usuarios.Any())
            {
                var rolAdmin = db.Roles.First(r => r.Nombre == "Administrador");

                var admin = new Usuario
                {
                    IdRol = rolAdmin.IdRol,
                    NombreCompleto = "Administrador",
                    Correo = "admin@turismojimani.com",
                    Activo = true,
                    FechaCreacion = DateTime.Now
                };
                admin.ContrasenaHash = new PasswordHasher<Usuario>().HashPassword(admin, "Admin123*");

                db.Usuarios.Add(admin);
                db.SaveChanges();
            }

            // Datos curiosos iniciales del sitio público (se editan desde el panel)
            if (!db.DatosCuriosos.Any())
            {
                db.DatosCuriosos.AddRange(DatosCuriososIniciales.Select(d =>
                    new DatoCurioso { Categoria = d.Categoria, Texto = d.Texto, Activo = true }));
                db.SaveChanges();
            }
        }

        private static readonly (string Categoria, string Texto)[] DatosCuriososIniciales =
        {
            ("Jimaní", "Jimaní es el municipio cabecera de la provincia Independencia, en el suroeste de la República Dominicana."),
            ("Jimaní", "En Jimaní está uno de los principales pasos fronterizos con Haití; del otro lado de la frontera se encuentra Malpasse."),
            ("Jimaní", "Jimaní está en la Hoya de Enriquillo, una depresión entre la Sierra de Neiba y la Sierra de Bahoruco."),
            ("Jimaní", "En el Mercado Binacional de Jimaní se reúnen comerciantes dominicanos y haitianos en los días de mercado."),
            ("Naturaleza", "El Lago Enriquillo es el lago más grande de las Antillas y el punto más bajo del Caribe: su superficie está por debajo del nivel del mar."),
            ("Naturaleza", "Las aguas del Lago Enriquillo son saladas porque el lago es el resto de un antiguo canal marino que atravesaba la isla."),
            ("Naturaleza", "En el Lago Enriquillo vive una de las poblaciones silvestres de cocodrilo americano más importantes del Caribe."),
            ("Naturaleza", "La isla Cabritos, dentro del Lago Enriquillo, es parque nacional y hogar de iguanas rinoceronte y de iguanas de Ricord."),
            ("Naturaleza", "En las orillas del Lago Enriquillo pueden observarse flamencos americanos."),
            ("Naturaleza", "La Sierra de Bahoruco forma parte de la Reserva de la Biosfera Jaragua-Bahoruco-Enriquillo, declarada por la UNESCO en 2002."),
            ("Naturaleza", "La zona de Jimaní es una de las más secas y cálidas del país: cactus, guayacanes y bosque seco forman su paisaje."),
            ("Cultura taína", "Las Caritas son petroglifos taínos: rostros tallados en la roca frente al Lago Enriquillo."),
            ("Cultura taína", "El Lago Enriquillo lleva el nombre del cacique taíno Enriquillo (Guarocuya), que resistió a los españoles en la Sierra de Bahoruco en el siglo XVI."),
            ("Cultura taína", "Palabras como hamaca, canoa, barbacoa y huracán vienen del idioma taíno."),
            ("Cultura taína", "Los taínos llamaban cemíes a sus espíritus protectores y a los objetos tallados que los representaban."),
            ("Cultura taína", "Atabey era, para los taínos, la madre de las aguas dulces y de la fertilidad."),
            ("Cultura taína", "El areíto era la ceremonia taína de canto y baile en la que se narraba la historia del pueblo."),
            ("Cultura dominicana", "La UNESCO inscribió el merengue como Patrimonio Cultural Inmaterial de la Humanidad en 2016."),
            ("Cultura dominicana", "La bachata fue declarada Patrimonio Cultural Inmaterial de la Humanidad por la UNESCO en 2019."),
            ("Cultura dominicana", "El larimar, una piedra azul que solo se encuentra en República Dominicana, se extrae en Barahona, provincia vecina de Independencia."),
            ("Cultura dominicana", "El escudo de la bandera dominicana tiene una Biblia abierta y el lema «Dios, Patria, Libertad»."),
            ("Cultura dominicana", "El merengue típico se toca con acordeón, güira y tambora."),
            ("Cultura dominicana", "«La bandera» es el almuerzo dominicano por excelencia: arroz, habichuelas y carne.")
        };
    }
}
