using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IngestaArchivosAPI.Data;

[Table("extraction_validations")]
public class ExtractionValidation
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [Column("archivo_id")]
    public int ArchivoId { get; set; }

    [Required]
    [Column("archivo_nombre")]
    public string ArchivoNombre { get; set; } = default!;

    [Required]
    [Column("office_id")]
    public int OfficeId { get; set; }

    [Required]
    [Column("input_text")]
    public string InputText { get; set; } = default!;

    [Required]
    [Column("openai_response")]
    public string OpenaiResponse { get; set; } = default!;

    [Required]
    [Column("prompt_used")]
    public string PromptUsed { get; set; } = default!;

    [Required]
    [Column("assistant_id")]
    public string AssistantId { get; set; } = default!;

    [Required]
    [Column("thread_id")]
    public string ThreadId { get; set; } = default!;

    [Required]
    [Column("run_id")]
    public string RunId { get; set; } = default!;

    [Column("status")]
    public string Status { get; set; } = "PendingUserValidation";

    [Column("corrected_json")]
    public string? CorrectedJson { get; set; }

    [Column("validated_by")]
    public string? ValidatedBy { get; set; }

    [Column("validated_at")]
    public DateTime? ValidatedAt { get; set; }

    [Column("validation_notes")]
    public string? ValidationNotes { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}