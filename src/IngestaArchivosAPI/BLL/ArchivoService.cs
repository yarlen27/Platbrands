using Microsoft.AspNetCore.Http;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IngestaArchivosAPI.BLL;

// Simplified result for OCR-only processing
public record OcrResult(
    bool Success,
    string? Error,
    double TotalSeconds,
    string? ExtractedText,
    Dictionary<string, double> Timings,
    string? SavedFilePath
);

public class ArchivoService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ArchivoService> _logger;

    public ArchivoService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<ArchivoService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<OcrResult> ProcesarArchivoAsync(IFormFile archivo)
    {
        DateTime time0 = DateTime.Now;
        var timing = new Dictionary<string, double>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Validar que el archivo sea PDF
        var extension = Path.GetExtension(archivo.FileName).ToLower();
        if (extension != ".pdf")
        {
            return new OcrResult(
                Success: false,
                Error: $"Solo se procesan archivos PDF. Archivo recibido: {extension}",
                TotalSeconds: 0,
                ExtractedText: null,
                Timings: timing,
                SavedFilePath: null
            );
        }

        _logger.LogInformation("🔍 Procesando PDF con OCR - Archivo: {FileName}, Tamaño: {Size} bytes", 
            archivo.FileName, archivo.Length);

        try
        {
            stopwatch.Restart();
            // Llamar al servicio OCR para archivos PDF
            var contenidoTexto = await ProcesarPdfConOcr(archivo);
            timing["ProcesarPdfConOcr"] = stopwatch.Elapsed.TotalSeconds;
            
            stopwatch.Restart();
            // Guardar el texto extraído en archivo .md
            var savedFilePath = await GuardarTextoComoMarkdown(archivo.FileName, contenidoTexto);
            timing["GuardarMarkdown"] = stopwatch.Elapsed.TotalSeconds;
            
            var duration = DateTime.Now - time0;
            timing["Total"] = duration.TotalSeconds;

            _logger.LogInformation("✅ OCR completado - Texto extraído: {Length} caracteres en {TotalSeconds}s", 
                contenidoTexto?.Length ?? 0, duration.TotalSeconds);

            return new OcrResult(
                Success: true,
                Error: null,
                TotalSeconds: duration.TotalSeconds,
                ExtractedText: contenidoTexto,
                Timings: timing,
                SavedFilePath: savedFilePath
            );
        }
        catch (Exception ex)
        {
            var duration = DateTime.Now - time0;
            timing["Total"] = duration.TotalSeconds;
            
            _logger.LogError(ex, "❌ Error procesando PDF: {Message}", ex.Message);
            
            return new OcrResult(
                Success: false,
                Error: ex.Message,
                TotalSeconds: duration.TotalSeconds,
                ExtractedText: null,
                Timings: timing,
                SavedFilePath: null
            );
        }
    }

    private async Task<string> ProcesarPdfConOcr(IFormFile archivo)
    {
        try
        {
            var ocrEndpoint = _configuration["OCR:Endpoint"];
            _logger.LogInformation("🌐 OCR Endpoint configurado: {Endpoint}", ocrEndpoint);

            if (string.IsNullOrEmpty(ocrEndpoint))
            {
                _logger.LogError("❌ OCR endpoint no configurado en appsettings");
                throw new InvalidOperationException("OCR endpoint no configurado en appsettings");
            }

            _logger.LogInformation("📦 Preparando contenido multipart para envío a OCR - Archivo: {FileName}, Tamaño: {Size} bytes",
                archivo.FileName, archivo.Length);

            using var content = new MultipartFormDataContent();
            using var streamContent = new StreamContent(archivo.OpenReadStream());
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            content.Add(streamContent, "file", archivo.FileName);

            _logger.LogInformation("🚀 Enviando petición POST a OCR: {Endpoint}", ocrEndpoint);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            using var httpClient = _httpClientFactory.CreateClient("OCR");
            var response = await httpClient.PostAsync(ocrEndpoint, content);
            stopwatch.Stop();

            _logger.LogInformation("⏱️ Petición OCR completada en {ElapsedMs}ms - Status: {StatusCode}",
                stopwatch.ElapsedMilliseconds, response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("❌ Error en respuesta OCR: {StatusCode} - {Content}", response.StatusCode, errorContent);
                throw new InvalidOperationException($"Error del servicio OCR: {response.StatusCode} - {errorContent}");
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("📄 Respuesta OCR recibida - Tamaño: {Size} caracteres", jsonResponse?.Length ?? 0);
            
            // LOG TEMPORAL: Ver respuesta completa del OCR para análisis
            _logger.LogInformation("🔍 RESPUESTA COMPLETA OCR: {JsonResponse}", jsonResponse);

            // Parsear la respuesta JSON y extraer el texto reconstruido
            using var document = JsonDocument.Parse(jsonResponse);
            var root = document.RootElement;

            if (root.TryGetProperty("reconstructed_text", out var reconstructedTextElement))
            {
                var textoExtraido = reconstructedTextElement.GetString() ?? string.Empty;
                _logger.LogInformation("✅ Texto extraído exitosamente - {Length} caracteres", textoExtraido.Length);
                return textoExtraido;
            }
            else
            {
                _logger.LogError("❌ La respuesta del OCR no contiene el campo 'reconstructed_text'");
                _logger.LogDebug("🔍 Respuesta completa del OCR: {JsonResponse}", jsonResponse);
                throw new InvalidOperationException("La respuesta del OCR no contiene el campo 'reconstructed_text'");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Error crítico procesando PDF con OCR - Archivo: {FileName}, Error: {Message}",
                archivo?.FileName, ex.Message);
            throw new InvalidOperationException($"Error al procesar PDF con OCR: {ex.Message}", ex);
        }
    }

    private async Task<string> GuardarTextoComoMarkdown(string nombreArchivo, string textoExtraido)
    {
        try
        {
            // Crear directorio si no existe
            var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "extracted-texts");
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
                _logger.LogInformation("📁 Directorio creado: {OutputDir}", outputDir);
            }

            // Generar nombre único para el archivo .md
            var nombreSinExtension = Path.GetFileNameWithoutExtension(nombreArchivo);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var nombreArchivoMd = $"{nombreSinExtension}_{timestamp}.md";
            var rutaCompleta = Path.Combine(outputDir, nombreArchivoMd);

            // Escribir el contenido al archivo .md
            await File.WriteAllTextAsync(rutaCompleta, textoExtraido);
            
            _logger.LogInformation("💾 Texto extraído guardado en: {FilePath}", rutaCompleta);
            
            return rutaCompleta;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error guardando archivo .md: {Message}", ex.Message);
            throw new InvalidOperationException($"Error guardando archivo .md: {ex.Message}", ex);
        }
    }

}