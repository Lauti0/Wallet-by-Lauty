using System.Collections.ObjectModel;
using System.Windows;
using CholinanzasApp.Data;
using CholinanzasApp.Models;
using CholinanzasApp.Services;
using Microsoft.EntityFrameworkCore;

namespace CholinanzasApp.ViewModels;

public record RuleRow(int Id, string Pattern, string Category, string Type);
public record CategoryChoice(int Id, string Label);

public partial class MainViewModel
{
    private const int ApplyBatchSize = 500;

    private List<RuleEntry> _rules = [];
    private List<(int TransactionId, int CategoryId)> _applicable = [];
    private List<Category>? _ruleCategoriesSource;
    private string _newRulePattern = "", _testDescription = "", _rulesApplyInfo = "";
    private CategoryChoice? _newRuleCategory;
    private RelayCommand? _addRuleCommand, _deleteRuleCommand, _applyRulesCommand;

    public ObservableCollection<RuleRow> RuleRows { get; } = [];
    public List<CategoryChoice> RuleCategories { get; private set; } = [];

    public string NewRulePattern { get => _newRulePattern; set => Set(ref _newRulePattern, value); }
    public CategoryChoice? NewRuleCategory { get => _newRuleCategory; set { if (value is not null) Set(ref _newRuleCategory, value); } }

    public string TestDescription
    {
        get => _testDescription;
        set
        {
            Set(ref _testDescription, value);
            Raise(nameof(TestResult));
        }
    }

    // Qué categoría le tocaría a lo que se escribe en el campo de prueba.
    public string TestResult
    {
        get
        {
            if (string.IsNullOrWhiteSpace(TestDescription)) return "";
            var rule = CategoryRuleService.FindMatch(TestDescription, null, _rules);
            if (rule is null) return "Ninguna regla coincide: quedaría en la categoría por defecto.";
            var name = _allCategories.FirstOrDefault(c => c.Id == rule.CategoryId)?.Name ?? "?";
            return $"→ {name}   (por la regla «{rule.Pattern}»)";
        }
    }

    public string RulesApplyInfo { get => _rulesApplyInfo; private set => Set(ref _rulesApplyInfo, value); }
    public bool CanApplyRules => _applicable.Count > 0;

    public RelayCommand AddRuleCommand => _addRuleCommand ??= new(_ => AddRule());
    public RelayCommand DeleteRuleCommand => _deleteRuleCommand ??= new(p => DeleteRule(p as RuleRow));
    public RelayCommand ApplyRulesCommand => _applyRulesCommand ??= new(_ => ApplyRules());

    // Mientras se escribe la descripción de un movimiento nuevo, se sugiere la categoría que dicen las reglas.
    // No se hace al editar uno existente: ahí la categoría ya fue elegida.
    private void SuggestCategory()
    {
        if (IsEditing) return;
        var rule = CategoryRuleService.FindMatch(NewDescription, NewType, _rules);
        if (rule is null) return;

        var category = FilteredCategories.FirstOrDefault(c => c.Id == rule.CategoryId);
        if (category is not null) NewCategory = category;
    }

    private void ReloadRules(AppDbContext db, List<FinanceTransaction> all)
    {
        _rules = CategoryRuleService.Load(db);

        // la lista de categorías solo se rearma si cambió: así no se pierde la selección del desplegable
        if (!ReferenceEquals(_ruleCategoriesSource, _allCategories))
        {
            var selected = NewRuleCategory?.Id;
            _ruleCategoriesSource = _allCategories;
            RuleCategories = _allCategories.OrderBy(c => c.Type).ThenBy(c => c.Name)
                .Select(c => new CategoryChoice(c.Id, $"{c.Name}  ·  {c.Type.ToString().ToLowerInvariant()}")).ToList();
            Raise(nameof(RuleCategories));
            NewRuleCategory = RuleCategories.FirstOrDefault(c => c.Id == selected) ?? RuleCategories.FirstOrDefault();
        }

        var names = _allCategories.ToDictionary(c => c.Id);
        RuleRows.Clear();
        foreach (var r in _rules.Where(r => names.ContainsKey(r.CategoryId))
                     .OrderBy(r => r.CategoryType).ThenBy(r => names[r.CategoryId].Name).ThenBy(r => r.Pattern))
            RuleRows.Add(new RuleRow(r.Id, r.Pattern, names[r.CategoryId].Name, r.CategoryType.ToString()));

        // movimientos que quedaron en "Otros" y que alguna regla sabe categorizar
        var others = new HashSet<string> { "Otros gastos", "Otros ingresos" };
        _applicable = all.Where(t => others.Contains(t.Category.Name))
            .Select(t => (Transaction: t, Rule: CategoryRuleService.FindMatch(t.Description, t.Type, _rules)))
            .Where(x => x.Rule is not null && x.Rule.CategoryId != x.Transaction.CategoryId)
            .Select(x => (x.Transaction.Id, x.Rule!.CategoryId))
            .ToList();

        RulesApplyInfo = _applicable.Count == 0
            ? "No hay movimientos en «Otros gastos» u «Otros ingresos» que tus reglas puedan recategorizar."
            : $"Hay {_applicable.Count:N0} movimientos en «Otros gastos» u «Otros ingresos» que tus reglas pueden recategorizar.";
        Raise(nameof(CanApplyRules));
        Raise(nameof(TestResult));
    }

