namespace IngestaArchivosAPI.Models
{
    public sealed class Assistant
    {

        // id,
        public Guid id { get; set; }
        //oficina_id,
        public int oficina_id { get; set; }
        //nombre_oficina,
        public string nombre_oficina { get; set; }
        //software_origen,
        public string software_origen { get; set; }
        //assistant_id,
        public string assistant_id { get; set; }
        //fecha_creacion,
        public DateTime fecha_creacion { get; set; }
        //activo,
        public bool activo { get; set; }
        //vector_store_id
        public string vector_store_id { get; set; }
        public string model_id { get; set; }

    }

    /// <summary>
    /// Información detallada de un asistente obtenida desde OpenAI
    /// </summary>
    public class AssistantInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Instructions { get; set; } = string.Empty;
        public long CreatedAt { get; set; }
    }
}
