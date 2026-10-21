using System.Collections.ObjectModel;
using CholinanzasApp.Data;
using CholinanzasApp.Models;
using CholinanzasApp.Services;
using Microsoft.EntityFrameworkCore;

namespace CholinanzasApp.ViewModels;

public record InstallmentRow(int Id, string Description, string Category, string Account, Currency Currency,
    decimal Amount, int Count, int Paid, DateTime? NextDate, decimal Remaining)
{
    public string AmountText => Money.Format(Amount, Currency);
    public string TotalText => Money.Format(Amount * Count, Currency);
    public string RemainingText => Money.Format(Remaining, Currency);
    public string ProgressText => Paid >= Count ? $"{Count} de {Count} · pagado" : $"{Paid} de {Count} pagadas";
    public string NextText => NextDate is { } d ? d.ToString("dd/MM/yyyy") : "—";
}

/// <summary>Compras en cuotas con tarjeta: cada cuota pendiente aparece en «Pendientes» (y en la proyección) hasta registrarla.</summary>
public partial class MainViewModel
{
    private string _newInstDescription = "", _newInstAmount = "", _newInstCount = "12", _newInstPaidBefore = "0";
    private DateTime _newInstFirstDate = DateTime.Today;
    private AccountRow? _newInstAccount;
    private Category? _newInstCategory;
    private int? _editingInstallmentId;

    public ObservableCollection<InstallmentRow> Installments { get; } = [];

    public string NewInstDescription { get => _newInstDescription; set => Set(ref _newInstDescription, value); }
    public string NewInstAmount { get => _newInstAmount; set { Set(ref _newInstAmount, value); Raise(nameof(NewInstTotalHint)); } }
    public string NewInstCount { get => _newInstCount; set { Set(ref _newInstCount, value); Raise(nameof(NewInstTotalHint)); } }
    public string NewInstPaidBefore { get => _newInstPaidBefore; set => Set(ref _newInstPaidBefore, value); }
    public DateTime NewInstFirstDate { get => _newInstFirstDate; set => Set(ref _newInstFirstDate, value); }
    public AccountRow? NewInstAccount { get => _newInstAccount; set { Set(ref _newInstAccount, value); Raise(nameof(NewInstTotalHint)); } }
    public Category? NewInstCategory { get => _newInstCategory; set => Set(ref _newInstCategory, value); }

    /// <summary>"Total de la compra: $ 1.200.000,00", calculado mientras se escribe.</summary>
    public string NewInstTotalHint =>
        TryParseAmount(NewInstAmount, out var amount) && amount > 0 && int.TryParse(NewInstCount, out var count) && count > 0
            ? $"Total de la compra: {Money.Format(amount * count, NewInstAccount?.Currency ?? Currency.ARS)}"
            : "";

    public bool IsEditingInstallment => _editingInstallmentId is not null;
    public string InstallmentFormTitle => IsEditingInstallment ? "Editar compra en cuotas" : "Nueva compra en cuotas";
    public string SaveInstallmentText => IsEditingInstallment ? "Guardar cambios" : "＋ Agregar";

    private RelayCommand? _addInstallment, _editInstallment, _cancelInstallment, _deleteInstallment;
    public RelayCommand AddInstallmentCommand => _addInstallment ??= new(_ => SaveInstallment());
    public RelayCommand EditInstallmentCommand => _editInstallment ??= new(p => EditInstallment(p as InstallmentRow));
    public RelayCommand CancelInstallmentEditCommand => _cancelInstallment ??= new(_ => ClearInstallmentForm());
    public RelayCommand DeleteInstallmentCommand => _deleteInstallment ??= new(p => DeleteInstallment(p as InstallmentRow));

    // Carga los planes y devuelve sus cuotas sin registrar (vencidas o futuras), para sumarlas a los pendientes.
    private List<PendingRow> ReloadInstallments(AppDbContext db, List<FinanceTransaction> all)
    {
        var plans = db.Installments.AsNoTracking().Include(p => p.Account).Include(p => p.Category)
            .OrderBy(p => p.Description).ToList();

        Installments.Clear();
        var pending = new List<PendingRow>();
        foreach (var plan in plans)
        {
            var registered = all.Where(t => t.InstallmentPlanId == plan.Id && t.InstallmentNumber is not null)
                .Select(t => t.InstallmentNumber!.Value).ToList();
            var open = InstallmentService.Unregistered(plan, registered).ToList();

            Installments.Add(new InstallmentRow(plan.Id, plan.Description, plan.Category.Name, plan.Account.Name,
                plan.Account.Currency, plan.InstallmentAmount, plan.Count, InstallmentService.Paid(plan, registered),
                open.Count > 0 ? InstallmentService.DateOf(plan, open[0]) : null,
                InstallmentService.Remaining(plan, registered)));

            foreach (var n in open)
                pending.Add(new PendingRow(0, InstallmentService.DateOf(plan, n),
                    $"{plan.Description} (cuota {n}/{plan.Count})", TransactionType.Gasto,
                    plan.AccountId, plan.CategoryId, plan.Category.Name, plan.Account.Name,
                    -plan.InstallmentAmount, plan.Account.Currency, plan.Id, n));
        }
        return pending;
    }

