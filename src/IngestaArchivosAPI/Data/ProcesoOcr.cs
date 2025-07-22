using System.ComponentModel.DataAnnotations.Schema;

namespace IngestaArchivosAPI.Data;

[Table("proceso_ocr")]
public class ProcesoOcr
{
    [Column("id")]
    public int Id { get; set; }
    
    [Column("archivo_id")]
    public int ArchivoId { get; set; }
    
    [Column("estado")]
    public string Estado { get; set; } = default!;
    
    [Column("mensaje")]
    public string? Mensaje { get; set; }
    
    [Column("tamano_original_bytes")]
    public int? TamanoOriginalBytes { get; set; }
    
    [Column("tamano_resultado_bytes")]
    public int? TamanoResultadoBytes { get; set; }
    
    [Column("tiempo_procesamiento_ms")]
    public int? TiempoProcesamiento { get; set; }
    
    [Column("paginas")]
    public int? Paginas { get; set; }
    
    [Column("fue_forzado")]
    public bool? FueForzado { get; set; }
    
    [Column("fecha")]
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    
    [Column("ruta_minio")]
    public string? RutaMinio { get; set; }
    
    [Column("oficina_id")]
    public int? OficinaId { get; set; }
    
    // Nuevo campo para almacenar el ID del archivo en OpenAI VectorStore
    [Column("vector_store_file_id")]
    public string? VectorStoreFileId { get; set; }
    
    // Navegación
    public ArchivoIngestado Archivo { get; set; } = default!;
}