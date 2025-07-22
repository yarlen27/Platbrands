using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IngestaArchivosAPI.Services;

public class OpenAIServiceSimple
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAIServiceSimple> _logger;
    private readonly Dictionary<int, string> _vectorStoreCache = new();
    private readonly string _apiKey;

    public OpenAIServiceSimple(IConfiguration configuration, ILogger<OpenAIServiceSimple> logger, HttpClient httpClient)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClient;

        _apiKey = _configuration["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI API Key no configurada");

        if (string.IsNullOrEmpty(_apiKey) || _apiKey == "your-openai-api-key-here")
        {
            throw new InvalidOperationException("OpenAI API Key no está configurada correctamente");
        }

        _logger.LogInformation("OpenAI API Key configurada correctamente (longitud: {Length})", _apiKey.Length);

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        _httpClient.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");
    }

    public async Task<string> SubirAVectorStore(string contenido, string nombreArchivo, int oficinaId, int? archivoId = null)
    {
        _logger.LogInformation("Iniciando subida de archivo {NombreArchivo} (ID: {ArchivoId}, contenido: {Length} caracteres) para oficina {OficinaId}",
            nombreArchivo, archivoId, contenido.Length, oficinaId);

        try
        {
            // Obtener o crear VectorStore para la oficina
            var vectorStoreId = await ObtenerOCrearVectorStore(oficinaId);

            // Crear archivo temporal con el contenido
            var tempFilePath = Path.GetTempFileName();
            await File.WriteAllTextAsync(tempFilePath, contenido, Encoding.UTF8);

            _logger.LogInformation("Archivo temporal creado en {TempPath} para {NombreArchivo}", tempFilePath, nombreArchivo);

            try
            {
                // Subir archivo a OpenAI usando el ID del archivo como nombre
                var fileNameId = archivoId?.ToString() ?? Guid.NewGuid().ToString();
                var fileName = $"{fileNameId}.csv";
                var fileId = await SubirArchivo(tempFilePath, fileName);

                // Asociar archivo al VectorStore
                await AgregarArchivoAVectorStore(vectorStoreId, fileId);

                // Esperar un poco y verificar el estado del archivo
                _logger.LogInformation("Esperando para verificar estado del archivo {FileId}...", fileId);
                await Task.Delay(TimeSpan.FromSeconds(5));

                await VerificarEstadoArchivoEnVectorStore(vectorStoreId, fileId, nombreArchivo);

                _logger.LogInformation("Archivo {Nombre} subido a VectorStore {VectorStoreId} como {FileId}",
                    nombreArchivo, vectorStoreId, fileId);

                return fileId;
            }
            finally
            {
                // Limpiar archivo temporal
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subiendo archivo {Nombre} a VectorStore para oficina {OficinaId}",
                nombreArchivo, oficinaId);
            throw;
        }
    }

    private async Task<string> SubirArchivo(string filePath, string fileName)
    {
        var fileInfo = new FileInfo(filePath);
        _logger.LogInformation("Subiendo archivo {FileName} (tamaño: {Size} bytes) a OpenAI", fileName, fileInfo.Length);

        using var form = new MultipartFormDataContent();
        using var fileStream = File.OpenRead(filePath);
        using var fileContent = new StreamContent(fileStream);

        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", fileName);
        form.Add(new StringContent("assistants"), "purpose");

        _logger.LogInformation("Realizando POST a OpenAI Files API para archivo {FileName}", fileName);

        var response = await _httpClient.PostAsync("https://api.openai.com/v1/files", form);

        var responseContent = await response.Content.ReadAsStringAsync();
        _logger.LogInformation("Respuesta de OpenAI Files API: Status={Status}, Content={Content}",
            response.StatusCode, responseContent);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Error subiendo archivo {FileName}: {Status} - {Content}",
                fileName, response.StatusCode, responseContent);
            throw new Exception($"Error subiendo archivo {fileName}: {response.StatusCode} - {responseContent}");
        }

        var fileResponse = JsonSerializer.Deserialize<FileUploadResponse>(responseContent);

        if (fileResponse?.Id == null)
        {
            _logger.LogError("No se pudo obtener ID del archivo subido. Respuesta: {Content}", responseContent);
            throw new Exception($"No se pudo obtener el ID del archivo subido. Respuesta: {responseContent}");
        }

        _logger.LogInformation("Archivo {FileName} subido exitosamente con ID {FileId}", fileName, fileResponse.Id);
        return fileResponse.Id;
    }

    private async Task AgregarArchivoAVectorStore(string vectorStoreId, string fileId)
    {
        _logger.LogInformation("Agregando archivo {FileId} al VectorStore {VectorStoreId}", fileId, vectorStoreId);

        var request = new { file_id = fileId };
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"https://api.openai.com/v1/vector_stores/{vectorStoreId}/files", content);

        var responseContent = await response.Content.ReadAsStringAsync();
        _logger.LogInformation("Respuesta de agregar archivo a VectorStore: Status={Status}, Content={Content}",
            response.StatusCode, responseContent);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Error agregando archivo {FileId} al VectorStore {VectorStoreId}: {Status} - {Content}",
                fileId, vectorStoreId, response.StatusCode, responseContent);
            throw new Exception($"Error agregando archivo al VectorStore: {response.StatusCode} - {responseContent}");
        }

        _logger.LogInformation("Archivo {FileId} agregado exitosamente al VectorStore {VectorStoreId}", fileId, vectorStoreId);
    }

    private async Task VerificarEstadoArchivoEnVectorStore(string vectorStoreId, string fileId, string nombreArchivo)
    {
        try
        {
            _logger.LogInformation("Verificando estado del archivo {FileId} en VectorStore {VectorStoreId}", fileId, vectorStoreId);

            var response = await _httpClient.GetAsync($"https://api.openai.com/v1/vector_stores/{vectorStoreId}/files/{fileId}");
            var responseContent = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("Estado del archivo {NombreArchivo} ({FileId}): Status={Status}, Content={Content}",
                nombreArchivo, fileId, response.StatusCode, responseContent);

            if (response.IsSuccessStatusCode)
            {
                var fileStatus = JsonSerializer.Deserialize<VectorStoreFileStatus>(responseContent);

                if (fileStatus?.Status == "failed" && fileStatus.LastError != null)
                {
                    _logger.LogError("Archivo {NombreArchivo} falló en VectorStore. Error: {Error}",
                        nombreArchivo, JsonSerializer.Serialize(fileStatus.LastError));
                }
                else if (fileStatus?.Status == "in_progress")
                {
                    _logger.LogInformation("Archivo {NombreArchivo} aún está procesándose en VectorStore", nombreArchivo);
                }
                else if (fileStatus?.Status == "completed")
                {
                    _logger.LogInformation("Archivo {NombreArchivo} procesado exitosamente en VectorStore", nombreArchivo);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verificando estado del archivo {FileId}", fileId);
        }
    }

    private async Task<string> ObtenerOCrearVectorStore(int oficinaId)
    {
        // Cache en memoria para evitar llamadas repetidas
        if (_vectorStoreCache.TryGetValue(oficinaId, out var cachedVectorStoreId))
        {
            return cachedVectorStoreId;
        }

        var nombreVectorStore = $"oficina_{oficinaId}";

        try
        {
            // Buscar VectorStore existente
            var response = await _httpClient.GetAsync("https://api.openai.com/v1/vector_stores");
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var vectorStoresList = JsonSerializer.Deserialize<VectorStoreListResponse>(responseContent);

            var vectorStoreExistente = vectorStoresList?.Data?.FirstOrDefault(vs => vs.Name == nombreVectorStore);
            if (vectorStoreExistente != null)
            {
                _vectorStoreCache[oficinaId] = vectorStoreExistente.Id;
                return vectorStoreExistente.Id;
            }

            // Crear nuevo VectorStore
            var createRequest = new { name = nombreVectorStore };
            var json = JsonSerializer.Serialize(createRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation("Creando VectorStore para oficina {OficinaId} con nombre {Nombre}", oficinaId, nombreVectorStore);

            var createResponse = await _httpClient.PostAsync("https://api.openai.com/v1/vector_stores", content);

            var createResponseContent = await createResponse.Content.ReadAsStringAsync();
            _logger.LogInformation("Respuesta de creación VectorStore: Status={Status}, Content={Content}",
                createResponse.StatusCode, createResponseContent);

            if (!createResponse.IsSuccessStatusCode)
            {
                throw new Exception($"Error creando VectorStore: {createResponse.StatusCode} - {createResponseContent}");
            }

            var nuevoVectorStore = JsonSerializer.Deserialize<VectorStoreResponse>(createResponseContent);

            if (nuevoVectorStore?.Id == null)
                throw new Exception($"No se pudo crear el VectorStore. Respuesta: {createResponseContent}");

            _vectorStoreCache[oficinaId] = nuevoVectorStore.Id;

            _logger.LogInformation("Creado nuevo VectorStore {VectorStoreId} para oficina {OficinaId}",
                nuevoVectorStore.Id, oficinaId);

            return nuevoVectorStore.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error obteniendo/creando VectorStore para oficina {OficinaId}", oficinaId);
            throw;
        }
    }

    public async Task<bool> EliminarArchivoDelVectorStore(string vectorStoreFileId, int oficinaId)
    {
        try
        {
            var vectorStoreId = await ObtenerOCrearVectorStore(oficinaId);
            var response = await _httpClient.DeleteAsync($"https://api.openai.com/v1/vector_stores/{vectorStoreId}/files/{vectorStoreFileId}");

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Archivo {FileId} eliminado del VectorStore {VectorStoreId}",
                    vectorStoreFileId, vectorStoreId);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error eliminando archivo {FileId} del VectorStore", vectorStoreFileId);
            return false;
        }
    }

    // DTOs para las respuestas de la API
    private class FileUploadResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }

    private class VectorStoreResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private class VectorStoreListResponse
    {
        [JsonPropertyName("data")]
        public VectorStoreResponse[]? Data { get; set; }
    }

    private class VectorStoreFileStatus
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("last_error")]
        public ErrorDetails? LastError { get; set; }

        [JsonPropertyName("usage_bytes")]
        public int UsageBytes { get; set; }
    }

    private class ErrorDetails
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}