using IngestaArchivosAPI.BLL;
using Microsoft.AspNetCore.Mvc;

namespace IngestaArchivosAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ArchivosController(ArchivoService _archivoService) : ControllerBase
{
    [HttpPost("{office_id}/{userId}")]
    [RequestSizeLimit(10485760)] // 10 MB límite
    public async Task<IActionResult> SubirArchivo(IFormFile archivo, [FromRoute] int office_id, [FromRoute] int userId)
    {
        try
        {
            var resultado = await _archivoService.ProcesarArchivoAsync(archivo, office_id, userId);

            if (!resultado.Success)
            {
                return BadRequest(new 
                { 
                    error = resultado.Error, 
                    office_id, 
                    userId, 
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
                office_id,
                userId,
                fileName = archivo?.FileName
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
                office_id,
                userId,
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
