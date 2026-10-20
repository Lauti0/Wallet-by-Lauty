using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CholinanzasApp.Models;

namespace CholinanzasApp.Services;

public enum DateStyle { Auto, DayFirst, MonthFirst, Iso }
public enum DecimalStyle { Auto, CommaDecimal, DotDecimal }
public enum ImportStatus { New, Duplicate, Error }

/// <summary>Identifica un movimiento para detectar si ya existe: mismo día, tipo, monto y descripción.</summary>
public readonly record struct DuplicateKey(DateTime Date, TransactionType Type, decimal Amount, string Description);

/// <summary>Una fila del archivo ya interpretada. Amount es siempre positivo; el signo está en Type.</summary>
public record ImportRow(int Line, DateTime? Date, string Description, decimal Amount, TransactionType Type,
    string? CategoryName, ImportStatus Status, string? Error);

public record ColumnGuess(int Date, int Description, int Amount, int Debit, int Credit, int Category, bool Split);

/// <summary>Qué columna es cada cosa (-1 = ninguna) y cómo leer fechas y números.</summary>
public class ImportOptions
{
    public int DateColumn { get; set; } = -1;
    public int DescriptionColumn { get; set; } = -1;
    public int AmountColumn { get; set; } = -1;
    public int DebitColumn { get; set; } = -1;
    public int CreditColumn { get; set; } = -1;
    public int CategoryColumn { get; set; } = -1;
    public bool SplitColumns { get; set; }
    public bool InvertSign { get; set; }
    public DateStyle DateStyle { get; set; } = DateStyle.Auto;
    public DecimalStyle DecimalStyle { get; set; } = DecimalStyle.Auto;
}

/// <summary>
/// Lee extractos CSV de distintos bancos: detecta separador, codificación, dónde empieza la tabla y
/// el formato de fechas y números, y los convierte en movimientos. No toca la base de datos.
/// </summary>
public static class CsvImportService
{
    public const long MaxFileBytes = 10 * 1024 * 1024;
    public const int MaxRows = 50_000;
    public const int MaxColumns = 50;
    public const int MaxFieldLength = 500;
    public const decimal MaxAmount = 1_000_000_000_000m;

    private static readonly char[] Delimiters = [';', ',', '\t', '|'];

    // ───────────── Leer el archivo ─────────────

