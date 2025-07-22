using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IngestaArchivosAPI.Data.Configurations;

public class ArchivoIngestadoConfiguration : IEntityTypeConfiguration<ArchivoIngestado>
{
    public void Configure(EntityTypeBuilder<ArchivoIngestado> entity)
    {
        entity.ToTable("archivo_ingestado");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd(); // Auto-incremento
        entity.Property(e => e.FechaCarga).HasColumnName("fecha_carga");
        entity.Property(e => e.NombreOriginal).HasColumnName("nombre_original");
        entity.Property(e => e.HashSha256).HasColumnName("hash_sha256");
        entity.Property(e => e.TamanoBytes).HasColumnName("tamano_bytes");
        entity.Property(e => e.RutaMinio).HasColumnName("ruta_minio");
        entity.Property(e => e.UsuarioCarga).HasColumnName("usuario_carga");
        entity.Property(e => e.OfficeId).HasColumnName("office_id");
    }
}
