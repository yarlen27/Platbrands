using Microsoft.AspNetCore.Http;
using IngestaArchivosAPI.Data;
using IngestaArchivosAPI.Services;
using IngestaArchivosAPI.Utils;
using System.Text;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using IngestaArchivosAPI.Models;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;

namespace IngestaArchivosAPI.BLL;

public record ChunkResult(
    int Index,
    List<object> Transacciones,
    Dictionary<string, double> Timing,
    bool Success,
    string? Error
);

public class ArchivoService
{
    private readonly ApplicationDbContext _context;
    private readonly OpenAIUtils _openAIUtils;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ArchivoService> _logger;

    public ArchivoService(ApplicationDbContext context, OpenAIUtils openAIUtils, IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<ArchivoService> logger)
    {
        _context = context;
        _openAIUtils = openAIUtils;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ProcesarArchivoResult> ProcesarArchivoAsync(IFormFile archivo, int officeId, int userId)
    {
        DateTime time0 = DateTime.Now;
        var timing = new Dictionary<string, double>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Validar si la oficina tiene asistente configurado
        stopwatch.Restart();
        var asistente = await _context.Assistants
            .Where(a => a.oficina_id == officeId && a.activo == true)
            .FirstOrDefaultAsync();
        timing["BuscarAsistente"] = stopwatch.Elapsed.TotalSeconds;

        if (asistente == null)
        {
            // Crear asistente para la oficina si no existe
            try
            {
                stopwatch.Restart();
                var assistantId = _openAIUtils.CreateAssistantForOffice(officeId, $"Oficina_{officeId}", "Sistema_General");
                timing["CrearAsistente"] = stopwatch.Elapsed.TotalSeconds;

                stopwatch.Restart();
                asistente = await _context.Assistants
                    .Where(a => a.oficina_id == officeId && a.activo == true)
                    .FirstOrDefaultAsync();
                timing["BuscarAsistenteDespuesCrear"] = stopwatch.Elapsed.TotalSeconds;

                if (asistente == null)
                {
                    return new ProcesarArchivoResult(false, $"Error al crear asistente para la oficina {officeId}", -1, null, timing);
                }
            }
            catch (Exception ex)
            {
                return new ProcesarArchivoResult(false, $"Error al crear asistente: {ex.Message}", -1, null, timing);
            }
        }

        // Extraer contenido del archivo
        var extension = Path.GetExtension(archivo.FileName).ToLower();

        stopwatch.Restart();
        using var stream = archivo.OpenReadStream();
        var contenidoBytes = new byte[stream.Length];
        await stream.ReadAsync(contenidoBytes, 0, contenidoBytes.Length);
        timing["LeerArchivo"] = stopwatch.Elapsed.TotalSeconds;

        string contenidoTexto;

        if (extension == ".pdf")
        {
            _logger.LogInformation("🔍 Procesando PDF con OCR...");
            stopwatch.Restart();
            // Llamar al servicio OCR para archivos PDF
            contenidoTexto = await ProcesarPdfConOcr(archivo);
            timing["ProcesarPdfConOcr"] = stopwatch.Elapsed.TotalSeconds;
            _logger.LogInformation("✅ OCR completado - Texto extraído: {Length} caracteres", contenidoTexto?.Length ?? 0);
        }
        else
        {
            stopwatch.Restart();
            contenidoTexto = FileUtils.ExtractTextContent(archivo.FileName, contenidoBytes);
            timing["ExtractTextContent"] = stopwatch.Elapsed.TotalSeconds;
        }

        // Procesar según el tipo de archivo
        stopwatch.Restart();
        var chunks = new List<string>();

        //los chunks seran hasta por página cada pagina termina en un string q comienza con: ------------------------- FIN PÁGINA, tener en cuenta q es necesario elimnar la linea de separación completa

        var lineas = contenidoTexto.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

        var paginaActual = new StringBuilder();
        foreach (var linea in lineas)
        {
            if (linea.StartsWith("------------------------- FIN PÁGINA"))
            {
                // Si hay contenido en la página actual, agregarlo como chunk
                if (paginaActual.Length > 0)
                {

                    string paginaChunk = paginaActual.ToString().Trim();

                    chunks.Add(paginaChunk);
                    paginaActual.Clear();
                }
            }
            else
            {
                // Agregar línea a la página actual
                paginaActual.AppendLine(linea);
            }
        }
        timing["DividirEnChunks"] = stopwatch.Elapsed.TotalSeconds;

        // Obtener el prompt para la oficina
        stopwatch.Restart();
        var prompt = await _context.PromptsIa
            .Where(p => p.OficinaId == officeId)
            .OrderByDescending(p => p.FechaCreacion)
            .FirstOrDefaultAsync();
        timing["ObtenerPrompt"] = stopwatch.Elapsed.TotalSeconds;

        // Si no existe prompt para la oficina, crear uno por defecto
        if (prompt == null)
        {
            stopwatch.Restart();
            var defaultPromptContent = "You are a medical billing data extraction specialist. Extract transaction data from financial documents and return valid JSON with these exact fields: patient_id, patient_name, insurance_company, check_amount, posted_amount, check_number, service_date, code, other_amount. Use null for missing values. Return array of objects for multiple transactions.";

            var defaultPrompt = new PromptIa
            {
                Id = Guid.NewGuid(),
                Nombre = $"Prompt por defecto - Oficina {officeId}",
                Descripcion = "Prompt por defecto creado automáticamente para extracción de datos médicos",
                Contenido = defaultPromptContent,
                HashContenido = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(defaultPromptContent)).Aggregate("", (current, b) => current + b.ToString("x2")),
                FechaCreacion = DateTime.UtcNow,
                OficinaId = officeId
            };

            _context.PromptsIa.Add(defaultPrompt);
            await _context.SaveChangesAsync();
            prompt = defaultPrompt;
            timing["CrearPromptPorDefecto"] = stopwatch.Elapsed.TotalSeconds;

            _logger.LogInformation("✅ Prompt por defecto creado para oficina {OfficeId}", officeId);
        }

        var promptTexto = prompt.Contenido;



        // Optimización: Si hay muchos chunks, usar modelo más rápido para chunks simples
        var useOptimizedModel = chunks.Count > 5;
        if (useOptimizedModel)
        {
            _logger.LogInformation("🚀 Usando modelo optimizado para {ChunkCount} chunks", chunks.Count);
        }

        // Procesar chunks en paralelo con el asistente y consolidar resultados
        var todasLasTransacciones = new List<object>();
        var chunkTimings = new List<Dictionary<string, double>>();

        _logger.LogInformation("🚀 Iniciando procesamiento paralelo de {ChunkCount} chunks", chunks.Count);

        // Procesamiento en lotes para optimizar recursos
        var batchSize = Math.Min(5, chunks.Count); // Máximo 5 chunks en paralelo
        var allResults = new List<ChunkResult>();

        for (int i = 0; i < chunks.Count; i += batchSize)
        {
            var batch = chunks.Skip(i).Take(batchSize).ToList();
            _logger.LogInformation("📦 Procesando lote {BatchNumber}: chunks {Start}-{End}",
                (i / batchSize) + 1, i + 1, Math.Min(i + batchSize, chunks.Count));

            var tareas = batch.Select(async (chunk, localIndex) =>
            {
                var globalIndex = i + localIndex;
                var chunkTiming = new Dictionary<string, double>();
                var chunkStopwatch = System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    _logger.LogInformation("📄 Procesando chunk {Index}/{Total}", globalIndex + 1, chunks.Count);

                    chunkStopwatch.Restart();
                    var resultado = await _openAIUtils.ProcesarChunkConAsistente(asistente.assistant_id, chunk, promptTexto);
                    chunkTiming["ProcesarChunkConAsistente"] = chunkStopwatch.Elapsed.TotalSeconds;

                    chunkStopwatch.Restart();
                    // Almacenar en extraction_validations
                    await AlmacenarExtraccionParaValidacion(
                        archivoId: 0, // Por ahora 0, ya que no guardamos archivos
                        archivoNombre: archivo.FileName,
                        officeId: officeId,
                        inputText: chunk,
                        openaiResponse: resultado,
                        promptUsed: promptTexto,
                        assistantId: asistente.assistant_id
                    );
                    chunkTiming["AlmacenarExtraccionParaValidacion"] = chunkStopwatch.Elapsed.TotalSeconds;

                    // Extraer las transacciones del resultado
                    chunkStopwatch.Restart();
                    var transacciones = new List<object>();
                    if (resultado is Dictionary<string, object> dict && dict.ContainsKey("resultado"))
                    {
                        var resultadoTexto = dict["resultado"]?.ToString();
                        if (!string.IsNullOrEmpty(resultadoTexto))
                        {
                            transacciones = ExtraerTransaccionesDeRespuesta(resultadoTexto);
                        }
                    }
                    chunkTiming["ExtraerTransaccionesDeRespuesta"] = chunkStopwatch.Elapsed.TotalSeconds;

                    _logger.LogInformation("✅ Chunk {Index} completado en {TotalSeconds}s con {TransactionCount} transacciones",
                        globalIndex + 1, chunkTiming.Values.Sum(), transacciones.Count);

                    return new ChunkResult(
                        Index: globalIndex,
                        Transacciones: transacciones,
                        Timing: chunkTiming,
                        Success: true,
                        Error: null
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error procesando chunk {Index}: {Message}", globalIndex + 1, ex.Message);
                    chunkTiming["Error"] = chunkStopwatch.Elapsed.TotalSeconds;
                    return new ChunkResult(
                        Index: globalIndex,
                        Transacciones: new List<object>(),
                        Timing: chunkTiming,
                        Success: false,
                        Error: ex.Message
                    );
                }
            }).ToArray();

            var batchResults = await Task.WhenAll(tareas);
            allResults.AddRange(batchResults);

            // Pequeña pausa entre lotes para evitar sobrecarga
            if (i + batchSize < chunks.Count)
            {
                await Task.Delay(100);
            }
        }

        // Consolidar resultados de todos los lotes
        foreach (var resultado in allResults.OrderBy(r => r.Index))
        {
            if (!resultado.Success)
            {
                chunkTimings.Add(resultado.Timing);
                return new ProcesarArchivoResult
                (
                    Success: false,
                    Error: $"Error procesando chunk {resultado.Index + 1}: {resultado.Error}",
                    TotalSeconds: -1,
                    Result: null,
                    Timings: timing,
                    ChunkTimings: chunkTimings
                );
            }

            todasLasTransacciones.AddRange(resultado.Transacciones);
            chunkTimings.Add(resultado.Timing);
        }

        _logger.LogInformation("🎯 Procesamiento por lotes completado: {TotalTransactions} elementos aplanados de {ChunkCount} chunks",
            todasLasTransacciones.Count, chunks.Count);

        DateTime time = DateTime.Now;
        var duration = time - time0;

        int TotalSeconds = (int)duration.TotalSeconds;

        timing["Total"] = duration.TotalSeconds;

        return new ProcesarArchivoResult(true, null, TotalSeconds, new
        {
            AssistantId = asistente.assistant_id,
            ChunksProcessed = chunks.Count,
            TotalTransactions = todasLasTransacciones.Count,
            TransaccionesAplanadas = todasLasTransacciones
        }, timing, chunkTimings);
    }

