using System.ComponentModel.DataAnnotations.Schema;

namespace IngestaArchivosAPI.Data;


[Table("archivo_ingestado")]
public class ArchivoIngestado
{
    public int Id { get; set; }
    public DateTime FechaCarga { get; set; } = DateTime.UtcNow;
    public string NombreOriginal { get; set; } = default!;
    public string HashSha256 { get; set; } = default!;
    public int TamanoBytes { get; set; }
    public string RutaMinio { get; set; } = default!;
    public string? UsuarioCarga { get; set; }

    public int? OfficeId { get; set; } // Relación opcional con Office   


}