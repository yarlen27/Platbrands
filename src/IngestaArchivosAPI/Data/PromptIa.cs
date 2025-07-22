using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IngestaArchivosAPI.Data;

[Table("prompt_ia")]
public class PromptIa
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [Column("nombre")]
    public string Nombre { get; set; } = default!;

    [Column("descripcion")]
    public string? Descripcion { get; set; }

    [Required]
    [Column("contenido")]
    public string Contenido { get; set; } = default!;

    [Required]
    [Column("hash_contenido")]
    public string HashContenido { get; set; } = default!;

    [Column("fecha_creacion")]
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    [Column("oficina_id")]
    public int? OficinaId { get; set; }
}