    private void SaveInstallment()
    {
        if (string.IsNullOrWhiteSpace(NewInstDescription))
        {
            InfoNotifier("Poné qué compraste (ej: Notebook, Heladera).");
            return;
        }
        if (!TryParseAmount(NewInstAmount, out var amount) || amount <= 0)
        {
            InfoNotifier("Ingresá el monto de cada cuota (mayor a 0).");
            return;
        }
        if (!int.TryParse(NewInstCount, out var count) || count is < 1 or > InstallmentService.MaxCount)
        {
            InfoNotifier($"La cantidad de cuotas tiene que estar entre 1 y {InstallmentService.MaxCount}.");
            return;
        }
        if (!int.TryParse(NewInstPaidBefore, out var paidBefore) || paidBefore < 0 || paidBefore >= count)
        {
            InfoNotifier("Las cuotas ya pagadas tienen que ser 0 o más, y menos que el total de cuotas.");
            return;
        }
        if (NewInstAccount is null || NewInstCategory is null)
        {
            InfoNotifier("Elegí una cuenta (la tarjeta) y una categoría.");
            return;
        }

        using (var db = new AppDbContext())
        {
            if (_editingInstallmentId is { } id)
            {
                var existing = db.Installments.Find(id);
                if (existing is null)
                {
                    InfoNotifier("Esa compra en cuotas ya no existe.");
                    ClearInstallmentForm();
                    return;
                }
                // las cuotas ya registradas como movimientos no cambian; se modifica solo lo que falta
                existing.Description = NewInstDescription.Trim();
                existing.InstallmentAmount = amount;
                existing.Count = count;
                existing.FirstDate = NewInstFirstDate.Date;
                existing.PaidBefore = paidBefore;
                existing.AccountId = NewInstAccount.Id;
                existing.CategoryId = NewInstCategory.Id;
            }
            else
            {
                db.Installments.Add(new InstallmentPlan
                {
                    Description = NewInstDescription.Trim(),
                    InstallmentAmount = amount,
                    Count = count,
                    FirstDate = NewInstFirstDate.Date,
                    PaidBefore = paidBefore,
                    AccountId = NewInstAccount.Id,
                    CategoryId = NewInstCategory.Id
                });
            }
            db.SaveChanges();
        }

        ClearInstallmentForm();
    }

    private void ClearInstallmentForm()
    {
        NewInstDescription = "";
        NewInstAmount = "";
        NewInstCount = "12";
        NewInstPaidBefore = "0";
        NewInstFirstDate = DateTime.Today;
        SetEditingInstallment(null);
        Reload();
    }

    private void SetEditingInstallment(int? id)
    {
        _editingInstallmentId = id;
        Raise(nameof(IsEditingInstallment));
        Raise(nameof(InstallmentFormTitle));
        Raise(nameof(SaveInstallmentText));
    }

    private void EditInstallment(InstallmentRow? row)
    {
        if (row is null) return;
        InstallmentPlan? p;
        using (var db = new AppDbContext())
            p = db.Installments.AsNoTracking().FirstOrDefault(x => x.Id == row.Id);
        if (p is null)
        {
            Reload();
            return;
        }

        SetEditingInstallment(p.Id);
        NewInstAccount = Accounts.FirstOrDefault(a => a.Id == p.AccountId);
        NewInstCategory = ExpenseCategories.FirstOrDefault(c => c.Id == p.CategoryId);
        NewInstDescription = p.Description;
        NewInstAmount = Fmt(p.InstallmentAmount);
        NewInstCount = p.Count.ToString();
        NewInstPaidBefore = p.PaidBefore.ToString();
        NewInstFirstDate = p.FirstDate;
        EditStarted?.Invoke();
    }

    private void DeleteInstallment(InstallmentRow? row)
    {
        if (row is null) return;
        if (!ConfirmDangerous($"¿Eliminar la compra en cuotas \"{row.Description}\"?\n\n" +
                              "Las cuotas que ya registraste como movimientos se mantienen. " +
                              "Se dejan de proyectar las que faltaban."))
            return;

        using (var db = new AppDbContext())
            db.Installments.Where(p => p.Id == row.Id).ExecuteDelete();
        if (_editingInstallmentId == row.Id) SetEditingInstallment(null);
        Reload();
    }
}
