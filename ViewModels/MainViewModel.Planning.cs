using System.Collections.ObjectModel;
using System.Windows;
using CholinanzasApp.Data;
using CholinanzasApp.Models;
using Microsoft.EntityFrameworkCore;

namespace CholinanzasApp.ViewModels;

public record BudgetRow(int Id, string Category, Currency Currency, decimal Limit, decimal Spent)
{
    public decimal Remaining => Limit - Spent;
    public double Percent => Limit == 0 ? 0 : (double)(Spent / Limit * 100);
    public string LimitText => Money.Format(Limit, Currency);
    public string SpentText => Money.Format(Spent, Currency);
    public string RemainingText => Money.Format(Remaining, Currency);
    public string PercentText => $"{Percent:0}%";
    public string Status => Spent > Limit ? "Excedido" : Percent >= 80 ? "Cerca del límite" : "OK";
}

public record GoalRow(int Id, string Name, Currency Currency, decimal Target, decimal Saved,
    DateTime TargetDate, int MonthsLeft)
{
    public decimal Remaining => Math.Max(0, Target - Saved);
    public double Percent => Target == 0 ? 0 : Math.Min(100, (double)(Saved / Target * 100));

    // Si la fecha ya pasó, lo que falta se considera necesario de inmediato.
    public decimal MonthlyNeeded => Remaining == 0 ? 0 : MonthsLeft == 0 ? Remaining : Remaining / MonthsLeft;

    public string TargetText => Money.Format(Target, Currency);
    public string SavedText => Money.Format(Saved, Currency);
    public string RemainingText => Money.Format(Remaining, Currency);
    public string MonthlyNeededText => Money.Format(MonthlyNeeded, Currency);
    public string PercentText => $"{Percent:0}%";
    public string Status => Remaining == 0 ? "Cumplida" : MonthsLeft == 0 ? "Vencida" : $"{MonthsLeft} meses";
}

public record GoalPlanRow(Currency Currency, decimal MonthlyNeeded, decimal MonthNet)
{
    public decimal Shortfall => MonthlyNeeded - MonthNet;
    public bool IsShort => Shortfall > 0;
    public string MonthlyNeededText => Money.Format(MonthlyNeeded, Currency);
    public string MonthNetText => Money.Format(MonthNet, Currency);
    public string Status => Shortfall <= 0
        ? "Vas bien"
        : $"Te faltan {Money.Format(Shortfall, Currency)} este mes";
}

public record BudgetAlert(string Text, bool IsExceeded);

public partial class MainViewModel
{
    private const int MaxVisibleAlerts = 3;

    // presupuestos
    private Category? _newBudgetCategory;
    private Currency _newBudgetCurrency = Currency.ARS;
    private string _newBudgetAmount = "";

    // metas
    private string _newGoalName = "";
    private Currency _newGoalCurrency = Currency.ARS;
    private string _newGoalTarget = "";
    private string _newGoalSaved = "0";
    private DateTime _newGoalDate = DateTime.Today.AddMonths(6);
    private GoalRow? _selectedGoal;
    private string _contributionAmount = "";

    private RelayCommand? _addBudgetCommand, _deleteBudgetCommand;
    private RelayCommand? _addGoalCommand, _deleteGoalCommand, _contributeCommand;

    public ObservableCollection<BudgetRow> Budgets { get; } = [];
    public ObservableCollection<GoalRow> Goals { get; } = [];
    public ObservableCollection<GoalPlanRow> GoalPlan { get; } = [];
    public ObservableCollection<BudgetAlert> BudgetAlerts { get; } = [];

    public List<Category> ExpenseCategories =>
        _allCategories.Where(c => c.Type == TransactionType.Gasto).ToList();

    public Category? NewBudgetCategory { get => _newBudgetCategory; set => Set(ref _newBudgetCategory, value); }
    public Currency NewBudgetCurrency { get => _newBudgetCurrency; set => Set(ref _newBudgetCurrency, value); }
    public string NewBudgetAmount { get => _newBudgetAmount; set => Set(ref _newBudgetAmount, value); }

