using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IngestaArchivosAPI.Data;

[Table("fine_tuning_jobs")]
public class FineTuningJob
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [Column("office_id")]
    public int OfficeId { get; set; }

    [Required]
    [Column("job_id")]
    public string JobId { get; set; } = default!;

    [Required]
    [Column("file_id")]
    public string FileId { get; set; } = default!;

    [Required]
    [Column("total_examples")]
    public int TotalExamples { get; set; }

    [Required]
    [Column("jsonl_size")]
    public int JsonlSize { get; set; }

    [Column("status")]
    public string Status { get; set; } = "running";

    [Column("fine_tuned_model_id")]
    public string? FineTunedModelId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}