using IngestaArchivosAPI.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Assistants;
using OpenAI.Files;
using OpenAI.VectorStores;
using System.Reflection;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Sentry;
using System.Net.Http.Headers;

namespace IngestaArchivosAPI.Utils;

public sealed class OpenAIUtils
{

    private readonly string openAiApiKey;
    private readonly DbUtils _dbUtils;
    private readonly IConfiguration _configuration;
    private readonly OpenAIClient _openAIClient;
    private readonly ILogger<OpenAIUtils> _logger;

    // Cache para threads reutilizables por oficina
    private readonly Dictionary<int, string> _threadCacheByOffice = new();
    private readonly Dictionary<int, DateTime> _threadCacheExpiry = new();
    private readonly TimeSpan _threadCacheTimeout = TimeSpan.FromMinutes(30);

    private readonly HttpClient httpClient;
    public OpenAIUtils(DbUtils dbUtils, IConfiguration configuration, ILogger<OpenAIUtils> logger)
    {
        _configuration = configuration;
        openAiApiKey = _configuration["OpenAI:ApiKey"] ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured");
        _dbUtils = dbUtils;
        _openAIClient = new OpenAIClient(openAiApiKey);

        _logger = logger;

        httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", openAiApiKey);
    }
    public string CreateAssistantForOffice(int officeId, string officeName, string sourceSoftware)
    {
        // Build instructions
        var instructions = $"Eres un sistema experto en la extracción de datos de documentos de la oficina {officeName}. " +
                           $"Los archivos que vas a procesar provienen del software {sourceSoftware}. " +
                           "Debes devolver siempre un JSON estructurado, con un objeto por registro.";

        // Obtener modelo dinámico para la oficina
        var openAiModel = _dbUtils.ObtenerModeloParaOficina(officeId);
        Console.WriteLine($"🤖 Usando modelo para oficina {officeId}: {openAiModel}");

        // Obtener o crear vector_store_id para la oficina
        var vectorStoreId = _dbUtils.ObtenerVectorStoreIdPorOficina(officeId);
        if (string.IsNullOrEmpty(vectorStoreId))
        {
            vectorStoreId = CrearVectorStoreParaOficina(officeId, officeName);
            _dbUtils.ActualizarVectorStoreIdPorOficina(officeId, vectorStoreId);
            Console.WriteLine($"🗂️ Vector store creado para oficina {officeId}: {vectorStoreId}");
        }

        // Verificar si ya existe un asistente y si usa el mismo modelo y vector store
        var existingAssistant = _dbUtils.ObtenerAssistantIdPorOficina(officeId);
        if (existingAssistant != null)
        {
            var currentModelInAssistant = existingAssistant.model_id;
            var currentVectorStoreInAssistant = existingAssistant.vector_store_id;
            if (currentModelInAssistant != openAiModel || currentVectorStoreInAssistant != vectorStoreId)
            {
                Console.WriteLine($"🔄 Modelo o vector store cambiaron. Eliminando asistente anterior...");
                EliminarAssistant(existingAssistant.assistant_id);
                _dbUtils.EliminarAssistantDeBd(existingAssistant.id);
            }
            else
            {
                Console.WriteLine($"✅ Asistente ya existe y usa el modelo y vector store correctos");
                return existingAssistant.assistant_id;
            }
        }

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {openAiApiKey}");
        httpClient.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

        var payload = new
        {
            instructions = instructions,
            name = $"Asistente {officeName}",
            tools = new[] { new { type = "code_interpreter" }, new { type = "file_search" } },
            model = openAiModel,
            tool_resources = new
            {
                file_search = new
                {
                    vector_store_ids = new[] { vectorStoreId }
                }
            }
        };

        var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
        var response = httpClient.PostAsync("https://api.openai.com/v1/assistants", content).Result;
        response.EnsureSuccessStatusCode();

        var responseBody = response.Content.ReadAsStringAsync().Result;
        using var doc = System.Text.Json.JsonDocument.Parse(responseBody);
        var assistantId = doc.RootElement.GetProperty("id").GetString();

        _dbUtils.RegistrarAssistantEnBd(officeId, officeName, sourceSoftware, assistantId!, vectorStoreId, openAiModel);

        return assistantId!;
    }

    /// <summary>
    /// Elimina un asistente de OpenAI
    /// </summary>
    public void EliminarAssistant(string assistantId)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {openAiApiKey}");
            httpClient.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