    /// <summary>Lee el archivo como texto. Prueba UTF-8 y, si no es válido, usa la codificación clásica de Windows.</summary>
    public static string ReadText(string path, out string encodingName)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxFileBytes)
            throw new InvalidDataException($"El archivo pesa más de {MaxFileBytes / 1_048_576} MB: es demasiado grande para importarlo.");

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encodingName = "UTF-8";
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encodingName = "UTF-16";
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encodingName = "UTF-16";
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            encodingName = "UTF-8";
            return text;
        }
        catch (DecoderFallbackException)
        {
            encodingName = "Windows (Latin-1)";
            return Encoding.Latin1.GetString(bytes);
        }
    }

    /// <summary>El separador más probable: el que parte la mayoría de las líneas en la misma cantidad de columnas.</summary>
    public static char DetectDelimiter(string text)
    {
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Take(30).ToList();
        var best = ';';
        var bestScore = -1;

        foreach (var d in Delimiters)
        {
            var counts = lines.Select(l => CountOutsideQuotes(l, d)).ToList();
            if (counts.Count == 0) continue;
            var modal = counts.GroupBy(c => c).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First();
            if (modal.Key == 0) continue;

            var score = modal.Count() * 100 + modal.Key; // más líneas coherentes primero; después, más columnas
            if (score > bestScore)
            {
                best = d;
                bestScore = score;
            }
        }
        return best;
    }

    private static int CountOutsideQuotes(string line, char delimiter)
    {
        var inQuotes = false;
        var count = 0;
        foreach (var c in line)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (c == delimiter && !inQuotes) count++;
        }
        return count;
    }

    /// <summary>Parte el texto en filas y columnas (comillas, comillas dobles y saltos de línea dentro de comillas).</summary>
    public static List<string[]> Parse(string text, char delimiter)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        void EndField()
        {
            var value = field.ToString();
            row.Add(value.Length > MaxFieldLength ? value[..MaxFieldLength] : value);
            field.Clear();
            if (row.Count > MaxColumns)
                throw new InvalidDataException($"El archivo tiene más de {MaxColumns} columnas: no parece un extracto bancario.");
        }

        void EndRow()
        {
            EndField();
            if (!(row.Count == 1 && row[0].Trim().Length == 0)) // se ignoran las líneas vacías
            {
                rows.Add(row.ToArray());
                if (rows.Count > MaxRows)
                    throw new InvalidDataException($"El archivo tiene más de {MaxRows:N0} filas: dividilo en partes para importarlo.");
            }
            row.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else if (c == '"' && field.Length == 0) inQuotes = true;
            else if (c == delimiter) EndField();
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                EndRow();
            }
            else field.Append(c);
        }

        if (field.Length > 0 || row.Count > 0) EndRow();
        return rows;
    }

    // ───────────── Dónde empieza la tabla y qué es cada columna ─────────────

    /// <summary>
    /// Muchos bancos ponen datos de la cuenta antes de la tabla. La tabla empieza en la primera fila que tiene
    /// la cantidad de columnas más común; y esa fila es un encabezado si no contiene fechas ni montos.
    /// </summary>
    public static (int Skip, bool HasHeader) GuessLayout(IReadOnlyList<string[]> rows)
    {
        var multi = rows.Where(r => r.Length >= 2).ToList();
        if (multi.Count == 0) return (0, false);

        var modal = multi.GroupBy(r => r.Length).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
        var skip = 0;
        while (skip < rows.Count && rows[skip].Length != modal) skip++;
        if (skip >= rows.Count) return (0, false);

        var first = rows[skip];
        var looksLikeData = first.Any(c => TryParseDate(c, DateStyle.Auto, out _) || TryParseAmount(c, DecimalStyle.CommaDecimal, out _));
        return (skip, !looksLikeData);
    }

    /// <summary>Propone qué columna es cada cosa, por el nombre del encabezado o, si no hay, por el contenido.</summary>
    public static ColumnGuess GuessColumns(string[]? header, IReadOnlyList<string[]> dataRows)
    {
        int date = -1, desc = -1, amount = -1, debit = -1, credit = -1, category = -1;
        var width = Math.Max(header?.Length ?? 0, dataRows.Count > 0 ? dataRows.Max(r => r.Length) : 0);

        static bool Has(string h, params string[] words) => words.Any(h.Contains);

        if (header is not null)
        {
            for (var i = 0; i < header.Length; i++)
            {
                var h = Normalize(header[i]);
                if (h.Length == 0) continue;

                if (date < 0 && Has(h, "fecha", "date")) date = i;
                else if (debit < 0 && Has(h, "debito", "egreso", "retiro", "salida", "cargo", "debit")) debit = i;
                else if (credit < 0 && Has(h, "credito", "deposito", "entrada", "abono", "acreditacion", "credit")) credit = i;
                else if (category < 0 && Has(h, "categoria", "rubro", "category")) category = i;
                else if (desc < 0 && Has(h, "descripcion", "concepto", "detalle", "movimiento", "referencia", "glosa", "leyenda", "description")) desc = i;
                else if (amount < 0 && Has(h, "monto", "importe", "amount", "valor")) amount = i;
            }
        }

        var split = debit >= 0 && credit >= 0;
        var sample = dataRows.Take(40).ToList();

        double Share(int col, Func<string, bool> test)
        {
            var cells = sample.Select(r => col < r.Length ? r[col].Trim() : "").Where(c => c.Length > 0).ToList();
            return cells.Count == 0 ? 0 : cells.Count(test) / (double)cells.Count;
        }

        // lo que el encabezado no resolvió se busca por el contenido de las columnas
        if (date < 0)
            date = Enumerable.Range(0, width).Where(c => Share(c, s => TryParseDate(s, DateStyle.Auto, out _)) >= 0.6).DefaultIfEmpty(-1).First();

        if (!split && amount < 0)
            amount = Enumerable.Range(0, width)
                .Where(c => c != date && c != category && Share(c, s => TryParseAmount(s, DecimalStyle.CommaDecimal, out _)) >= 0.6)
                .DefaultIfEmpty(-1).First();

        if (desc < 0 && sample.Count > 0)
        {
            var taken = new HashSet<int> { date, amount, debit, credit, category };
            desc = Enumerable.Range(0, width).Where(c => !taken.Contains(c))
                .OrderByDescending(c => sample.Average(r => c < r.Length ? r[c].Length : 0))
                .DefaultIfEmpty(-1).First();
        }

        return new ColumnGuess(date, desc, amount, debit, credit, category, split);
    }

    // ───────────── Fechas y montos ─────────────

    /// <summary>Si el archivo usa día/mes o mes/día: se mira si alguna fecha tiene un valor mayor a 12 que lo delata.</summary>
    public static DateStyle DetectDateStyle(IEnumerable<string> samples)
    {
        var dayFirstEvidence = false;
        var monthFirstEvidence = false;
        foreach (var raw in samples)
        {
            var m = Regex.Match(raw.Trim().Split(' ', 'T')[0], @"^(\d{1,2})[/\-.](\d{1,2})[/\-.]\d{2,4}$");
            if (!m.Success) continue;
            if (int.Parse(m.Groups[1].Value) > 12) dayFirstEvidence = true;
            if (int.Parse(m.Groups[2].Value) > 12) monthFirstEvidence = true;
        }
        return monthFirstEvidence && !dayFirstEvidence ? DateStyle.MonthFirst : DateStyle.DayFirst; // sin evidencia: día primero
    }

    public static bool TryParseDate(string text, DateStyle style, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var m = Regex.Match(text.Trim().Split(' ', 'T')[0], @"^(\d{1,4})[/\-.](\d{1,2})[/\-.](\d{1,4})$");
        if (!m.Success) return false;

        int year, month, day;
        if (m.Groups[1].Value.Length == 4) // año primero: 2026-09-15
        {
            year = int.Parse(m.Groups[1].Value);
            month = int.Parse(m.Groups[2].Value);
            day = int.Parse(m.Groups[3].Value);
        }
        else
        {
            if (style == DateStyle.Iso) return false;
            year = int.Parse(m.Groups[3].Value);
            if (m.Groups[3].Value.Length <= 2) year += 2000;
            var first = int.Parse(m.Groups[1].Value);
            var second = int.Parse(m.Groups[2].Value);
            (day, month) = style == DateStyle.MonthFirst ? (second, first) : (first, second);
        }

        if (year is < 1990 or > 2100 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
            return false;

        date = new DateTime(year, month, day);
        return true;
    }

    /// <summary>Si los montos usan coma decimal (1.234,56) o punto decimal (1,234.56). Sin evidencia: coma.</summary>
    public static DecimalStyle DetectDecimalStyle(IEnumerable<string> samples)
    {
        var comma = 0;
        var dot = 0;
        foreach (var s in samples.Select(x => x.Trim()))
        {
            if (Regex.IsMatch(s, @"\d,\d{1,2}\)?-?$")) comma++;
            else if (Regex.IsMatch(s, @"\d\.\d{1,2}\)?-?$")) dot++;
        }
        return dot > comma ? DecimalStyle.DotDecimal : DecimalStyle.CommaDecimal;
    }

    /// <summary>Acepta $, US$, espacios, signo adelante o atrás, y negativos entre paréntesis.</summary>
    public static bool TryParseAmount(string text, DecimalStyle style, out decimal value)
    {
        value = 0;
        var s = text.Trim();
        if (s.Length == 0) return false;

        var negative = false;
        if (s.StartsWith('(') && s.EndsWith(')'))
        {
            negative = true;
            s = s[1..^1];
        }

        foreach (var junk in new[] { "US$", "U$S", "USD", "ARS", "$", " ", " " })
            s = s.Replace(junk, "");

        if (s.EndsWith('-'))
        {
            negative = true;
            s = s[..^1];
        }
        if (s.StartsWith('-'))
        {
            negative = true;
            s = s[1..];
        }
        else if (s.StartsWith('+')) s = s[1..];

        if (s.Length == 0 || !s.Any(char.IsDigit)) return false;

        s = style == DecimalStyle.DotDecimal ? s.Replace(",", "") : s.Replace(".", "").Replace(",", ".");
        if (!decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)) return false;

        value = negative ? -parsed : parsed;
        return true;
    }

    // ───────────── Convertir las filas en movimientos ─────────────

    /// <param name="data">Las filas de datos (sin encabezado ni filas salteadas).</param>
    /// <param name="firstLine">Número de línea (desde 1) de la primera fila, para los mensajes de error.</param>
    /// <param name="existing">Movimientos que ya hay en la cuenta; cada uno "absorbe" una fila igual del archivo.</param>
    public static List<ImportRow> Evaluate(IReadOnlyList<string[]> data, ImportOptions o, int firstLine,
        IReadOnlyDictionary<DuplicateKey, int> existing)
    {
        static string Cell(string[] r, int i) => i >= 0 && i < r.Length ? r[i].Trim() : "";

        var dateStyle = o.DateStyle == DateStyle.Auto
            ? DetectDateStyle(data.Take(500).Select(r => Cell(r, o.DateColumn)))
            : o.DateStyle;
        var amountSamples = data.Take(500).SelectMany(r => o.SplitColumns
            ? new[] { Cell(r, o.DebitColumn), Cell(r, o.CreditColumn) }
            : new[] { Cell(r, o.AmountColumn) }).Where(s => s.Length > 0);
        var decimalStyle = o.DecimalStyle == DecimalStyle.Auto ? DetectDecimalStyle(amountSamples) : o.DecimalStyle;

        var remaining = new Dictionary<DuplicateKey, int>(existing);
        var result = new List<ImportRow>(data.Count);

        for (var i = 0; i < data.Count; i++)
        {
            var r = data[i];
            var line = firstLine + i;
            var description = Cell(r, o.DescriptionColumn);
            var category = Cell(r, o.CategoryColumn);

            ImportRow Fail(string error) =>
                new(line, null, description, 0, TransactionType.Gasto, null, ImportStatus.Error, error);

            if (!TryParseDate(Cell(r, o.DateColumn), dateStyle, out var date))
            {
                result.Add(Fail($"Fecha no válida: \"{Cell(r, o.DateColumn)}\""));
                continue;
            }

            decimal signed;
            if (o.SplitColumns)
            {
                var debitText = Cell(r, o.DebitColumn);
                var creditText = Cell(r, o.CreditColumn);
                decimal debit = 0, credit = 0;
                if (debitText.Length > 0 && !TryParseAmount(debitText, decimalStyle, out debit))
                {
                    result.Add(Fail($"Débito no válido: \"{debitText}\""));
                    continue;
                }
                if (creditText.Length > 0 && !TryParseAmount(creditText, decimalStyle, out credit))
                {
                    result.Add(Fail($"Crédito no válido: \"{creditText}\""));
                    continue;
                }
                signed = Math.Abs(credit) - Math.Abs(debit);
            }
            else
            {
                var amountText = Cell(r, o.AmountColumn);
                if (amountText.Length == 0)
                {
                    result.Add(Fail("Sin monto"));
                    continue;
                }
                if (!TryParseAmount(amountText, decimalStyle, out signed))
                {
                    result.Add(Fail($"Monto no válido: \"{amountText}\""));
                    continue;
                }
            }

            if (o.InvertSign) signed = -signed;
            if (signed == 0)
            {
                result.Add(Fail("Monto cero"));
                continue;
            }
            if (Math.Abs(signed) > MaxAmount)
            {
                result.Add(Fail("Monto demasiado grande"));
                continue;
            }

            var type = signed < 0 ? TransactionType.Gasto : TransactionType.Ingreso;
            var amount = Math.Abs(signed);
            if (description.Length == 0) description = "Importado";

            var key = new DuplicateKey(date, type, amount, Normalize(description));
            var status = ImportStatus.New;
            if (remaining.TryGetValue(key, out var left) && left > 0)
            {
                remaining[key] = left - 1;
                status = ImportStatus.Duplicate;
            }

            result.Add(new ImportRow(line, date, description, amount, type, category.Length > 0 ? category : null, status, null));
        }
        return result;
    }

    public static Dictionary<DuplicateKey, int> BuildExistingMap(IEnumerable<FinanceTransaction> existing)
    {
        var map = new Dictionary<DuplicateKey, int>();
        foreach (var t in existing)
        {
            var key = new DuplicateKey(t.Date.Date, t.Type, t.Amount, Normalize(t.Description));
            map[key] = map.GetValueOrDefault(key) + 1;
        }
        return map;
    }

    /// <summary>Minúsculas, sin acentos y con espacios normalizados: para comparar textos.</summary>
    public static string Normalize(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return Regex.Replace(sb.ToString().ToLowerInvariant().Trim(), @"\s+", " ");
    }
}