    private void AddRule()
    {
        var pattern = NewRulePattern.Trim();
        if (pattern.Length < CategoryRuleService.MinPatternLength)
        {
            MessageBox.Show($"Escribí al menos {CategoryRuleService.MinPatternLength} letras del texto a buscar.", "Regla");
            return;
        }
        if (NewRuleCategory is null)
        {
            MessageBox.Show("Elegí la categoría que le corresponde.", "Regla");
            return;
        }

        using (var db = new AppDbContext())
        {
            if (db.CategoryRules.Count() >= CategoryRuleService.MaxRules)
            {
                MessageBox.Show($"Llegaste al máximo de {CategoryRuleService.MaxRules} reglas. Borrá alguna para agregar otra.", "Regla");
                return;
            }

            // un mismo texto no puede ir a dos categorías: si ya existe, se actualiza
            var normalized = CsvImportService.Normalize(pattern);
            var existing = db.CategoryRules.ToList().FirstOrDefault(r => CsvImportService.Normalize(r.Pattern) == normalized);
            if (existing is null)
                db.CategoryRules.Add(new CategoryRule { Pattern = pattern, CategoryId = NewRuleCategory.Id });
            else
                existing.CategoryId = NewRuleCategory.Id;
            db.SaveChanges();
        }

        NewRulePattern = "";
        Reload();
    }

    private void DeleteRule(RuleRow? row)
    {
        if (row is null) return;
        if (MessageBox.Show($"¿Borrar la regla \"{row.Pattern}\" → {row.Category}?", "Eliminar",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

        using (var db = new AppDbContext())
            db.CategoryRules.Where(r => r.Id == row.Id).ExecuteDelete();
        Reload();
    }

    // Recategoriza lo que quedó en "Otros". Cambia muchos movimientos juntos, así que primero se hace un respaldo.
    private void ApplyRules()
    {
        if (_applicable.Count == 0) return;

        var answer = MessageBox.Show(
            $"Se van a cambiar de categoría {_applicable.Count:N0} movimientos que hoy están en «Otros».\n\n" +
            "Antes se hace un respaldo automático de tus datos, por si querés volver atrás.\n\n¿Continuar?",
            "Aplicar reglas", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        var changed = RunApplyRules();
        if (changed > 0)
            MessageBox.Show($"Listo, se recategorizaron {changed:N0} movimientos.", "Aplicar reglas",
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // Hace el cambio (con respaldo previo) y devuelve cuántos movimientos cambió; 0 si no se pudo.
    private int RunApplyRules()
    {
        if (_applicable.Count == 0) return 0;

        try
        {
            BackupService.CreateBackup();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo hacer el respaldo previo, así que no se cambió nada.\n\n{ex.Message}", "Aplicar reglas",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return 0;
        }

        var changed = _applicable.Count;
        using (var db = new AppDbContext())
        {
            var target = _applicable.ToDictionary(a => a.TransactionId, a => a.CategoryId);
            foreach (var batch in target.Keys.Chunk(ApplyBatchSize))
                foreach (var t in db.Transactions.Where(t => batch.Contains(t.Id)).ToList())
                    t.CategoryId = target[t.Id];
            db.SaveChanges(); // todo o nada
        }

        Reload();
        return changed;
    }
}
