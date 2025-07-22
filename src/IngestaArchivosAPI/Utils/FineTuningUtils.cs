using IngestaArchivosAPI.Models;
using System.Text;
using System.Text.Json;
using Sentry;

namespace IngestaArchivosAPI.Utils;

public class FineTuningUtils
{
    private readonly DbUtils _dbUtils;
    private readonly OpenAIUtils _openAIUtils;
    private const int MIN_VALIDATIONS_FOR_FINE_TUNING = 50;

    public FineTuningUtils(DbUtils dbUtils, OpenAIUtils openAIUtils)
    {
        _dbUtils = dbUtils;
        _openAIUtils = openAIUtils;
    }

    /// <summary>
    /// Verifica si hay suficientes validaciones para iniciar fine-tuning
    /// </summary>
    public bool DebeIniciarFineTuning(int officeId)
    {
        var validaciones = _dbUtils.ObtenerExtraccionesValidadasCorrectas(officeId, MIN_VALIDATIONS_FOR_FINE_TUNING + 10);
        return validaciones.Count >= MIN_VALIDATIONS_FOR_FINE_TUNING;
    }

    /// <summary>
    /// Genera el archivo JSONL para fine-tuning
    /// </summary>
    public string GenerarJSONL(int officeId)
    {
        var validaciones = _dbUtils.ObtenerExtraccionesValidadasCorrectas(officeId, 1000);
        
        if (validaciones.Count < MIN_VALIDATIONS_FOR_FINE_TUNING)
        {
            throw new InvalidOperationException($"Se requieren al menos {MIN_VALIDATIONS_FOR_FINE_TUNING} validaciones. Actualmente hay {validaciones.Count}.");
        }

        var jsonlContent = new StringBuilder();
        
        foreach (var validacion in validaciones)
        {
            // Determinar qué JSON usar (el original o el corregido)
            string jsonToUse = validacion.status == ValidationStatus.Corrected && !string.IsNullOrEmpty(validacion.corrected_json)
                ? validacion.corrected_json
                : validacion.openai_response;

            // Usar el prompt específico de la oficina en lugar del genérico
            string systemPrompt = validacion.prompt_used;

            // Crear el formato JSONL para fine-tuning
            var trainingExample = new
            {
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = systemPrompt
                    },
                    new
                    {
                        role = "user",
                        content = validacion.input_text
                    },
                    new
                    {
                        role = "assistant",
                        content = jsonToUse
                    }
                }
            };

            // Agregar línea al JSONL
            string jsonLine = JsonSerializer.Serialize(trainingExample);
            jsonlContent.AppendLine(jsonLine);
        }

        return jsonlContent.ToString();
    }

    /// <summary>
    /// Inicia el proceso completo de fine-tuning
    /// </summary>
    public async Task<string> IniciarFineTuningAutomatico(int officeId)
    {
        try
        {
            Console.WriteLine($"🚀 Iniciando fine-tuning automático para oficina {officeId}");

            // 1. Verificar que hay suficientes validaciones
            if (!DebeIniciarFineTuning(officeId))
            {
                throw new InvalidOperationException($"No hay suficientes validaciones para fine-tuning. Se requieren {MIN_VALIDATIONS_FOR_FINE_TUNING}.");
            }

            // 2. Generar JSONL
            Console.WriteLine("📝 Generando archivo JSONL...");
            string jsonlContent = GenerarJSONL(officeId);
            
            // 3. Obtener el número de validaciones para guardar en la BD
            var validaciones = _dbUtils.ObtenerExtraccionesValidadasCorrectas(officeId, 1000);
            
            // 4. Subir archivo a OpenAI
            Console.WriteLine("📤 Subiendo archivo JSONL a OpenAI...");
            byte[] jsonlBytes = Encoding.UTF8.GetBytes(jsonlContent);
            string fileName = $"fine_tuning_dataset_office_{officeId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.jsonl";
            
            string fileId = await _openAIUtils.SubirArchivoParaFineTuning(jsonlBytes, fileName);

            // 5. Crear job de fine-tuning
            Console.WriteLine("🔧 Creando job de fine-tuning...");
            string jobId = await _openAIUtils.CrearJobFineTuning(fileId, officeId);

            // 6. Guardar información del job en base de datos
            _dbUtils.GuardarJobFineTuning(officeId, jobId, fileId, validaciones.Count, jsonlContent.Length);

            Console.WriteLine($"✅ Fine-tuning iniciado exitosamente. Job ID: {jobId}");
            return jobId;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error al iniciar fine-tuning: {ex.Message}");
            SentrySdk.CaptureException(ex);
            throw;
        }
    }

    /// <summary>
    /// Verifica el estado de un job de fine-tuning
    /// </summary>
    public async Task<FineTuningJobStatus> VerificarEstadoFineTuning(string jobId)
    {
        return await _openAIUtils.VerificarEstadoJobFineTuning(jobId);
    }

    /// <summary>
    /// Procesa un job completado y actualiza el modelo
    /// </summary>
    public async Task ProcesarJobCompletado(string jobId, int officeId)
    {
        try
        {
            Console.WriteLine($"🎉 Procesando job completado: {jobId}");

            // 1. Obtener información del job
            var jobStatus = await _openAIUtils.VerificarEstadoJobFineTuning(jobId);
            
            if (jobStatus.Status == "succeeded" && !string.IsNullOrEmpty(jobStatus.FineTunedModel))
            {
                // 2. Actualizar configuración de la oficina con el nuevo modelo
                _dbUtils.ConfigurarModeloParaOficina(
                    officeId: officeId,
                    modelName: "gpt-3.5-turbo", // Modelo base
                    isFineTuned: true,
                    fineTunedModelId: jobStatus.FineTunedModel,
                    createdBy: "system",
                    notes: $"Fine-tuning automático completado. Job: {jobId}"
                );

                // 3. Actualizar estado del job en base de datos
                _dbUtils.ActualizarEstadoJobFineTuning(jobId, "completed", jobStatus.FineTunedModel);

                Console.WriteLine($"✅ Modelo fine-tuneado configurado para oficina {officeId}: {jobStatus.FineTunedModel}");
            }
            else if (jobStatus.Status == "failed")
            {
                _dbUtils.ActualizarEstadoJobFineTuning(jobId, "failed", null);
                Console.WriteLine($"❌ Fine-tuning falló para job {jobId}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error al procesar job completado: {ex.Message}");
            SentrySdk.CaptureException(ex);
            throw;
        }
    }
}

public class FineTuningJobStatus
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? FineTunedModel { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? Error { get; set; }
} 