    public string NewGoalName { get => _newGoalName; set => Set(ref _newGoalName, value); }
    public Currency NewGoalCurrency { get => _newGoalCurrency; set => Set(ref _newGoalCurrency, value); }
    public string NewGoalTarget { get => _newGoalTarget; set => Set(ref _newGoalTarget, value); }
    public string NewGoalSaved { get => _newGoalSaved; set => Set(ref _newGoalSaved, value); }
    public DateTime NewGoalDate { get => _newGoalDate; set => Set(ref _newGoalDate, value); }
    public GoalRow? SelectedGoal { get => _selectedGoal; set => Set(ref _selectedGoal, value); }
    public string ContributionAmount { get => _contributionAmount; set => Set(ref _contributionAmount, value); }

    public RelayCommand AddBudgetCommand => _addBudgetCommand ??= new(_ => AddBudget());
    public RelayCommand DeleteBudgetCommand => _deleteBudgetCommand ??= new(p => DeleteBudget(p as BudgetRow));
    public RelayCommand AddGoalCommand => _addGoalCommand ??= new(_ => AddGoal());
    public RelayCommand DeleteGoalCommand => _deleteGoalCommand ??= new(p => DeleteGoal(p as GoalRow));
    public RelayCommand ContributeCommand => _contributeCommand ??= new(_ => Contribute());

