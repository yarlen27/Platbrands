using Microsoft.EntityFrameworkCore;
using IngestaArchivosAPI;
using IngestaArchivosAPI.Data.Configurations;
using IngestaArchivosAPI.Models;

namespace IngestaArchivosAPI.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public DbSet<ArchivoIngestado> ArchivosIngestados => Set<ArchivoIngestado>();
    public DbSet<ProcesoOcr> ProcesoOcr => Set<ProcesoOcr>();
    public DbSet<PromptIa> PromptsIa => Set<PromptIa>();
    public DbSet<Assistant> Assistants => Set<Assistant>();
    public DbSet<ExtractionValidation> ExtractionValidations => Set<ExtractionValidation>();
    public DbSet<FineTuningJob> FineTuningJobs => Set<FineTuningJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ArchivoIngestadoConfiguration());
        
        // Configuración para ProcesoOcr
        modelBuilder.Entity<ProcesoOcr>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Estado).IsRequired();
            entity.HasCheckConstraint("proceso_ocr_estado_check", 
                "estado = ANY (ARRAY['procesado'::text, 'omitido'::text, 'fallido'::text])");
            
            // Relación con ArchivoIngestado
            entity.HasOne(e => e.Archivo)
                  .WithMany()
                  .HasForeignKey(e => e.ArchivoId)
                  .OnDelete(DeleteBehavior.NoAction);
        });

        // Configuración para Assistant
        modelBuilder.Entity<Assistant>(entity =>
        {
            entity.ToTable("asistente_oficina");
            entity.HasKey(e => e.id);
            entity.Property(e => e.nombre_oficina).IsRequired();
            entity.Property(e => e.oficina_id).IsRequired();
            entity.Property(e => e.software_origen).IsRequired();
            entity.Property(e => e.assistant_id).IsRequired();
            entity.Property(e => e.vector_store_id).IsRequired();
            entity.Property(e => e.model_id).IsRequired();
            entity.Property(e => e.activo).HasDefaultValue(true);
            entity.Property(e => e.fecha_creacion).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
    }
}
