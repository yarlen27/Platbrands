namespace IngestaArchivosAPI.Models
{
    public sealed class OfficeModelConfig
    {
        public Guid id { get; set; }
        public int office_id { get; set; }
        public string model_name { get; set; } = "gpt-4o";
        public bool is_fine_tuned { get; set; }
        public string? fine_tuned_model_id { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
        public string? created_by { get; set; }
        public string? notes { get; set; }

        /// <summary>
        /// Obtiene el nombre del modelo a usar (fine-tuneado o base)
        /// </summary>
        public string GetModelToUse()
        {
            return is_fine_tuned && !string.IsNullOrEmpty(fine_tuned_model_id) 
                ? fine_tuned_model_id 
                : model_name;
        }
    }
} 