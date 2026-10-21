using System.IO;
using System.Windows;
using CholinanzasApp.Data;
using CholinanzasApp.Models;
using CholinanzasApp.Services;
using Microsoft.EntityFrameworkCore;

namespace CholinanzasApp.ViewModels;

/// <summary>
/// Edición de transferencias, recurrentes, presupuestos, metas y cuentas. Todas funcionan igual que la edición de
/// movimientos: "Editar" carga el elemento en el formulario de arriba, y el mismo botón de guardar actualiza
/// en lugar de agregar. Acá vive el estado y las acciones de cargar y cancelar; el guardado está en cada formulario.
/// </summary>
public partial class MainViewModel
{
    private int? _editingTransferId, _editingRecurringId, _editingBudgetId, _editingGoalId, _editingAccountId;
    private DateTime _editingGoalDate;

    /// <summary>Se avisa al empezar a editar para que la ventana suba la página hasta el formulario.</summary>
    public event Action? EditStarted;

    // ───────────── Transferencias ─────────────
    public bool IsEditingTransfer => _editingTransferId is not null;
    public string TransferFormTitle => IsEditingTransfer ? "Editar transferencia" : "Mover plata entre mis cuentas";
    public string SaveTransferText => IsEditingTransfer ? "Guardar cambios" : "Transferir";

    // ───────────── Recurrentes ─────────────
    public bool IsEditingRecurring => _editingRecurringId is not null;
    public string RecurringFormTitle => IsEditingRecurring ? "Editar recurrente" : "Nuevo movimiento que se repite todos los meses";
    public string SaveRecurringText => IsEditingRecurring ? "Guardar cambios" : "＋ Agregar";

    // ───────────── Presupuestos ─────────────
    public bool IsEditingBudget => _editingBudgetId is not null;
    public string BudgetFormTitle => IsEditingBudget ? "Editar presupuesto" : "Poné un límite mensual a una categoría";
    public string SaveBudgetText => IsEditingBudget ? "Guardar cambios" : "Guardar";

    // ───────────── Metas ─────────────
    public bool IsEditingGoal => _editingGoalId is not null;
    public string GoalFormTitle => IsEditingGoal ? "Editar meta" : "Nueva meta";
    public string SaveGoalText => IsEditingGoal ? "Guardar cambios" : "＋ Agregar";

    // ───────────── Cuentas ─────────────
    public bool IsEditingAccount => _editingAccountId is not null;
    public string AccountFormTitle => IsEditingAccount ? "Editar cuenta" : "Nueva cuenta";
    public string SaveAccountText => IsEditingAccount ? "Guardar cambios" : "＋ Agregar";

    private RelayCommand? _editTransfer, _cancelTransfer, _editRecurring, _cancelRecurring, _editBudget, _cancelBudget,
        _editGoal, _cancelGoal, _editAccount, _cancelAccount, _deleteAccount;

    public RelayCommand EditTransferCommand => _editTransfer ??= new(p => EditTransfer(p as TransferRow));
    public RelayCommand CancelTransferEditCommand => _cancelTransfer ??= new(_ => ClearTransferForm());
    public RelayCommand EditRecurringCommand => _editRecurring ??= new(p => EditRecurring(p as RecurringRow));
    public RelayCommand CancelRecurringEditCommand => _cancelRecurring ??= new(_ => ClearRecurringForm());
    public RelayCommand EditBudgetCommand => _editBudget ??= new(p => EditBudget(p as BudgetRow));
    public RelayCommand CancelBudgetEditCommand => _cancelBudget ??= new(_ => ClearBudgetForm());
    public RelayCommand EditGoalCommand => _editGoal ??= new(p => EditGoal(p as GoalRow));
    public RelayCommand CancelGoalEditCommand => _cancelGoal ??= new(_ => ClearGoalForm());
    public RelayCommand EditAccountCommand => _editAccount ??= new(p => EditAccount(p as AccountRow));
    public RelayCommand DeleteAccountCommand => _deleteAccount ??= new(p => DeleteAccount(p as AccountRow));
    public RelayCommand CancelAccountEditCommand => _cancelAccount ??= new(_ => ClearAccountForm());

    private static string Fmt(decimal value) => value.ToString("0.00", Ar);

    // Después de guardar se muestra el mes de la fecha guardada, para que se vea lo que se acaba de guardar.
    private void ShowMonthOf(DateTime date)
    {
        var month = new DateTime(date.Year, date.Month, 1);
        if (month == _month) return;
        _month = month;
        Raise(nameof(MonthTitle));
    }

    // ───────────── Transferencias ─────────────

    private void SetEditingTransfer(int? id)
    {
        _editingTransferId = id;
        Raise(nameof(IsEditingTransfer));
        Raise(nameof(TransferFormTitle));
        Raise(nameof(SaveTransferText));
    }

    private void ClearTransferForm()
    {
        TransferAmountOut = "";
        TransferAmountIn = "";
        TransferDescription = "";
        SetEditingTransfer(null);
        Reload();
    }

