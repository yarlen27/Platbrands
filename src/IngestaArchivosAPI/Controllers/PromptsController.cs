using IngestaArchivosAPI.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace IngestaArchivosAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PromptsController(ApplicationDbContext context, ILogger<PromptsController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ObtenerPrompts()
    {
        var prompts = await context.PromptsIa
            .OrderByDescending(p => p.FechaCreacion)
            .Select(p => new
            {
                p.Id,
                p.Nombre,
                p.Descripcion,
                p.FechaCreacion,
                p.OficinaId,
                ContenidoLength = p.Contenido.Length
            })
            .ToListAsync();

        return Ok(prompts);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> ObtenerPrompt(Guid id)
    {
        var prompt = await context.PromptsIa.FindAsync(id);
        
        if (prompt == null)
            return NotFound();

        return Ok(prompt);
    }

    [HttpGet("oficina/{oficinaId}")]
    public async Task<IActionResult> ObtenerPromptPorOficina(int oficinaId)
    {
        // Buscar prompt específico para la oficina
        var promptEspecifico = await context.PromptsIa
            .Where(p => p.OficinaId == oficinaId)
            .OrderByDescending(p => p.FechaCreacion)
            .FirstOrDefaultAsync();

        if (promptEspecifico != null)
            return Ok(promptEspecifico);

        // Si no hay específico, buscar genérico
        var promptGenerico = await context.PromptsIa
            .Where(p => p.OficinaId == null)
            .OrderByDescending(p => p.FechaCreacion)
            .FirstOrDefaultAsync();

        if (promptGenerico != null)
            return Ok(promptGenerico);

        return NotFound("No se encontró prompt para la oficina especificada");
    }

    [HttpPost]
    public async Task<IActionResult> CrearPrompt([FromBody] CrearPromptRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Contenido))
            return BadRequest("El contenido del prompt es requerido");

        var hashContenido = GenerarHash(request.Contenido);

        var nuevoPrompt = new PromptIa
        {
            Nombre = request.Nombre,
            Descripcion = request.Descripcion,
            Contenido = request.Contenido,
            HashContenido = hashContenido,
            OficinaId = request.OficinaId,
            FechaCreacion = DateTime.UtcNow
        };

        context.PromptsIa.Add(nuevoPrompt);
        await context.SaveChangesAsync();

        logger.LogInformation("Nuevo prompt creado: {Nombre} para oficina {OficinaId}", 
            request.Nombre, request.OficinaId);

        return Ok(new { Id = nuevoPrompt.Id, Mensaje = "Prompt creado exitosamente" });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> ActualizarPrompt(Guid id, [FromBody] ActualizarPromptRequest request)
    {
        var prompt = await context.PromptsIa.FindAsync(id);
        
        if (prompt == null)
            return NotFound();

        var hashContenido = GenerarHash(request.Contenido);

        // Crear nueva versión del prompt en lugar de actualizar
        var nuevoPrompt = new PromptIa
        {
            Nombre = request.Nombre ?? prompt.Nombre,
            Descripcion = request.Descripcion ?? prompt.Descripcion,
            Contenido = request.Contenido,
            HashContenido = hashContenido,
            OficinaId = prompt.OficinaId,
            FechaCreacion = DateTime.UtcNow
        };

        context.PromptsIa.Add(nuevoPrompt);
        await context.SaveChangesAsync();

        logger.LogInformation("Nueva versión de prompt creada: {Nombre} para oficina {OficinaId}", 
            nuevoPrompt.Nombre, nuevoPrompt.OficinaId);

        return Ok(new { Id = nuevoPrompt.Id, Mensaje = "Nueva versión del prompt creada" });
    }

    private static string GenerarHash(string contenido)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(contenido));
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }
}

public class CrearPromptRequest
{
    public string Nombre { get; set; } = default!;
    public string? Descripcion { get; set; }
    public string Contenido { get; set; } = default!;
    public int? OficinaId { get; set; }
}

public class ActualizarPromptRequest
{
    public string? Nombre { get; set; }
    public string? Descripcion { get; set; }
    public string Contenido { get; set; } = default!;
}