    private static int MonthsUntil(DateTime target)
    {
        var days = (target.Date - DateTime.Today).TotalDays;
        return days <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(days / 30.4375));
    }

    // inMonth: movimientos del mes visible (con Account y Category cargados).
    private void ReloadPlanning(AppDbContext db, List<FinanceTransaction> inMonth)
    {
        var selectedGoalId = SelectedGoal?.Id;

        Budgets.Clear();
        foreach (var b in db.Budgets.AsNoTracking().Include(b => b.Category).ToList()
                     .OrderBy(b => b.Category.Name).ThenBy(b => b.Currency))
        {
            var spent = inMonth
                .Where(t => t.Type == TransactionType.Gasto && t.CategoryId == b.CategoryId
                            && t.Account.Currency == b.Currency)
                .Sum(t => t.Amount);
            Budgets.Add(new BudgetRow(b.Id, b.Category.Name, b.Currency, b.Amount, spent));
        }

        Goals.Clear();
        foreach (var g in db.Goals.AsNoTracking().ToList().OrderBy(g => g.TargetDate))
            Goals.Add(new GoalRow(g.Id, g.Name, g.Currency, g.TargetAmount, g.SavedAmount,
                g.TargetDate, MonthsUntil(g.TargetDate)));
        SelectedGoal = Goals.FirstOrDefault(g => g.Id == selectedGoalId);

        // cuánto hay que ahorrar por mes vs. cuánto se ahorró (neto) en el mes visible
        GoalPlan.Clear();
        foreach (var cur in Goals.Select(g => g.Currency).Distinct().OrderBy(c => c))
        {
            var needed = Goals.Where(g => g.Currency == cur).Sum(g => g.MonthlyNeeded);
            var net = Summary.FirstOrDefault(s => s.Currency == cur)?.Net ?? 0;
            GoalPlan.Add(new GoalPlanRow(cur, needed, net));
        }
    }

    // Avisos de presupuesto del mes en curso: no dependen del mes que se esté mirando, porque
    // lo que importa es cuánto va gastado ahora. Se muestran arriba de todas las pestañas.
    private void ReloadBudgetAlerts(AppDbContext db, List<FinanceTransaction> all)
    {
        BudgetAlerts.Clear();

        var current = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var next = current.AddMonths(1);

        var flagged = db.Budgets.AsNoTracking().Include(b => b.Category).ToList()
            .Select(b => new BudgetRow(b.Id, b.Category.Name, b.Currency, b.Amount,
                all.Where(t => t.Type == TransactionType.Gasto && t.CategoryId == b.CategoryId
                               && t.Account.Currency == b.Currency && t.Date >= current && t.Date < next)
                    .Sum(t => t.Amount)))
            .Where(r => r.Status != "OK")
            .OrderByDescending(r => r.Percent)
            .ToList();

        foreach (var r in flagged.Take(MaxVisibleAlerts))
            BudgetAlerts.Add(r.Status == "Excedido"
                ? new BudgetAlert($"Superaste el presupuesto de {r.Category} ({r.Currency}): gastaste {r.SpentText} de {r.LimitText} este mes.", true)
                : new BudgetAlert($"Estás cerca del límite de {r.Category} ({r.Currency}): llevás {r.SpentText} de {r.LimitText} este mes ({r.PercentText}).", false));

        if (flagged.Count > MaxVisibleAlerts)
            BudgetAlerts.Add(new BudgetAlert($"Y {flagged.Count - MaxVisibleAlerts} alertas más en la pestaña Presupuestos.", false));
    }

    private void AddBudget()
    {
        if (NewBudgetCategory is null)
        {
            MessageBox.Show("Elegí una categoría.", "Presupuesto");
            return;
        }
        if (!TryParseAmount(NewBudgetAmount, out var amount) || amount <= 0)
        {
            MessageBox.Show("Ingresá un límite válido mayor a 0.", "Presupuesto");
            return;
        }

        using (var db = new AppDbContext())
        {
            // un solo presupuesto por categoría y moneda: si existe, se actualiza
            var existing = db.Budgets.FirstOrDefault(b =>
                b.CategoryId == NewBudgetCategory.Id && b.Currency == NewBudgetCurrency);
            if (existing is null)
                db.Budgets.Add(new Budget
                {
                    CategoryId = NewBudgetCategory.Id,
                    Currency = NewBudgetCurrency,
                    Amount = amount
                });
            else
                existing.Amount = amount;
            db.SaveChanges();
        }

        NewBudgetAmount = "";
        Reload();
    }

    private void DeleteBudget(BudgetRow? row)
    {
        if (row is null) return;
        if (MessageBox.Show($"¿Eliminar el presupuesto de {row.Category} ({row.Currency})?", "Eliminar",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

        using (var db = new AppDbContext())
        {
            db.Budgets.Where(b => b.Id == row.Id).ExecuteDelete();
        }
        Reload();
    }

    private void AddGoal()
    {
        if (string.IsNullOrWhiteSpace(NewGoalName))
        {
            MessageBox.Show("Poné un nombre para la meta (ej: Vacaciones, Auto).", "Meta");
            return;
        }
        if (!TryParseAmount(NewGoalTarget, out var target) || target <= 0)
        {
            MessageBox.Show("Ingresá un objetivo válido mayor a 0.", "Meta");
            return;
        }
        if (!TryParseAmount(NewGoalSaved, out var saved) || saved < 0)
        {
            MessageBox.Show("Lo ahorrado hasta ahora no es válido.", "Meta");
            return;
        }
        if (NewGoalDate.Date <= DateTime.Today)
        {
            MessageBox.Show("La fecha objetivo tiene que ser futura.", "Meta");
            return;
        }

        using (var db = new AppDbContext())
        {
            db.Goals.Add(new SavingsGoal
            {
                Name = NewGoalName.Trim(),
                Currency = NewGoalCurrency,
                TargetAmount = target,
                SavedAmount = saved,
                TargetDate = NewGoalDate.Date
            });
            db.SaveChanges();
        }

        NewGoalName = "";
        NewGoalTarget = "";
        NewGoalSaved = "0";
        Reload();
    }

    private void DeleteGoal(GoalRow? row)
    {
        if (row is null) return;
        if (MessageBox.Show($"¿Eliminar la meta \"{row.Name}\"?", "Eliminar",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

        using (var db = new AppDbContext())
        {
            db.Goals.Where(g => g.Id == row.Id).ExecuteDelete();
        }
        Reload();
    }

    // Suma un aporte a la meta seleccionada. Un monto negativo sirve para corregir o retirar.
    private void Contribute()
    {
        if (SelectedGoal is null)
        {
            MessageBox.Show("Seleccioná una meta de la tabla.", "Aporte");
            return;
        }
        if (!TryParseAmount(ContributionAmount, out var amount) || amount == 0)
        {
            MessageBox.Show("Ingresá un monto de aporte válido.", "Aporte");
            return;
        }

        var goalId = SelectedGoal.Id;
        using (var db = new AppDbContext())
        {
            var goal = db.Goals.Find(goalId);
            if (goal is null) return;
            goal.SavedAmount = Math.Max(0, goal.SavedAmount + amount);
            db.SaveChanges();
        }

        ContributionAmount = "";
        Reload();
    }
}