    private void EditTransfer(TransferRow? row)
    {
        if (row is null) return;
        Transfer? t;
        using (var db = new AppDbContext())
            t = db.Transfers.AsNoTracking().FirstOrDefault(x => x.Id == row.Id);
        if (t is null)
        {
            Reload();
            return;
        }

        SetEditingTransfer(t.Id);
        TransferFrom = Accounts.FirstOrDefault(a => a.Id == t.FromAccountId);
        TransferTo = Accounts.FirstOrDefault(a => a.Id == t.ToAccountId);
        TransferDate = t.Date;
        TransferAmountOut = Fmt(t.AmountOut);
        // "monto que entra" solo se usa cuando las cuentas son de distinta moneda
        TransferAmountIn = TransferFrom is not null && TransferTo is not null && TransferFrom.Currency != TransferTo.Currency
            ? Fmt(t.AmountIn)
            : "";
        TransferDescription = t.Description;
        EditStarted?.Invoke();
    }

    // ───────────── Recurrentes ─────────────

    private void SetEditingRecurring(int? id)
    {
        _editingRecurringId = id;
        Raise(nameof(IsEditingRecurring));
        Raise(nameof(RecurringFormTitle));
        Raise(nameof(SaveRecurringText));
    }

    private void ClearRecurringForm()
    {
        NewRecDescription = "";
        NewRecAmount = "";
        NewRecDay = "1";
        SetEditingRecurring(null);
        Reload();
    }

    private void EditRecurring(RecurringRow? row)
    {
        if (row is null) return;
        RecurringTransaction? r;
        using (var db = new AppDbContext())
            r = db.Recurrings.AsNoTracking().FirstOrDefault(x => x.Id == row.Id);
        if (r is null)
        {
            Reload();
            return;
        }

        SetEditingRecurring(r.Id);
        NewRecType = r.Type; // al cambiar el tipo se recarga la lista de categorías
        NewRecCategory = RecFilteredCategories.FirstOrDefault(c => c.Id == r.CategoryId);
        NewRecAccount = Accounts.FirstOrDefault(a => a.Id == r.AccountId);
        NewRecDescription = r.Description;
        NewRecAmount = Fmt(r.Amount);
        NewRecDay = r.DayOfMonth.ToString();
        EditStarted?.Invoke();
    }

    // ───────────── Presupuestos ─────────────

    private void SetEditingBudget(int? id)
    {
        _editingBudgetId = id;
        Raise(nameof(IsEditingBudget));
        Raise(nameof(BudgetFormTitle));
        Raise(nameof(SaveBudgetText));
    }

    private void ClearBudgetForm()
    {
        NewBudgetAmount = "";
        SetEditingBudget(null);
        Reload();
    }

    private void EditBudget(BudgetRow? row)
    {
        if (row is null) return;
        Budget? b;
        using (var db = new AppDbContext())
            b = db.Budgets.AsNoTracking().FirstOrDefault(x => x.Id == row.Id);
        if (b is null)
        {
            Reload();
            return;
        }

        SetEditingBudget(b.Id);
        NewBudgetCategory = ExpenseCategories.FirstOrDefault(c => c.Id == b.CategoryId);
        NewBudgetCurrency = b.Currency;
        NewBudgetAmount = Fmt(b.Amount);
        EditStarted?.Invoke();
    }

    // ───────────── Metas ─────────────

    private void SetEditingGoal(int? id, DateTime originalDate = default)
    {
        _editingGoalId = id;
        _editingGoalDate = originalDate;
        Raise(nameof(IsEditingGoal));
        Raise(nameof(GoalFormTitle));
        Raise(nameof(SaveGoalText));
    }

    private void ClearGoalForm()
    {
        NewGoalName = "";
        NewGoalTarget = "";
        NewGoalSaved = "0";
        NewGoalDate = DateTime.Today.AddMonths(6);
        SetEditingGoal(null);
        Reload();
    }

    private void EditGoal(GoalRow? row)
    {
        if (row is null) return;
        SavingsGoal? g;
        using (var db = new AppDbContext())
            g = db.Goals.AsNoTracking().FirstOrDefault(x => x.Id == row.Id);
        if (g is null)
        {
            Reload();
            return;
        }

        SetEditingGoal(g.Id, g.TargetDate);
        NewGoalName = g.Name;
        NewGoalCurrency = g.Currency;
        NewGoalTarget = Fmt(g.TargetAmount);
        NewGoalSaved = Fmt(g.SavedAmount);
        NewGoalDate = g.TargetDate;
        EditStarted?.Invoke();
    }

    // ───────────── Cuentas ─────────────

    private void SetEditingAccount(int? id)
    {
        _editingAccountId = id;
        Raise(nameof(IsEditingAccount));
        Raise(nameof(AccountFormTitle));
        Raise(nameof(SaveAccountText));
    }

