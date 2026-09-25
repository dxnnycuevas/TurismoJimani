#nullable enable
using Microsoft.EntityFrameworkCore;
using AppDonnyCuevas20210074.Models;

namespace AppDonnyCuevas20210074.Data
{
    public class TurismoJimaniContext : DbContext
    {
        public TurismoJimaniContext(DbContextOptions<TurismoJimaniContext> options)
            : base(options)
        {
        }

        public DbSet<Rol> Roles => Set<Rol>();
        public DbSet<Usuario> Usuarios => Set<Usuario>();
        public DbSet<Lugar> Lugares => Set<Lugar>();
        public DbSet<Categoria> Categorias => Set<Categoria>();
        public DbSet<Atractivo> Atractivos => Set<Atractivo>();
        public DbSet<Alojamiento> Alojamientos => Set<Alojamiento>();
        public DbSet<Restaurante> Restaurantes => Set<Restaurante>();
        public DbSet<Transporte> Transportes => Set<Transporte>();
        public DbSet<Servicio> Servicios => Set<Servicio>();
        public DbSet<Imagen> Imagenes => Set<Imagen>();
        public DbSet<Horario> Horarios => Set<Horario>();
        public DbSet<Contacto> Contactos => Set<Contacto>();
        public DbSet<Ruta> Rutas => Set<Ruta>();
        public DbSet<Intencion> Intenciones => Set<Intencion>();
        public DbSet<EjemploIntencion> EjemplosIntencion => Set<EjemploIntencion>();
        public DbSet<DocumentoConocimiento> DocumentosConocimiento => Set<DocumentoConocimiento>();
        public DbSet<ConsultaAsistente> ConsultasAsistente => Set<ConsultaAsistente>();
        public DbSet<Evento> Eventos => Set<Evento>();
        public DbSet<DatoCurioso> DatosCuriosos => Set<DatoCurioso>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ---- Índices UNIQUE (columnas UNIQUE del script original) ----
            modelBuilder.Entity<Rol>().HasIndex(r => r.Nombre).IsUnique();
            modelBuilder.Entity<Categoria>().HasIndex(c => c.Nombre).IsUnique();
            modelBuilder.Entity<Servicio>().HasIndex(s => s.Nombre).IsUnique();
            modelBuilder.Entity<Intencion>().HasIndex(i => i.Nombre).IsUnique();
            modelBuilder.Entity<Usuario>().HasIndex(u => u.Correo).IsUnique();

            // ---- Relaciones 1:1 (el IdLugar es UNIQUE en cada una de estas tablas) ----
            modelBuilder.Entity<Lugar>()
                .HasOne(l => l.Atractivo)
                .WithOne(a => a.Lugar)
                .HasForeignKey<Atractivo>(a => a.IdLugar);

            modelBuilder.Entity<Lugar>()
                .HasOne(l => l.Alojamiento)
                .WithOne(a => a.Lugar)
                .HasForeignKey<Alojamiento>(a => a.IdLugar);

            modelBuilder.Entity<Lugar>()
                .HasOne(l => l.Restaurante)
                .WithOne(r => r.Lugar)
                .HasForeignKey<Restaurante>(r => r.IdLugar);

            modelBuilder.Entity<Lugar>()
                .HasOne(l => l.Transporte)
                .WithOne(t => t.Lugar)
                .HasForeignKey<Transporte>(t => t.IdLugar);

            // ---- Muchos a muchos: LugarCategoria y LugarServicio (sin columnas propias) ----
            modelBuilder.Entity<Lugar>()
                .HasMany(l => l.Categorias)
                .WithMany(c => c.Lugares)
                .UsingEntity(j => j.ToTable("LugarCategoria"));

            modelBuilder.Entity<Lugar>()
                .HasMany(l => l.Servicios)
                .WithMany(s => s.Lugares)
                .UsingEntity(j => j.ToTable("LugarServicio"));

            // ---- Rutas: dos FK hacia Lugares, sin cascada (evita ciclos de borrado en SQL Server) ----
            modelBuilder.Entity<Ruta>()
                .HasOne(r => r.LugarOrigen)
                .WithMany()
                .HasForeignKey(r => r.IdLugarOrigen)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Ruta>()
                .HasOne(r => r.LugarDestino)
                .WithMany()
                .HasForeignKey(r => r.IdLugarDestino)
                .OnDelete(DeleteBehavior.Restrict);

            // ---- Eventos: el lugar es opcional; si se borra el lugar, el evento se conserva ----
            modelBuilder.Entity<Evento>()
                .HasOne(e => e.Lugar)
                .WithMany()
                .HasForeignKey(e => e.IdLugar)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Evento>().HasIndex(e => e.FechaInicio);

            // ---- CHECK constraint (DiaSemana BETWEEN 1 AND 7) ----
            modelBuilder.Entity<Horario>()
                .ToTable(t => t.HasCheckConstraint("CK_Horarios_DiaSemana", "DiaSemana BETWEEN 1 AND 7"));
        }
    }
}