    private List<object> ExtraerTransaccionesDeRespuesta(string resultadoTexto)
    {
        var transacciones = new List<Transaction>();

        try
        {
            // Extraer JSON del texto (puede venir envuelto en ```json)
            var jsonPattern = @"```json\s*(\[.*?\])\s*```";
            var match = Regex.Match(resultadoTexto, jsonPattern, RegexOptions.Singleline);

            string jsonString;
            if (match.Success)
            {
                jsonString = match.Groups[1].Value;
            }
            else
            {
                // Si no hay markdown, asumir que todo el texto es JSON
                jsonString = resultadoTexto.Trim();
            }

            // Deserializar directamente como array de transacciones
            var transaccionesArray = JsonConvert.DeserializeObject<List<Transaction>>(jsonString);
            if (transaccionesArray != null)
            {
                transacciones.AddRange(transaccionesArray);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error extrayendo transacciones de respuesta: {Message}", ex.Message);
            throw new InvalidOperationException("Error al extraer transacciones de la respuesta", ex);

        }

        // Agrupar transacciones por CheckNumber usando TransactionHeader y TransactionDetail
        var transaccionesAgrupadas = AgruparTransaccionesPorCheque(transacciones);

        // Aplanar las transacciones agrupadas y retornar
        return AplanarTransaccionesAgrupadas(transaccionesAgrupadas);
    }

    private List<TransactionHeader> AgruparTransaccionesPorCheque(List<Transaction> transacciones)
    {
        var transaccionesAgrupadas = new List<TransactionHeader>();

        // Agrupar transacciones por CheckNumber
        var gruposPorCheque = transacciones
            .Where(t => !string.IsNullOrEmpty(t.CheckNumber))
            .GroupBy(t => t.CheckNumber)
            .ToList();

        foreach (var grupo in gruposPorCheque)
        {
            var primerTransaccion = grupo.First();

            // Crear el header basado en la primera transacción del grupo
            var header = new TransactionHeader
            {
                CheckNumber = grupo.Key,
                Insurance = primerTransaccion.InsuranceCompany,
                Amount = primerTransaccion.CheckAmount?.ToString(),
                IsActive = true,
                PostedByLsi = true,
                VerifyTotalAmount = true,
                ClaimNumber = grupo.Count(),
                CustomColor = new CustomColor
                {
                    Background = "#FFFFFF",
                    TextColor = "#000000"
                },
                EftAmountDifference = 0,
                CheckAmountDifference = 0,
                TypeAdjustment = "payments",
                IsDayEndedByCashPoster = false,
                PaymentTypeId = 1002,
                PaymentTypeName = primerTransaccion.PaymentType,
                OrderInList = 1,
                Details = new List<TransactionDetail>()
            };

            // Crear los detalles para cada transacción en el grupo
            int subOrder = 1;
            foreach (var transaccion in grupo)
            {
                var detail = new TransactionDetail
                {
                    CheckNumber = transaccion.CheckNumber,
                    Amount = transaccion.PostedAmount ?? 0,
                    PatientName = transaccion.PatientName,
                    CpdpId = 0,
                    FlashReportNote = transaccion.Code,
                    TipologyServiceDate = transaccion.ServiceDate,
                    AuditIsMandatory = false,
                    AuditDone = false,
                    MaxDatePenalty = false,
                    Verify = false,
                    SubOrderInList = subOrder++,
                    ShowRow = true,
                    RemoveDetail = false,
                    LastRow = false
                };

                //Amount es PostedAmount, si no existe o es cero, usar CheckAmount
                if (detail.Amount == 0 && transaccion.CheckAmount.HasValue)
                {
                    detail.Amount = transaccion.CheckAmount.Value;
                }

                header.Details.Add(detail);
            }

            // Marcar el último detalle como última fila
            if (header.Details.Any())
            {
                header.Details.Last().LastRow = true;
            }

            transaccionesAgrupadas.Add(header);
        }
        return transaccionesAgrupadas;
    }

    private List<object> AplanarTransaccionesAgrupadas(List<TransactionHeader> transaccionesAgrupadas)
    {
        var transaccionesAplanadas = new List<object>();

        int orderInList = 0;

        foreach (var grupo in transaccionesAgrupadas)
        {
            // Agregar el encabezado
            transaccionesAplanadas.Add(grupo);

            int subOrder = 0;

            // Agregar todos los detalles del grupo
            if (grupo.Details != null)
            {
                foreach (var detalle in grupo.Details)
                {
                    subOrder++;
                    detalle.SubOrderInList = subOrder;
                    transaccionesAplanadas.Add(detalle);
                    // Marcar el último detalle como última fila
                    if (subOrder == grupo.Details.Count)
                    {
                        detalle.LastRow = true;
                    }
                }
            }
            // Incrementar el orden en lista para el siguiente grupo
            orderInList++;
            grupo.OrderInList = orderInList;
            grupo.ClaimNumber = grupo.Details?.Count ?? 0;
            grupo.TypeAdjustment = "payments";
            grupo.Amount = grupo.Details?.Sum(d => d.Amount).ToString() ?? "0.00";
            grupo.PostedByLsi = true;
        }


        return transaccionesAplanadas;
    }

    private async Task AlmacenarExtraccionParaValidacion(
        int archivoId,
        string archivoNombre,
        int officeId,
        string inputText,
        object openaiResponse,
        string promptUsed,
        string assistantId)
    {
        // Extraer thread_id y run_id del resultado de OpenAI
        string threadId = "";
        string runId = "";
        string responseText = "";

        if (openaiResponse is Dictionary<string, object> dict)
        {
            if (dict.ContainsKey("run_id"))
                runId = dict["run_id"]?.ToString() ?? "";

            if (dict.ContainsKey("resultado"))
                responseText = dict["resultado"]?.ToString() ?? "";

            // El thread_id debería venir en el mensaje_completo si existe
            if (dict.ContainsKey("mensaje_completo"))
            {
                var mensajeCompleto = dict["mensaje_completo"];
                if (mensajeCompleto is Dictionary<string, object> msgDict && msgDict.ContainsKey("thread_id"))
                {
                    threadId = msgDict["thread_id"]?.ToString() ?? "";
                }
            }
        }

        var extractionValidation = new Data.ExtractionValidation
        {
            ArchivoId = archivoId,
            ArchivoNombre = archivoNombre,
            OfficeId = officeId,
            InputText = inputText,
            OpenaiResponse = responseText,
            PromptUsed = promptUsed,
            AssistantId = assistantId,
            ThreadId = threadId,
            RunId = runId,
            Status = "PendingUserValidation"
        };

        _context.ExtractionValidations.Add(extractionValidation);
        await _context.SaveChangesAsync();

        // Verificar fine-tuning en background - NO esperamos
        _ = Task.Run(async () => await VerificarYDispararFineTuning(officeId));
    }

    private async Task VerificarYDispararFineTuning(int officeId)
    {
        try
        {
            // Solo contar registros que han sido validados por humanos
            var totalValidados = await _context.ExtractionValidations
                .CountAsync(ev => ev.OfficeId == officeId &&
                           ev.ValidatedAt != null);

            // Si es múltiplo de 50 Y hay suficientes validados
            if (totalValidados % 50 == 0 && totalValidados > 0)
            {
                await IniciarFineTuningParaOficina(officeId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error en fine-tuning: {ex.Message}");
        }
    }

    private async Task IniciarFineTuningParaOficina(int officeId)
    {
        try
        {
            Console.WriteLine($"🚀 Iniciando fine-tuning para oficina {officeId}");

            // 1. Obtener todos los registros validados de la oficina
            var registrosValidados = await _context.ExtractionValidations
                .Where(ev => ev.OfficeId == officeId && ev.ValidatedAt != null)
                .OrderByDescending(ev => ev.ValidatedAt)
                .ThenByDescending(ev => ev.CreatedAt)
                .ToListAsync();

            // 2. Aplicar límite de 5000 registros (eliminar 1000 menos relevantes si es necesario)
            if (registrosValidados.Count > 5000)
            {
                // Mantener los primeros 4000 (más recientes y/o corregidos)
                var registrosAMantener = registrosValidados
                    .OrderByDescending(r => r.CorrectedJson != null ? 1 : 0) // Priorizar corregidos
                    .ThenByDescending(r => r.ValidatedAt)
                    .Take(4000)
                    .ToList();

                // Eliminar los 1000 menos relevantes
                var idsAEliminar = registrosValidados
                    .Except(registrosAMantener)
                    .Select(r => r.Id)
                    .ToList();

                var registrosAEliminar = _context.ExtractionValidations
                    .Where(ev => idsAEliminar.Contains(ev.Id));

                _context.ExtractionValidations.RemoveRange(registrosAEliminar);
                await _context.SaveChangesAsync();

                registrosValidados = registrosAMantener;
                Console.WriteLine($"🗑️ Eliminados {idsAEliminar.Count} registros menos relevantes para oficina {officeId}");
            }

            // 3. Generar contenido JSONL
            var jsonlContent = GenerarJsonlParaFineTuning(registrosValidados);

            // 4. Subir archivo a OpenAI
            var jsonlBytes = System.Text.Encoding.UTF8.GetBytes(jsonlContent);
            var nombreArchivo = $"fine_tuning_office_{officeId}_{DateTime.UtcNow:yyyyMMddHHmmss}.jsonl";

            var fileId = await _openAIUtils.SubirArchivoParaFineTuning(jsonlBytes, nombreArchivo);
            Console.WriteLine($"📁 Archivo JSONL subido: {fileId}");

            // 5. Crear job de fine-tuning
            var jobId = await _openAIUtils.CrearJobFineTuning(fileId, officeId);
            Console.WriteLine($"⚙️ Job de fine-tuning creado: {jobId}");

            // 6. Guardar en tabla fine_tuning_jobs
            var fineTuningJob = new FineTuningJob
            {
                OfficeId = officeId,
                JobId = jobId,
                FileId = fileId,
                TotalExamples = registrosValidados.Count,
                JsonlSize = jsonlBytes.Length,
                Status = "running"
            };

            _context.FineTuningJobs.Add(fineTuningJob);
            await _context.SaveChangesAsync();

            Console.WriteLine($"✅ Fine-tuning iniciado para oficina {officeId} con {registrosValidados.Count} ejemplos");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error al iniciar fine-tuning para oficina {officeId}: {ex.Message}");
        }
    }

    private string GenerarJsonlParaFineTuning(List<Data.ExtractionValidation> registros)
    {
        var jsonlLines = new List<string>();

        foreach (var registro in registros)
        {
            // Usar corrected_json si existe, sino openai_response
            var finalResponse = !string.IsNullOrEmpty(registro.CorrectedJson)
                ? registro.CorrectedJson
                : registro.OpenaiResponse;

            var jsonlEntry = new
            {
                messages = new[]
                {
                    new { role = "system", content = "You are a medical billing data extraction specialist. Extract transaction data from financial documents and return valid JSON with these exact fields: patient_id, patient_name, insurance_company, check_amount, posted_amount, check_number, service_date, code, other_amount. Use null for missing values. Return array of objects for multiple transactions." },
                    new { role = "user", content = registro.InputText },
                    new { role = "assistant", content = finalResponse }
                }
            };

            jsonlLines.Add(JsonConvert.SerializeObject(jsonlEntry));
        }

        return string.Join("\n", jsonlLines);
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
}