using Microsoft.EntityFrameworkCore;
using SchId.Api.Data.Entities;

namespace SchId.Api.Data;

/// <summary>
/// DbContext mapeado explícitamente contra el esquema YA EXISTENTE
/// (base de datos SCHIDData). No usar migraciones de EF Core para crear o
/// modificar estas tablas: el esquema es propiedad del sistema original y
/// puede tener otras tablas/dependencias (ej. dbo.OCMap) que este proyecto
/// no toca.
/// </summary>
public class SchIdDbContext : DbContext
{
    public SchIdDbContext(DbContextOptions<SchIdDbContext> options) : base(options)
    {
    }

    public DbSet<Persona> Personas => Set<Persona>();
    public DbSet<Estancia> Estancias => Set<Estancia>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Persona>(entity =>
        {
            entity.ToTable("Personas");
            entity.HasKey(p => p.ID);

            entity.Property(p => p.ID).HasColumnName("ID");
            entity.Property(p => p.Nombre).HasColumnName("Nombre").HasColumnType("nchar(200)");
            entity.Property(p => p.Direccion).HasColumnName("Direccion").HasColumnType("nchar(1000)");
            entity.Property(p => p.Telefono).HasColumnName("Telefono").HasColumnType("nchar(50)");
            entity.Property(p => p.Nacionalidad).HasColumnName("Nacionalidad").HasColumnType("nchar(50)");
            entity.Property(p => p.Edad).HasColumnName("Edad");
            entity.Property(p => p.Residencia).HasColumnName("Residencia").HasColumnType("nchar(100)");
            entity.Property(p => p.Ocupacion).HasColumnName("Ocupacion").HasColumnType("nchar(100)");
            entity.Property(p => p.CURP).HasColumnName("CURP").HasColumnType("nchar(20)");
            entity.Property(p => p.Origen).HasColumnName("Origen").HasColumnType("nchar(50)");
            entity.Property(p => p.IdAWS).HasColumnName("IdAWS").HasColumnType("nchar(50)");
            entity.Property(p => p.IdAWSInt).HasColumnName("IdAWSInt");
            entity.Property(p => p.AutorizoFaceID).HasColumnName("AutorizoFaceID");
            entity.Property(p => p.FechaAlta).HasColumnName("FechaAlta").HasColumnType("numeric(18,1)");
            entity.Property(p => p.UltimaModificacion).HasColumnName("UltimaModificacion").HasColumnType("numeric(18,10)");

            // Nota: las columnas IDFoto1 / IDFoto2 de la tabla real no tienen
            // propiedad correspondiente en la clase Persona, así que EF Core
            // simplemente no las conoce ni las toca (ver Persona.cs).
        });

        modelBuilder.Entity<Estancia>(entity =>
        {
            entity.ToTable("Estancias");
            entity.HasKey(e => e.IdEstancia);

            entity.Property(e => e.IdEstancia).HasColumnName("IdEstancia");
            entity.Property(e => e.AccId).HasColumnName("AccId");
            entity.Property(e => e.FDeteccion).HasColumnName("FDeteccion").HasColumnType("numeric(18,8)");
            entity.Property(e => e.FIngreso).HasColumnName("FIngreso").HasColumnType("numeric(18,8)");
            entity.Property(e => e.FSalida).HasColumnName("FSalida").HasColumnType("numeric(18,8)");
            entity.Property(e => e.Hospedado).HasColumnName("Hospedado");
            entity.Property(e => e.IdPersona).HasColumnName("IdPersona");
            entity.Property(e => e.TipoAsignacion).HasColumnName("TipoAsignacion").HasColumnType("nchar(10)");
        });
    }
}
