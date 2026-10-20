using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace CholinanzasApp.Services;

/// <summary>
/// Lee un libro de Excel (.xlsx) y lo convierte en las mismas filas de texto que produce el lector de CSV, para que
/// el resto del importador (detección de columnas, vista previa, duplicados) funcione igual. No toca la base de datos.
/// Un .xlsx es un archivo comprimido que viene de afuera: se limita su tamaño, lo que declara descomprimido, la
/// cantidad de filas y de columnas, y nunca se ejecutan macros ni se siguen vínculos externos.
/// </summary>
public static partial class ExcelImportService
{
    private const long MaxUncompressedBytes = 100L * 1024 * 1024;
    private const int MaxZipEntries = 2_000;

    /// <summary>Nombres de las hojas visibles que tienen datos, en el orden del libro.</summary>
    public static List<string> GetSheetNames(string path)
    {
        using var workbook = Open(path);
        return workbook.Worksheets
            .Where(s => s.Visibility == XLWorksheetVisibility.Visible && s.LastCellUsed() is not null)
            .Select(s => s.Name)
            .ToList();
    }

    /// <summary>Las filas de una hoja como texto. Las fechas salen como año-mes-día, para que no haya dudas de formato.</summary>
    public static List<string[]> ReadRows(string path, string sheetName)
    {
        using var workbook = Open(path);
        if (!workbook.TryGetWorksheet(sheetName, out var sheet))
            throw new InvalidDataException($"No se encontró la hoja «{sheetName}» en el archivo.");

        var lastCell = sheet.LastCellUsed();
        if (lastCell is null) return [];

        if (lastCell.Address.RowNumber > CsvImportService.MaxRows)
            throw new InvalidDataException(
                $"La hoja tiene más de {CsvImportService.MaxRows:N0} filas: dividila en partes para importarla.");
        if (lastCell.Address.ColumnNumber > CsvImportService.MaxColumns)
            throw new InvalidDataException(
                $"La hoja tiene más de {CsvImportService.MaxColumns} columnas: no parece un extracto bancario.");

        // solo las celdas con contenido (una hoja "vacía" puede declarar millones de celdas con formato)
        var cells = sheet.CellsUsed(XLCellsUsedOptions.Contents).ToList();

        // Los números de Excel se escriben con el mismo estilo que los números guardados como texto en esa hoja
        // (85.000,50 o 1,234.50). Si se mezclaran estilos, el importador elegiría uno para todos y leería mal los montos.
        var commaDecimal = cells.Count(c => c.Value.Type == XLDataType.Text && CommaDecimal().IsMatch(c.Value.GetText()))
                           > cells.Count(c => c.Value.Type == XLDataType.Text && DotDecimal().IsMatch(c.Value.GetText()));

        // agrupadas por fila
        var byRow = new SortedDictionary<int, SortedDictionary<int, string>>();
        foreach (var cell in cells)
        {
            var text = CellText(cell.Value, commaDecimal);
            if (text.Length == 0) continue;
            if (!byRow.TryGetValue(cell.Address.RowNumber, out var columns))
                byRow[cell.Address.RowNumber] = columns = [];
            columns[cell.Address.ColumnNumber] = text;
        }

        // cada fila llega hasta su última celda con contenido: así un título de una sola celda no se confunde con la tabla
        var rows = new List<string[]>(byRow.Count);
        foreach (var columns in byRow.Values)
        {
            var row = new string[columns.Keys.Max()];
            Array.Fill(row, "");
            foreach (var (column, text) in columns) row[column - 1] = text;
            rows.Add(row);
        }
        return rows;
    }

    private static XLWorkbook Open(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > CsvImportService.MaxFileBytes)
            throw new InvalidDataException(
                $"El archivo pesa más de {CsvImportService.MaxFileBytes / 1_048_576} MB: es demasiado grande para importarlo.");

        // Un .xlsx es un zip: se mira cuánto declara ocupar descomprimido antes de abrirlo ("zip bomb").
        // El zip y ClosedXML lanzan distintos tipos de error según cómo esté roto el archivo (incluso InvalidDataException,
        // con un mensaje técnico en inglés): al usuario se le dice siempre lo mismo.
        int entries;
        long uncompressed;
        try
        {
            using var stream = File.OpenRead(path);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            entries = zip.Entries.Count;
            uncompressed = zip.Entries.Sum(e => e.Length);
        }
        catch (Exception ex) when (IsFormatProblem(ex))
        {
            throw Unreadable();
        }

        if (entries > MaxZipEntries || uncompressed > MaxUncompressedBytes)
            throw new InvalidDataException("El archivo Excel es demasiado grande para importarlo.");

        try
        {
            return new XLWorkbook(path);
        }
        catch (Exception ex) when (IsFormatProblem(ex))
        {
            throw Unreadable();
        }
    }

    // Todo error de formato del archivo; no incluye los de disco (archivo en uso, sin permiso) ni la falta de memoria.
    private static bool IsFormatProblem(Exception ex) => ex is not (OutOfMemoryException or IOException or UnauthorizedAccessException);

    private static InvalidDataException Unreadable() => new(
        "No se pudo leer el archivo Excel. Puede estar dañado, protegido con contraseña o no ser un .xlsx. " +
        "Probá guardarlo de nuevo como «Libro de Excel (.xlsx)» o como CSV.");

    [GeneratedRegex(@"\d,\d{1,2}$")] private static partial Regex CommaDecimal();
    [GeneratedRegex(@"\d\.\d{1,2}$")] private static partial Regex DotDecimal();

    private static string CellText(XLCellValue value, bool commaDecimal)
    {
        var text = value.Type switch
        {
            XLDataType.Text => value.GetText().Trim(),
            XLDataType.DateTime => value.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            XLDataType.Number => NumberText(value.GetNumber(), commaDecimal),
            XLDataType.Boolean => value.GetBoolean() ? "true" : "false",
            XLDataType.TimeSpan => value.GetTimeSpan().ToString(null, CultureInfo.InvariantCulture),
            _ => "" // vacía o con error (#DIV/0!, etc.)
        };
        return text.Length > CsvImportService.MaxFieldLength ? text[..CsvImportService.MaxFieldLength] : text;
    }

    // Los números salen con 2 decimales (como se guardan los montos) y los enteros, sin decimales; sin separador de miles.
    // Con el decimal siempre presente, el detector de formato del importador no se confunde con "1.234" (¿mil o uno con 234?).
    private static string NumberText(double number, bool commaDecimal)
    {
        if (double.IsNaN(number) || double.IsInfinity(number)) return "";
        if (number == Math.Floor(number) && Math.Abs(number) < 1e15)
            return ((long)number).ToString(CultureInfo.InvariantCulture);

        var text = Math.Round(number, 2).ToString("0.00", CultureInfo.InvariantCulture);
        return commaDecimal ? text.Replace('.', ',') : text;
    }
}
