namespace IngestaArchivosAPI.Data;


public sealed record ProcesarArchivoResult(bool Success, string? Error, int TotalSeconds, object? Result, Dictionary<string, double> Timings, List<Dictionary<string, double>> ChunkTimings = null!);
