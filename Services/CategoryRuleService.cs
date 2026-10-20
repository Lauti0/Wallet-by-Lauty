using System.Text.RegularExpressions;
using CholinanzasApp.Data;
using CholinanzasApp.Models;
using Microsoft.EntityFrameworkCore;

namespace CholinanzasApp.Services;

public enum RuleSaveResult { Created, Updated, TooShort, LimitReached }

/// <summary>Una regla lista para comparar: el texto ya viene sin acentos ni mayúsculas.</summary>
public record RuleEntry(int Id, string Pattern, string NormalizedPattern, int CategoryId, TransactionType CategoryType);

/// <summary>
/// Reglas de categorías: "si la descripción contiene X, la categoría es Y". Se aplican al importar, al escribir
/// un movimiento a mano y sobre lo ya cargado. No distinguen mayúsculas ni acentos.
/// </summary>
public static class CategoryRuleService
{
    public const int MaxRules = 500;
    public const int MinPatternLength = 2;
    public const int MaxPatternLength = 60;

    // Reglas de ejemplo que se cargan una sola vez; el usuario las puede cambiar o borrar.
    private static readonly (string Category, string[] Patterns)[] Defaults =
    [
        ("Supermercado", ["carrefour", "coto", "jumbo", "changomas", "supermercado", "la anonima"]),
        ("Transporte", ["sube", "uber", "cabify", "ypf", "shell", "axion", "peaje", "estacionamiento"]),
        ("Suscripciones", ["netflix", "spotify", "disney", "hbo", "youtube premium", "amazon prime"]),
        ("Servicios", ["edenor", "edesur", "metrogas", "aysa", "telecentro", "movistar", "personal flow", "fibertel"]),
        ("Salud", ["farmacity", "farmacia", "osde", "swiss medical", "galeno"]),
        ("Salidas", ["mcdonald", "burger king", "starbucks", "rappi", "pedidosya", "cinemark", "hoyts"]),
        ("Impuestos", ["afip", "arba", "agip", "monotributo"]),
        ("Educación", ["udemy", "coursera"]),
        ("Sueldo", ["sueldo", "haberes"]),
        ("Inversiones", ["intereses", "dividendos", "plazo fijo"])
    ];

    // Palabras que aparecen en casi cualquier descripción bancaria: no identifican a un comercio.
    private static readonly HashSet<string> GenericWords =
    [
        "compra", "compras", "pago", "pagos", "debito", "credito", "transferencia", "transf", "tarjeta", "visa", "master",
        "mastercard", "debin", "cuota", "cuotas", "importado", "retiro", "extraccion", "deposito", "servicio", "servicios",
        "saldo", "sucursal", "numero", "para", "desde", "hacia", "movimiento", "operacion", "consumo", "https", "http", "www",
        "argentina", "cuenta", "banco", "pesos", "dolares"
    ];

    /// <summary>
    /// Guarda una regla. Un mismo texto no puede ir a dos categorías: si ya existe (sin importar mayúsculas,
    /// acentos ni espacios), se le cambia la categoría en lugar de duplicarla.
    /// </summary>
    public static RuleSaveResult Save(AppDbContext db, string pattern, int categoryId)
    {
        pattern = pattern.Trim();
        if (pattern.Length > MaxPatternLength) pattern = pattern[..MaxPatternLength];

        var normalized = CsvImportService.Normalize(pattern);
        if (normalized.Length < MinPatternLength) return RuleSaveResult.TooShort;

        var existing = db.CategoryRules.ToList().FirstOrDefault(r => CsvImportService.Normalize(r.Pattern) == normalized);
        if (existing is not null)
        {
            existing.CategoryId = categoryId;
            db.SaveChanges();
            return RuleSaveResult.Updated;
        }

        if (db.CategoryRules.Count() >= MaxRules) return RuleSaveResult.LimitReached;
        db.CategoryRules.Add(new CategoryRule { Pattern = pattern, CategoryId = categoryId });
        db.SaveChanges();
        return RuleSaveResult.Created;
    }

    /// <summary>
    /// Propone el texto de una regla a partir de una descripción: la primera palabra que identifica algo
    /// (sin números ni palabras genéricas como "compra" o "tarjeta"). Devuelve null si no hay ninguna.
    /// Si una regla existente ya manda esa descripción a otra categoría, intenta dos palabras seguidas
    /// para ser más específica y no pisarla.
    /// </summary>
    public static string? SuggestPattern(string description, string? conflictingPattern = null)
    {
        var normalized = CsvImportService.Normalize(description);
        var words = Regex.Matches(normalized, "[a-z]{4,}").Select(m => m.Value).Where(w => !GenericWords.Contains(w)).ToList();
        if (words.Count == 0) return null;

        var pattern = words[0];
        if (conflictingPattern is not null && words.Count >= 2)
        {
            // solo vale si las dos palabras van pegadas en la descripción: si no, la regla no la encontraría
            var pair = $"{words[0]} {words[1]}";
            if (normalized.Contains(pair) && pair.Length > CsvImportService.Normalize(conflictingPattern).Length)
                pattern = pair;
        }
        return pattern.Length > MaxPatternLength ? pattern[..MaxPatternLength] : pattern;
    }

    /// <summary>Carga las reglas ordenadas: primero las de texto más largo, que son las más específicas.</summary>
    public static List<RuleEntry> Load(AppDbContext db) =>
        db.CategoryRules.AsNoTracking().Include(r => r.Category).ToList()
            .Select(r => new RuleEntry(r.Id, r.Pattern, CsvImportService.Normalize(r.Pattern), r.CategoryId, r.Category.Type))
            .Where(e => e.NormalizedPattern.Length >= MinPatternLength)
            .OrderByDescending(e => e.NormalizedPattern.Length).ThenBy(e => e.Id)
            .ToList();

    /// <summary>
    /// La primera regla que coincide. Con type solo cuentan las reglas cuya categoría es de ese tipo
    /// (una regla de gasto no se aplica a un ingreso); sin type, cualquiera.
    /// </summary>
    public static RuleEntry? FindMatch(string description, TransactionType? type, IReadOnlyList<RuleEntry> rules)
    {
        if (string.IsNullOrWhiteSpace(description) || rules.Count == 0) return null;

        var text = CsvImportService.Normalize(description);
        foreach (var rule in rules) // ya vienen de la más específica a la menos
            if ((type is null || rule.CategoryType == type) && text.Contains(rule.NormalizedPattern))
                return rule;
        return null;
    }

    /// <summary>
    /// Carga las reglas de ejemplo la primera vez. Se marca con PRAGMA user_version para que, si el usuario
    /// las borra, no vuelvan a aparecer en el próximo arranque.
    /// </summary>
    public static void SeedDefaultsOnce(AppDbContext db)
    {
        var version = db.Database.SqlQueryRaw<int>("SELECT user_version AS Value FROM pragma_user_version")
            .AsEnumerable().First();
        if (version >= 1) return;

        var categories = db.Categories.AsNoTracking().ToList()
            .GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First().Id);

        foreach (var (category, patterns) in Defaults)
        {
            if (!categories.TryGetValue(category, out var id)) continue;
            foreach (var pattern in patterns)
                db.CategoryRules.Add(new CategoryRule { Pattern = pattern, CategoryId = id });
        }
        db.SaveChanges();
        db.Database.ExecuteSqlRaw("PRAGMA user_version = 1");
    }
}
