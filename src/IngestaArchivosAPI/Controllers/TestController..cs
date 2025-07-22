using Microsoft.AspNetCore.Mvc;

namespace IngestaArchivosAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    public TestController()
    {
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }

    [HttpGet("ocr-config")]
    public IActionResult ProbarConfiguracionOcr()
    {
        var ocrEndpoint = Environment.GetEnvironmentVariable("OCR__Endpoint") ?? "No configurado";
        return Ok(new { mensaje = "Configuración OCR", endpoint = ocrEndpoint });
    }

}