    private void ClearAccountForm()
    {
        NewAccountName = "";
        NewAccountInitial = "0";
        SetEditingAccount(null);
        Reload();
    }

    /// <summary>Pregunta por una acción que borra datos; el "No" es el botón por defecto. Reemplazable para probar sin ventanas.</summary>
    public Func<string, bool> ConfirmDangerous { get; set; } = text =>
        MessageBox.Show(text, "Eliminar", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
        == MessageBoxResult.Yes;

    // Cuánto se pierde al borrar una cuenta: se muestra antes de confirmar.
    private record AccountImpact(int Transactions, int Transfers, int Recurrings, int Installments);

    private static AccountImpact CountAccountImpact(AppDbContext db, int id) => new(
        db.Transactions.Count(t => t.AccountId == id),
        db.Transfers.Count(t => t.FromAccountId == id || t.ToAccountId == id),
        db.Recurrings.Count(r => r.AccountId == id),
        db.Installments.Count(p => p.AccountId == id));

    private static string Plural(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

    public void DeleteAccount(AccountRow? row)
    {
        if (row is null) return;

        AccountImpact impact;
        using (var db = new AppDbContext())
        {
            if (!db.Accounts.Any(a => a.Id == row.Id)) { Reload(); return; }
            if (db.Accounts.Count() <= 1)
            {
                InfoNotifier("No podés eliminar tu única cuenta: la app necesita al menos una para registrar movimientos.\n\n" +
                             "Si querés, creá otra primero y después eliminá esta.");
                return;
            }
            impact = CountAccountImpact(db, row.Id);
        }

        var lost = new List<string>();
        if (impact.Transactions > 0) lost.Add(Plural(impact.Transactions, "movimiento", "movimientos"));
        if (impact.Transfers > 0) lost.Add(Plural(impact.Transfers, "transferencia", "transferencias"));
        if (impact.Recurrings > 0) lost.Add(Plural(impact.Recurrings, "movimiento recurrente", "movimientos recurrentes"));
        if (impact.Installments > 0) lost.Add(Plural(impact.Installments, "compra en cuotas", "compras en cuotas"));
        var detail = lost.Count == 0
            ? "La cuenta no tiene movimientos."
            : "También se van a borrar para siempre: " + string.Join(", ", lost) + ". Tus totales históricos y los gráficos cambian.";

        if (!ConfirmDangerous($"¿Eliminar la cuenta \"{row.Name}\" ({row.Currency})?\n\n{detail}\n\n" +
                              "Antes de borrar, la app guarda un respaldo automático por si te arrepentís " +
                              "(lo podés restaurar desde Respaldos)."))
            return;

        // Sin respaldo no se borra: es lo que hace reversible esta acción.
        try
        {
            BackupService.CreateBackup();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or Microsoft.Data.Sqlite.SqliteException)
        {
            InfoNotifier("No se pudo guardar el respaldo previo, así que no se eliminó nada. Revisá que haya espacio en el disco.");
            return;
        }

        using (var db = new AppDbContext())
        using (var tx = db.Database.BeginTransaction())
        {
            // se borran los dependientes a mano: no dependemos de cascadas de la base
            db.Transactions.Where(t => t.AccountId == row.Id).ExecuteDelete();
            db.Transfers.Where(t => t.FromAccountId == row.Id || t.ToAccountId == row.Id).ExecuteDelete();
            db.Recurrings.Where(r => r.AccountId == row.Id).ExecuteDelete();
            db.Installments.Where(p => p.AccountId == row.Id).ExecuteDelete();
            db.Accounts.Where(a => a.Id == row.Id).ExecuteDelete();
            tx.Commit();
        }

        if (_editingAccountId == row.Id) SetEditingAccount(null);
        if (_editingId is not null || _editingTransferId is not null || _editingRecurringId is not null
            || _editingInstallmentId is not null)
        {
            // lo que se estaba editando puede haber desaparecido con la cuenta
            using var db = new AppDbContext();
            if (_editingId is { } eid && !db.Transactions.Any(t => t.Id == eid)) SetEditing(null);
            if (_editingTransferId is { } tid && !db.Transfers.Any(t => t.Id == tid)) SetEditingTransfer(null);
            if (_editingRecurringId is { } rid && !db.Recurrings.Any(r => r.Id == rid)) SetEditingRecurring(null);
            if (_editingInstallmentId is { } iid && !db.Installments.Any(p => p.Id == iid)) SetEditingInstallment(null);
        }
        Reload();
    }

    private void EditAccount(AccountRow? row)
    {
        if (row is null) return;
        Account? a;
        using (var db = new AppDbContext())
            a = db.Accounts.AsNoTracking().FirstOrDefault(x => x.Id == row.Id);
        if (a is null)
        {
            Reload();
            return;
        }

        SetEditingAccount(a.Id);
        NewAccountName = a.Name;
        NewAccountCurrency = a.Currency;
        NewAccountInitial = Fmt(a.InitialBalance);
        EditStarted?.Invoke();
    }
}
