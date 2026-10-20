using CholinanzasApp.Data;
using CholinanzasApp.Models;
using Microsoft.EntityFrameworkCore;

namespace CholinanzasApp.Services;

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
