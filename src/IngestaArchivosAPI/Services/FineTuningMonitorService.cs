using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using IngestaArchivosAPI.Data;
using IngestaArchivosAPI.Utils;
using Microsoft.EntityFrameworkCore;

namespace IngestaArchivosAPI.Services;

public class FineTuningMonitorService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FineTuningMonitorService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1); // Cada minuto

    public FineTuningMonitorService(
        IServiceProvider serviceProvider,
        ILogger<FineTuningMonitorService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🔄 Fine-tuning Monitor Service iniciado");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MonitorearJobsDeFineTuning();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en el monitoreo de fine-tuning");
            }

            // Esperar 1 minuto antes del siguiente chequeo
            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private async Task MonitorearJobsDeFineTuning()
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var openAIUtils = scope.ServiceProvider.GetRequiredService<OpenAIUtils>();

        // Obtener jobs que están corriendo
        var jobsRunning = await context.FineTuningJobs
            .Where(job => job.Status == "running")
            .ToListAsync();

        if (!jobsRunning.Any())
        {
            _logger.LogDebug("📊 No hay jobs de fine-tuning en ejecución");
            return;
        }

        _logger.LogInformation($"🔍 Monitoreando {jobsRunning.Count} jobs de fine-tuning");

        foreach (var job in jobsRunning)
        {
            try
            {
                await ProcesarJobDeFineTuning(job, context, openAIUtils);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Error procesando job {job.JobId} para oficina {job.OfficeId}");
            }
        }
    }

    private async Task ProcesarJobDeFineTuning(
        FineTuningJob job, 
        ApplicationDbContext context, 
        OpenAIUtils openAIUtils)
    {
        _logger.LogDebug($"🔍 Verificando estado del job {job.JobId}");

        // Consultar estado en OpenAI
        var jobStatus = await openAIUtils.VerificarEstadoJobFineTuning(job.JobId);

        // Actualizar estado en BD
        job.Status = jobStatus.Status;
        job.UpdatedAt = DateTime.UtcNow;

        if (jobStatus.Status == "succeeded")
        {
            _logger.LogInformation($"✅ Job {job.JobId} completado exitosamente para oficina {job.OfficeId}");
            
            // Actualizar con el modelo fine-tuneado
            job.FineTunedModelId = jobStatus.FineTunedModel;
            
            // Actualizar el asistente de la oficina con el nuevo modelo
            await ActualizarModeloDelAsistente(job.OfficeId, jobStatus.FineTunedModel!, context);
        }
        else if (jobStatus.Status == "failed")
        {
            _logger.LogError($"❌ Job {job.JobId} falló para oficina {job.OfficeId}. Error: {jobStatus.Error}");
        }
        else if (jobStatus.Status == "cancelled")
        {
            _logger.LogWarning($"⚠️ Job {job.JobId} fue cancelado para oficina {job.OfficeId}");
        }
        else
        {
            _logger.LogDebug($"⏳ Job {job.JobId} sigue en progreso: {jobStatus.Status}");
        }

        await context.SaveChangesAsync();
    }

    private async Task ActualizarModeloDelAsistente(int officeId, string nuevoModeloId, ApplicationDbContext context)
    {
        var asistente = await context.Assistants
            .Where(a => a.oficina_id == officeId && a.activo == true)
            .FirstOrDefaultAsync();

        if (asistente != null)
        {
            var modeloAnterior = asistente.model_id;
            asistente.model_id = nuevoModeloId;
            asistente.fecha_creacion = DateTime.UtcNow; // Actualizar fecha para indicar cambio

            await context.SaveChangesAsync();

            _logger.LogInformation($"🔄 Asistente de oficina {officeId} actualizado:");
            _logger.LogInformation($"   Modelo anterior: {modeloAnterior}");
            _logger.LogInformation($"   Modelo nuevo: {nuevoModeloId}");
        }
        else
        {
            _logger.LogWarning($"⚠️ No se encontró asistente activo para oficina {officeId}");
        }
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🛑 Fine-tuning Monitor Service detenido");
        await base.StopAsync(stoppingToken);
    }
} 