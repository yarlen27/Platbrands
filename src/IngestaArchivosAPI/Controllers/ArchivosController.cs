using IngestaArchivosAPI.BLL;
using Microsoft.AspNetCore.Mvc;

namespace IngestaArchivosAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ArchivosController(ArchivoService _archivoService) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(10485760)] // 10 MB límite
    public async Task<IActionResult> SubirArchivo(IFormFile archivo)
    {
        try
        {
            var resultado = await _archivoService.ProcesarArchivoAsync(archivo);

            if (!resultado.Success)
            {
                return BadRequest(new 
                { 
                    error = resultado.Error, 
                    fileName = archivo?.FileName,
                    details = "Error en procesamiento OCR de PDF"
                });
            }

            return Ok(new
            {
                success = true,
                totalSeconds = resultado.TotalSeconds,
                timings = resultado.Timings,
                extractedText = resultado.ExtractedText,
                textLength = resultado.ExtractedText?.Length ?? 0,
                fileName = archivo?.FileName,
                savedFilePath = resultado.SavedFilePath
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "Error interno del servidor",
                message = ex.Message,
                innerException = ex.InnerException?.Message,
                stackTrace = ex.StackTrace,
                fileName = archivo?.FileName,
                timestamp = DateTime.UtcNow
            });
        }
    }

    [HttpGet("version")]
    public IActionResult GetVersion()
    {
        var versionFile = Path.Combine(Directory.GetCurrentDirectory(), "version.txt");
        var commitHash = "archivo no existe";
        var buildDate = "archivo no existe";

        if (System.IO.File.Exists(versionFile))
        {
            var lines = System.IO.File.ReadAllLines(versionFile);
            commitHash = lines.Length > 0 ? lines[0] : "unknown";
            buildDate = lines.Length > 1 ? lines[1] : "unknown";
        }

        return Ok(new
        {
            version = commitHash,
            commit = commitHash,
            buildDate = buildDate,
            timestamp = DateTime.UtcNow,
            environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown"
        });
    }
}
