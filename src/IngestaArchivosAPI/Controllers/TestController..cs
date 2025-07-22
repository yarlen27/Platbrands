using IngestaArchivosAPI.Data;
using IngestaArchivosAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Minio;
using Minio.ApiEndpoints;
using Minio.DataModel.Args;


namespace IngestaArchivosAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly MinioService _minio;

    public TestController(ApplicationDbContext db, MinioService minio)
    {
        _db = db;
        _minio = minio;
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }

    [HttpGet("db")]
    public async Task<IActionResult> ProbarBaseDatos()
    {
        var count = await _db.ArchivosIngestados.CountAsync();
        return Ok(new { mensaje = "Conexión a base de datos exitosa", registros = count });
    }

[HttpGet("minio")]
public async Task<IActionResult> ProbarMinio()
{
    try
    {
        var archivos = new List<string>();
        var listArgs = new ListObjectsArgs()
            .WithBucket("archivos-ingestados")
            .WithRecursive(true);

        await foreach (var item in _minio.Client.ListObjectsEnumAsync(listArgs))
        {
            archivos.Add(item.Key);
        }

        return Ok(new { mensaje = "Conexión a MinIO exitosa", archivos });
    }
    catch (Exception ex)
    {
        return StatusCode(500, new { mensaje = "Error al conectar con MinIO", detalle = ex.Message });
    }
}

}
