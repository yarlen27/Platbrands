namespace IngestaArchivosAPI.Models
{
    public enum ValidationStatus
    {
        PendingUserValidation,
        ValidatedCorrect,
        ValidatedIncorrect,
        Corrected
    }

    public sealed class ExtractionValidation
    {
        public Guid id { get; set; }
        public int archivo_id { get; set; }
        public string archivo_nombre { get; set; }
        public int office_id { get; set; }
        public string input_text { get; set; } // Texto del PDF/OCR
        public string openai_response { get; set; } // JSON generado por OpenAI
        public string prompt_used { get; set; } // Prompt que se usó
        public string assistant_id { get; set; }
        public string thread_id { get; set; }
        public string run_id { get; set; }
        public ValidationStatus status { get; set; }
        public string? corrected_json { get; set; } // JSON corregido por el usuario
        public string? validated_by { get; set; } // Usuario que validó
        public DateTime? validated_at { get; set; }
        public string? validation_notes { get; set; } // Notas del usuario
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
    }
} 