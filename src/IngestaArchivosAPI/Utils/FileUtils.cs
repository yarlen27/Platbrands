using System.IO;
using System.Text;
using OfficeOpenXml;
using Sentry;

namespace IngestaArchivosAPI.Utils;

public static class FileUtils
{
    static FileUtils()
    {
        // Configurar EPPlus para uso no comercial (o cambiar a comercial si es necesario)
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    /// <summary>
    /// Detecta el tipo de archivo basado en su contenido y extensión
    /// </summary>
    public static FileType DetectFileType(string fileName, byte[] content)
    {
        // Verificar extensión
        string extension = Path.GetExtension(fileName).ToLowerInvariant();

        switch (extension)
        {
            case ".csv":
                return FileType.CSV;
            case ".xlsx":
            case ".xls":
                return FileType.Excel;
            case ".pdf":
                return FileType.PDF;
            case ".txt":
                return FileType.Text;
            default:
                // Verificar magic bytes para detectar tipo real
                return DetectByMagicBytes(content);
        }
    }

    /// <summary>
    /// Detecta el tipo de archivo basado en los primeros bytes (magic bytes)
    /// </summary>
    private static FileType DetectByMagicBytes(byte[] content)
    {
        if (content.Length < 4) return FileType.Unknown;

        // XLSX/XLSX: PK (ZIP header)
        if (content[0] == 0x50 && content[1] == 0x4B)
        {
            return FileType.Excel;
        }

        // PDF: %PDF
        if (content[0] == 0x25 && content[1] == 0x50 && content[2] == 0x44 && content[3] == 0x46)
        {
            return FileType.PDF;
        }

        // CSV/TXT: Verificar si es texto válido
        try
        {
            string text = Encoding.UTF8.GetString(content, 0, Math.Min(content.Length, 1000));
            if (IsValidText(text))
            {
                return FileType.Text;
            }
        }
        catch
        {
            // No es texto válido
        }

        return FileType.Unknown;
    }

    /// <summary>
    /// Verifica si un string es texto válido (no binario)
    /// </summary>
    private static bool IsValidText(string text)
    {
        // Verificar si contiene caracteres de control no válidos
        foreach (char c in text)
        {
            if (c < 32 && c != '\t' && c != '\n' && c != '\r')
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Extrae el contenido de texto de un archivo, manejando diferentes tipos
    /// </summary>
    public static string ExtractTextContent(string fileName, byte[] content)
    {
        FileType fileType = DetectFileType(fileName, content);

        switch (fileType)
        {
            case FileType.CSV:
            case FileType.Text:
                return ExtractTextFile(content);
            case FileType.Excel:
                return ExtractExcelFile(content);
            case FileType.PDF:
                // Los PDFs ya vienen como texto desde el OCR
                return ExtractPdfFile(content);
            default:
                throw new NotSupportedException($"Tipo de archivo no soportado: {fileType}");
        }
    }

    private static string ExtractPdfFile(byte[] content)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Extrae texto de archivos de texto plano (CSV, TXT)
    /// </summary>
    private static string ExtractTextFile(byte[] content)
    {
        // Intentar diferentes encodings
        var encodings = new[] { Encoding.UTF8, Encoding.ASCII, Encoding.Default };

        foreach (var encoding in encodings)
        {
            try
            {
                string text = encoding.GetString(content);
                if (IsValidText(text))
                {
                    return text;
                }
            }
            catch (Exception ex)
            {
                SentrySdk.CaptureException(ex);
                continue;
            }
        }

        throw new InvalidOperationException("No se pudo extraer texto válido del archivo");
    }

    /// <summary>
    /// Detecta el tipo de separador usado en un archivo de texto
    /// </summary>
    public static DelimiterType DetectDelimiter(string textContent)
    {
        if (string.IsNullOrWhiteSpace(textContent))
            return DelimiterType.Unknown;

        // Tomar una muestra del contenido (primeras 10 líneas)
        var lines = textContent.Split('\n').Take(10).ToArray();

        // Contar ocurrencias de cada separador
        int commaCount = 0;
        int semicolonCount = 0;
        int tabCount = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            commaCount += line.Count(c => c == ',');
            semicolonCount += line.Count(c => c == ';');
            tabCount += line.Count(c => c == '\t');
        }

        // Determinar el separador más probable
        if (tabCount > 0 && tabCount >= commaCount && tabCount >= semicolonCount)
        {
            return DelimiterType.Tab;
        }
        else if (semicolonCount > 0 && semicolonCount >= commaCount)
        {
            return DelimiterType.Semicolon;
        }
        else if (commaCount > 0)
        {
            return DelimiterType.Comma;
        }

        return DelimiterType.Unknown;
    }

    /// <summary>
    /// Divide un archivo delimitado en pedazos de 10 líneas, incluyendo siempre el encabezado
    /// </summary>
    public static List<string> SplitDelimitedFile(string textContent, DelimiterType delimiterType)
    {
        if (string.IsNullOrWhiteSpace(textContent) || delimiterType == DelimiterType.Unknown)
            return new List<string> { textContent };

        var lines = textContent.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        if (lines.Length <= 1)
            return new List<string> { textContent };

        var chunks = new List<string>();
        string header = lines[0]; // Primera línea como encabezado
        var dataLines = lines.Skip(1).ToArray(); // Resto de las líneas

        // Dividir en chunks de 10 líneas
        for (int i = 0; i < dataLines.Length; i += 10)
        {
            var chunk = dataLines.Skip(i).Take(10).ToArray();
            var chunkContent = new List<string> { header };
            chunkContent.AddRange(chunk);

            chunks.Add(string.Join("\n", chunkContent));
        }

        return chunks;
    }

    /// <summary>
    /// Extrae texto de archivos Excel (XLSX, XLS)
    /// </summary>
    private static string ExtractExcelFile(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content);
            using var package = new ExcelPackage(stream);

            var result = new StringBuilder();

            // Procesar todas las hojas del Excel
            foreach (var worksheet in package.Workbook.Worksheets)
            {
                if (worksheet.Dimension == null) continue;

                result.AppendLine($"=== HOJA: {worksheet.Name} ===");

                // Obtener el rango de datos
                var startRow = worksheet.Dimension.Start.Row;
                var endRow = worksheet.Dimension.End.Row;
                var startCol = worksheet.Dimension.Start.Column;
                var endCol = worksheet.Dimension.End.Column;

                // Procesar fila por fila
                for (int row = startRow; row <= endRow; row++)
                {
                    var rowData = new List<string>();

                    for (int col = startCol; col <= endCol; col++)
                    {
                        var cell = worksheet.Cells[row, col];
                        string cellValue = "";

                        if (cell.Value != null)
                        {
                            // Convertir el valor de la celda a string
                            cellValue = cell.Value.ToString() ?? "";

                            // Si es fecha, formatear como string
                            if (cell.Value is DateTime dateValue)
                            {
                                cellValue = dateValue.ToString("yyyy-MM-dd");
                            }
                        }

                        rowData.Add(cellValue);
                    }

                    // Unir las celdas de la fila con tabulaciones
                    result.AppendLine(string.Join("\t", rowData));
                }

                result.AppendLine(); // Línea en blanco entre hojas
            }

            return result.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            throw new InvalidOperationException($"Error al extraer contenido de Excel: {ex.Message}", ex);
        }
    }
}

public enum FileType
{
    Unknown,
    CSV,
    Excel,
    PDF,
    Text
}

public enum DelimiterType
{
    Unknown,
    Comma,
    Semicolon,
    Tab
}