            var response = httpClient.DeleteAsync($"https://api.openai.com/v1/assistants/{assistantId}").Result;
            response.EnsureSuccessStatusCode();

            Console.WriteLine($"🗑️ Asistente {assistantId} eliminado exitosamente");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Error al eliminar asistente {assistantId}: {ex.Message}");
            SentrySdk.CaptureException(ex);
            // No lanzamos la excepción para no interrumpir el flujo
        }
    }

    /// <summary>
    /// Obtiene información detallada de un asistente desde OpenAI
    /// </summary>
    public async Task<AssistantInfo?> ObtenerInformacionAssistant(string assistantId)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {openAiApiKey}");
            httpClient.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

            var response = await httpClient.GetAsync($"https://api.openai.com/v1/assistants/{assistantId}");
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            return new AssistantInfo
            {
                Id = root.GetProperty("id").GetString()!,
                Name = root.GetProperty("name").GetString()!,
                Model = root.GetProperty("model").GetString()!,
                Instructions = root.GetProperty("instructions").GetString()!,
                CreatedAt = root.GetProperty("created_at").GetInt64()
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Error al obtener información del asistente {assistantId}: {ex.Message}");
            SentrySdk.CaptureException(ex);
            return null;
        }
    }

    private static string GetEnv(string key, string? defaultValue = null)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrEmpty(value))
        {
            if (defaultValue != null)
                return defaultValue;
            throw new InvalidOperationException($"Environment variable '{key}' is not set.");
        }
        return value;
    }

    internal string CrearHilodeAsisente(string assistant_id, string archivo, string vector_store_id)
    {


        // Step 2: Prepare thread creation payload
        var payload = new
        {
            metadata = new { nombre_hilo = archivo },
            tool_resources = new
            {
                file_search = new
                {
                    vector_store_ids = new[] { vector_store_id }
                }
            }
        };

        // Step 3: Call OpenAI API to create thread
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {openAiApiKey}");
        httpClient.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

        var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
        var response = httpClient.PostAsync("https://api.openai.com/v1/threads", content).Result;
        response.EnsureSuccessStatusCode();

        var responseBody = response.Content.ReadAsStringAsync().Result;
        using var doc = System.Text.Json.JsonDocument.Parse(responseBody);
        var threadId = doc.RootElement.GetProperty("id").GetString();

        return threadId!;
    }

    public async Task<string?> SubirArchivoAVectorStore(byte[] contenido, string nombreArchivo, string vectorStoreId)
    {
        // 1. Subir archivo
        var fileId = await SubirArchivoAsync(contenido, nombreArchivo);

        // 2. Asociar archivo al vector store
        await AsociarArchivoAVectorStoreAsync(fileId, vectorStoreId);


        await EsperarArchivoProcesadoAsync(vectorStoreId, fileId);

        return fileId;
    }

    private async Task<string> SubirArchivoAsync(byte[] contenido, string nombreArchivo)
    {
        var url = "https://api.openai.com/v1/files";




        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(contenido);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain"); // <-- Aquí ajustado a texto plano
        content.Add(fileContent, "file", nombreArchivo);

        content.Add(new StringContent("assistants"), "purpose");

        var response = await httpClient.PostAsync(url, content);
        string responseBody = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var fileId = doc.RootElement.GetProperty("id").GetString();

        return fileId!;
    }
    private async Task AsociarArchivoAVectorStoreAsync(string fileId, string vectorStoreId)
    {
        // Ejemplo ilustrativo, adapta la URL y payload según tu API real
        var url = $"https://api.openai.com/v1/vector_stores/{vectorStoreId}/files";

        var jsonPayload = new
        {
            file_id = fileId
        };

        var jsonString = System.Text.Json.JsonSerializer.Serialize(jsonPayload);
        using var content = new StringContent(jsonString, System.Text.Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync(url, content);
        string responseBody = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();
    }


    private async Task EsperarArchivoProcesadoAsync(string vectorStoreId, string fileId, int timeoutSegundos = 90, int intervaloMs = 1500)
    {
        var url = $"https://api.openai.com/v1/vector_stores/{vectorStoreId}/files/{fileId}";

        var tiempoInicio = DateTime.UtcNow;

        while ((DateTime.UtcNow - tiempoInicio).TotalSeconds < timeoutSegundos)
        {
            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string responseBody = await response.Content.ReadAsStringAsync();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var status = doc.RootElement.GetProperty("status").GetString();

            if (status == "processed" || status == "completed")
            {
                return; // Archivo listo
            }
            else if (status == "failed")
            {
                throw new Exception("El procesamiento del archivo falló.");
            }

            await Task.Delay(intervaloMs);
        }

        throw new TimeoutException("Tiempo de espera para que el archivo se procese excedido.");
    }
    public string LanzarRunConArchivo(string assistantId, string threadId, string fileId, string instrucciones)
    {
        // Step 1: Add user message to thread with file attachment
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {openAiApiKey}");
        httpClient.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

        // Create message with attachment
        var messagePayload = new
        {
            role = "user",
            content = instrucciones,
            attachments = new[]
            {
                new
                {
                    file_id = fileId,
                    tools = new[] { new { type = "file_search" } }
                }
            }
        };
        var messageContent = new StringContent(JsonSerializer.Serialize(messagePayload), Encoding.UTF8, "application/json");
        var messageResponse = httpClient.PostAsync(
            $"https://api.openai.com/v1/threads/{threadId}/messages", messageContent).Result;
        messageResponse.EnsureSuccessStatusCode();

        // Step 2: Create the run
        var runPayload = new
        {
            assistant_id = assistantId
        };
        var runContent = new StringContent(JsonSerializer.Serialize(runPayload), Encoding.UTF8, "application/json");
        var runResponse = httpClient.PostAsync(
            $"https://api.openai.com/v1/threads/{threadId}/runs", runContent).Result;
        runResponse.EnsureSuccessStatusCode();

        var runBody = runResponse.Content.ReadAsStringAsync().Result;
        using var runDoc = JsonDocument.Parse(runBody);
        var runId = runDoc.RootElement.GetProperty("id").GetString();

        return runId!;
    }
    public Dictionary<string, object> EsperarResultadoRun(string threadId, string runId, int timeoutSeconds = 120)
    {
        // Optimizado: timeout reducido de 360s a 120s y polling más frecuente
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {openAiApiKey}");
        httpClient.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

        JsonElement run = default;
        var pollInterval = 500; // Reducido de 1000ms a 500ms
        var maxPolls = (timeoutSeconds * 1000) / pollInterval;

        for (int i = 0; i < maxPolls; i++)
        {
            var runResp = httpClient.GetAsync($"https://api.openai.com/v1/threads/{threadId}/runs/{runId}").Result;
            runResp.EnsureSuccessStatusCode();
            var runBody = runResp.Content.ReadAsStringAsync().Result;
            using var runDoc = JsonDocument.Parse(runBody);
            run = runDoc.RootElement.Clone();
            var status = run.GetProperty("status").GetString();
            if (status == "completed")
                break;
            if (status == "failed" || status == "cancelled" || status == "expired")
                throw new Exception($"El run terminó con estado: {status}");
            Thread.Sleep(pollInterval);
        }

        // Get usage info
        //var usage = run.TryGetProperty("usage", out var usageProp) ? usageProp : default;

        // Get messages
        var msgResp = httpClient.GetAsync($"https://api.openai.com/v1/threads/{threadId}/messages").Result;
        msgResp.EnsureSuccessStatusCode();
        //deserializar comom AssistantResponse
        var msgBody = msgResp.Content.ReadAsStringAsync().Result;
        AssistantResponse assistantResponse = JsonSerializer.Deserialize<AssistantResponse>(msgBody)
            ?? throw new Exception("No se pudo deserializar la respuesta del asistente.");


        //var msgBody = msgResp.Content.ReadAsStringAsync().Result;
        //using var msgDoc = JsonDocument.Parse(msgBody);
        var messages = assistantResponse.data;

        foreach (var msg in messages)
        {
            if (msg.role == "assistant")
            {
                var respuestaCompleta = msg;

                var contentArr = msg.content;
                string? resultado = null;

                foreach (var content in contentArr)
                {
                    if (content.type == "text")
                    {
                        resultado = content.text.value;
                        break; // Solo necesitamos el primer texto
                    }
                }


                var result = new Dictionary<string, object>
                {
                    ["resultado"] = resultado,
                    ["run_id"] = runId,
                    //            //["tokens"] = usage.ValueKind != JsonValueKind.Undefined ? usage : null,
                    ["mensaje_completo"] = JsonSerializer.Deserialize<object>(JsonSerializer.Serialize(respuestaCompleta))
                };
                return result;
            }
        }

        throw new Exception("No se encontró una respuesta del assistant.");
    }

    // ===== MÉTODOS PARA FINE-TUNING =====

    /// <summary>
    /// Sube un archivo JSONL para fine-tuning
    /// </summary>
    public async Task<string> SubirArchivoParaFineTuning(byte[] contenido, string nombreArchivo)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {openAiApiKey}");

        using var fileStream = new MemoryStream(contenido);
        using var form = new MultipartFormDataContent();
        form.Add(new StreamContent(fileStream), "file", nombreArchivo);
        form.Add(new StringContent("fine-tune"), "purpose");

        var uploadResponse = await httpClient.PostAsync("https://api.openai.com/v1/files", form);
        uploadResponse.EnsureSuccessStatusCode();

        var uploadBody = await uploadResponse.Content.ReadAsStringAsync();
        using var uploadDoc = JsonDocument.Parse(uploadBody);
        var fileId = uploadDoc.RootElement.GetProperty("id").GetString();

        // Esperar a que el archivo esté procesado
        await EsperarProcesamientoArchivo(fileId);

        return fileId!;
    }

    /// <summary>
    /// Crea un job de fine-tuning
    /// </summary>
    public async Task<string> CrearJobFineTuning(string fileId, int officeId)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {openAiApiKey}");

        var payload = new
        {
            model = "gpt-4.1-mini-2025-04-14",
            training_file = fileId,
            hyperparameters = new
            {
                n_epochs = 3
            },
            suffix = $"office-{officeId}-{DateTime.UtcNow:yyyyMMdd}"
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync("https://api.openai.com/v1/fine_tuning/jobs", content);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseBody);
        var jobId = doc.RootElement.GetProperty("id").GetString();

        return jobId!;
    }

    /// <summary>
    /// Verifica el estado de un job de fine-tuning
    /// </summary>
    public async Task<FineTuningJobStatus> VerificarEstadoJobFineTuning(string jobId)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {openAiApiKey}");

        var response = await httpClient.GetAsync($"https://api.openai.com/v1/fine_tuning/jobs/{jobId}");
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        string? errorMsg = null;
        if (root.TryGetProperty("error", out var errorProp) && errorProp.ValueKind == JsonValueKind.Object)
        {
            if (errorProp.TryGetProperty("message", out var msgProp))
                errorMsg = msgProp.GetString();
        }

        string? fineTunedModel = null;
        if (root.TryGetProperty("fine_tuned_model", out var modelProp) && modelProp.ValueKind == JsonValueKind.String)
            fineTunedModel = modelProp.GetString();

        return new FineTuningJobStatus
        {
            Id = root.GetProperty("id").GetString() ?? "",
            Status = root.GetProperty("status").GetString() ?? "",
            FineTunedModel = fineTunedModel,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("created_at").GetInt64()).DateTime,
            FinishedAt = root.TryGetProperty("finished_at", out var finishedProp) && finishedProp.ValueKind != JsonValueKind.Null
                ? DateTimeOffset.FromUnixTimeSeconds(finishedProp.GetInt64()).DateTime
                : null,
            Error = errorMsg
        };
    }

    /// <summary>
    /// Espera a que un archivo esté procesado
    /// </summary>
    private async Task EsperarProcesamientoArchivo(string fileId)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {openAiApiKey}");

        while (true)
        {
            var response = await httpClient.GetAsync($"https://api.openai.com/v1/files/{fileId}");
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseBody);
            var status = doc.RootElement.GetProperty("status").GetString();

            if (status == "processed")
            {
                break;
            }
            else if (status == "error")
            {
                throw new Exception("Error al procesar el archivo para fine-tuning.");
            }

            await Task.Delay(1000); // Reducido de 2000ms a 1000ms
        }
    }

    // Nuevo: Crear vector store en OpenAI
    public string CrearVectorStoreParaOficina(int officeId, string officeName)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {openAiApiKey}");
        httpClient.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

        var payload = new
        {
            name = $"VectorStore_Oficina_{officeId}_{officeName}",
            //description = $"Vector store para la oficina {officeName} (ID: {officeId})"
        };
        var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
        var response = httpClient.PostAsync("https://api.openai.com/v1/vector_stores", content).Result;
        response.EnsureSuccessStatusCode();
        var responseBody = response.Content.ReadAsStringAsync().Result;
        using var doc = System.Text.Json.JsonDocument.Parse(responseBody);
        var vectorStoreId = doc.RootElement.GetProperty("id").GetString();
        return vectorStoreId!;
    }

    /// <summary>
    /// Procesa un chunk de texto directamente con el asistente sin usar vector store
    /// Crea un thread único para cada chunk para evitar conflictos en procesamiento paralelo
    /// </summary>
    public async Task<object> ProcesarChunkConAsistente(string assistantId, string chunkContent, string? promptPersonalizado = null)
    {
        // Crear un thread único para este chunk (no usar cache para evitar conflictos)
        var threadId = await CrearThreadAsync();

        // Construir el prompt final
        var promptFinal = !string.IsNullOrEmpty(promptPersonalizado)
            ? $"{promptPersonalizado}\n\nProcesa el siguiente contenido:\n{chunkContent}"
            : $"Procesa el siguiente contenido y extrae los datos relevantes en formato JSON:\n{chunkContent}";

        // Enviar mensaje al thread
        await EnviarMensajeAThread(threadId, promptFinal);

        // Ejecutar el asistente con timeout optimizado
        var runId = await EjecutarAsistente(assistantId, threadId);

        // Esperar y obtener resultado con timeout reducido
        var resultado = EsperarResultadoRun(threadId, runId, timeoutSeconds: 90);

        return resultado;
    }

    /// <summary>
    /// Obtiene un thread reutilizable para una oficina específica
    /// </summary>
    private async Task<string> ObtenerThreadReutilizable(string assistantId)
    {
        // Obtener office_id del asistente para el cache
        var assistant = _dbUtils.ObtenerAssistantIdPorOficina(GetOfficeIdFromAssistant(assistantId));
        var officeId = assistant?.oficina_id ?? 0;

        // Verificar si tenemos un thread válido en cache
        if (_threadCacheByOffice.TryGetValue(officeId, out var cachedThreadId) &&
            _threadCacheExpiry.TryGetValue(officeId, out var expiry) &&
            DateTime.UtcNow < expiry)
        {
            return cachedThreadId;
        }

        // Crear nuevo thread y guardarlo en cache
        var threadId = await CrearThreadAsync();
        _threadCacheByOffice[officeId] = threadId;
        _threadCacheExpiry[officeId] = DateTime.UtcNow.Add(_threadCacheTimeout);

        return threadId;
    }

    /// <summary>
    /// Extrae el office_id del assistant_id usando una aproximación simple
    /// </summary>
    private int GetOfficeIdFromAssistant(string assistantId)
    {
        try
        {
            // Buscar el asistente en la base de datos por assistant_id
            using var conn = _dbUtils.GetConnection();
            conn.Open();
            using var cmd = new Npgsql.NpgsqlCommand(
                "SELECT oficina_id FROM asistente_oficina WHERE assistant_id = @assistantId LIMIT 1", conn);
            cmd.Parameters.AddWithValue("assistantId", assistantId);

            var result = cmd.ExecuteScalar();
            return result != null ? Convert.ToInt32(result) : 0;
        }
        catch
        {
            return 0; // Fallback
        }
    }

    /// <summary>
    /// Crea un thread temporal
    /// </summary>
    private async Task<string> CrearThreadAsync()
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {openAiApiKey}");
        httpClient.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

        var payload = new { };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync("https://api.openai.com/v1/threads", content);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseBody);
        var threadId = doc.RootElement.GetProperty("id").GetString();

        return threadId!;
    }

    /// <summary>
    /// Envía un mensaje a un thread
    /// </summary>
    private async Task EnviarMensajeAThread(string threadId, string mensaje)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {openAiApiKey}");
        httpClient.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

        var payload = new
        {
            role = "user",
            content = mensaje
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync($"https://api.openai.com/v1/threads/{threadId}/messages", content);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Ejecuta el asistente en un thread
    /// </summary>
    private async Task<string> EjecutarAsistente(string assistantId, string threadId)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {openAiApiKey}");
        httpClient.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

        var payload = new
        {
            assistant_id = assistantId
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync($"https://api.openai.com/v1/threads/{threadId}/runs", content);

        //loguear la respuesta
        var responseBody = await response.Content.ReadAsStringAsync();
        _logger.LogInformation("Respuesta de OpenAI: {ResponseBody}", responseBody);

        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(responseBody);
        var runId = doc.RootElement.GetProperty("id").GetString();

        return runId!;
    }
}
