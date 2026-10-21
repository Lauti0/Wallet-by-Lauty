using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CholinanzasApp.Data;
using CholinanzasApp.Models;
using CholinanzasApp.Services;
using Microsoft.EntityFrameworkCore;

namespace CholinanzasApp.ViewModels;

public record AccountRow(int Id, string Name, Currency Currency, decimal Balance)
{
    public string BalanceText => Money.Format(Balance, Currency);
    public bool IsNegative => Balance < 0;
}

public record MonthSummaryRow(Currency Currency, decimal Income, decimal Expense)
{
    public decimal Net => Income - Expense;
    public bool NetIsNegative => Net < 0;
    public string IncomeText => Money.Format(Income, Currency);
    public string ExpenseText => Money.Format(Expense, Currency);
    public string NetText => Money.Format(Net, Currency);
}

public record CategoryTotalRow(string Category, Currency Currency, decimal Total, double Percent)
{
    public string TotalText => Money.Format(Total, Currency);
    public string PercentText => $"{Percent:0}%";
}

public record TransactionRow(int Id, DateTime Date, string Type, string Category, string Account,
    string Description, decimal SignedAmount, Currency Currency)
{
    public string AmountText => Money.Format(SignedAmount, Currency);
}

public record RecurringRow(int Id, string Description, string Type, string Category, string Account,
    Currency Currency, decimal Amount, int DayOfMonth)
{
    public string AmountText => Money.Format(Amount, Currency);
    public string DayText => $"día {DayOfMonth}";
}

public record PendingRow(int RecurringId, DateTime Date, string Description, TransactionType Type,
    int AccountId, int CategoryId, string Category, string Account, decimal SignedAmount, Currency Currency,
    int? InstallmentPlanId = null, int? InstallmentNumber = null)
{
    public string AmountText => Money.Format(SignedAmount, Currency);
}

public record ProjectionRow(Currency Currency, decimal Balance, decimal PendingIncome, decimal PendingExpense, int DaysLeft)
{
    public decimal Projected => Balance + PendingIncome - PendingExpense;
    public bool ProjectedIsNegative => Projected < 0;
    public string BalanceText => Money.Format(Balance, Currency);
    public string PendingIncomeText => Money.Format(PendingIncome, Currency);
    public string PendingExpenseText => Money.Format(PendingExpense, Currency);
    public string ProjectedText => Money.Format(Projected, Currency);
    public string DailyText => DaysLeft > 0 ? Money.Format(Projected / DaysLeft, Currency) : "—";
}

public static class Money
{
    private static readonly CultureInfo Ar = new("es-AR");

    public static string Format(decimal amount, Currency currency) =>
        $"{(currency == Currency.USD ? "US$" : "$")} {amount.ToString("N2", Ar)}";
}

public partial class MainViewModel : ViewModelBase
{
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    // formulario de movimiento
    private DateTime _newDate = DateTime.Today;
    private string _newAmount = "";
    private TransactionType _newType = TransactionType.Gasto;
    private AccountRow? _newAccount;
    private Category? _newCategory;
    private string _newDescription = "";

    // formulario de cuenta
    private string _newAccountName = "";
    private Currency _newAccountCurrency = Currency.ARS;
    private string _newAccountInitial = "0";

    // formulario de recurrente
    private TransactionType _newRecType = TransactionType.Gasto;
    private string _newRecDescription = "";
    private string _newRecAmount = "";
    private string _newRecDay = "1";
    private AccountRow? _newRecAccount;
    private Category? _newRecCategory;

    private List<Category> _allCategories = [];

    public ObservableCollection<RecurringRow> Recurrings { get; } = [];
    public ObservableCollection<PendingRow> Pending { get; } = [];
    public ObservableCollection<ProjectionRow> Projection { get; } = [];
    public ObservableCollection<Category> RecFilteredCategories { get; } = [];

    public ObservableCollection<AccountRow> Accounts { get; } = [];
    public ObservableCollection<Category> FilteredCategories { get; } = [];
    public ObservableCollection<TransactionRow> Transactions { get; } = [];
    public ObservableCollection<MonthSummaryRow> Summary { get; } = [];
    public ObservableCollection<CategoryTotalRow> CategoryTotals { get; } = [];

    public Array TransactionTypes { get; } = Enum.GetValues<TransactionType>();
    public Array Currencies { get; } = Enum.GetValues<Currency>();

    public string MonthTitle
    {
        get
        {
            var text = _month.ToString("MMMM yyyy", new CultureInfo("es-AR"));
            return char.ToUpper(text[0]) + text[1..];
        }
    }

    public DateTime NewDate { get => _newDate; set => Set(ref _newDate, value); }
    public string NewAmount { get => _newAmount; set => Set(ref _newAmount, value); }
    public string NewDescription
    {
        get => _newDescription;
        set
        {
            Set(ref _newDescription, value);
            SuggestCategory();
        }
    }
    public AccountRow? NewAccount { get => _newAccount; set => Set(ref _newAccount, value); }
    public Category? NewCategory { get => _newCategory; set => Set(ref _newCategory, value); }

    public TransactionType NewType
    {
        get => _newType;
        set
        {
            Set(ref _newType, value);
            RefreshFilteredCategories();
        }
    }

    public string NewAccountName { get => _newAccountName; set => Set(ref _newAccountName, value); }
    public Currency NewAccountCurrency { get => _newAccountCurrency; set => Set(ref _newAccountCurrency, value); }
    public string NewAccountInitial { get => _newAccountInitial; set => Set(ref _newAccountInitial, value); }

    public string NewRecDescription { get => _newRecDescription; set => Set(ref _newRecDescription, value); }
    public string NewRecAmount { get => _newRecAmount; set => Set(ref _newRecAmount, value); }
    public string NewRecDay { get => _newRecDay; set => Set(ref _newRecDay, value); }
    public AccountRow? NewRecAccount { get => _newRecAccount; set => Set(ref _newRecAccount, value); }
    public Category? NewRecCategory { get => _newRecCategory; set => Set(ref _newRecCategory, value); }

    public TransactionType NewRecType
    {
        get => _newRecType;
        set
        {
            Set(ref _newRecType, value);
            RefreshRecFilteredCategories();
        }
    }

    public RelayCommand AddRecurringCommand { get; }
    public RelayCommand DeleteRecurringCommand { get; }
    public RelayCommand ConfirmPendingCommand { get; }
    public RelayCommand SaveTransactionCommand { get; }
    public RelayCommand EditTransactionCommand { get; }
    public RelayCommand CancelEditCommand { get; }
    public RelayCommand DeleteTransactionCommand { get; }

    // Edición: el formulario de movimientos sirve para agregar y para editar (guarda el Id que se edita).
    private int? _editingId;
    public bool IsEditing => _editingId is not null;
    public string TransactionFormTitle => IsEditing ? "Editar movimiento" : "Nuevo movimiento";
    public string SaveTransactionText => IsEditing ? "Guardar cambios" : "＋ Agregar";
    public RelayCommand AddAccountCommand { get; }
    public RelayCommand PreviousMonthCommand { get; }
    public RelayCommand NextMonthCommand { get; }

    public MainViewModel()
    {
        AddRecurringCommand = new RelayCommand(_ => AddRecurring());
        DeleteRecurringCommand = new RelayCommand(p => DeleteRecurring(p as RecurringRow));
        ConfirmPendingCommand = new RelayCommand(p => ConfirmPending(p as PendingRow));
        SaveTransactionCommand = new RelayCommand(_ => SaveTransaction());
        EditTransactionCommand = new RelayCommand(p => EditTransaction(p as TransactionRow));
        CancelEditCommand = new RelayCommand(_ => ClearTransactionForm());
        DeleteTransactionCommand = new RelayCommand(p => DeleteTransaction(p as TransactionRow));
        AddAccountCommand = new RelayCommand(_ => AddAccount());
        PreviousMonthCommand = new RelayCommand(_ => ChangeMonth(-1));
        NextMonthCommand = new RelayCommand(_ => ChangeMonth(1));

        using var db = new AppDbContext();
        _allCategories = db.Categories.AsNoTracking().OrderBy(c => c.Name).ToList();
        RefreshFilteredCategories();
        RefreshRecFilteredCategories();
        NewBudgetCategory = ExpenseCategories.FirstOrDefault();
        Reload();
        RefreshBackups();

        LoadRateSettings();
        StartAutomaticRate();
    }

    private void RefreshFilteredCategories()
    {
        FilteredCategories.Clear();
        foreach (var c in _allCategories.Where(c => c.Type == _newType))
            FilteredCategories.Add(c);
        NewCategory = FilteredCategories.FirstOrDefault();
    }

    private void RefreshRecFilteredCategories()
    {
        RecFilteredCategories.Clear();
        foreach (var c in _allCategories.Where(c => c.Type == _newRecType))
            RecFilteredCategories.Add(c);
        NewRecCategory = RecFilteredCategories.FirstOrDefault();
    }

    private void ChangeMonth(int delta)
    {
        _month = _month.AddMonths(delta);
        Raise(nameof(MonthTitle));
        Reload();
    }

    private void Reload()
    {
        using var db = new AppDbContext();
        var accounts = db.Accounts.AsNoTracking().OrderBy(a => a.Name).ToList();
        var all = db.Transactions.AsNoTracking()
            .Include(t => t.Account).Include(t => t.Category).ToList();

        // saldos por cuenta (decimal se agrega en memoria: SQLite no suma decimales)
        var selectedId = NewAccount?.Id;
        var selectedRecId = NewRecAccount?.Id;
        var selectedInstId = NewInstAccount?.Id;
        var transferFromId = TransferFrom?.Id;
        var transferToId = TransferTo?.Id;
        var transfers = db.Transfers.AsNoTracking()
            .Include(t => t.FromAccount).Include(t => t.ToAccount).ToList();
        Accounts.Clear();
        foreach (var a in accounts)
        {
            var movement = all.Where(t => t.AccountId == a.Id)
                .Sum(t => t.Type == TransactionType.Ingreso ? t.Amount : -t.Amount);
            var transferred = transfers.Where(t => t.ToAccountId == a.Id).Sum(t => t.AmountIn)
                              - transfers.Where(t => t.FromAccountId == a.Id).Sum(t => t.AmountOut);
            Accounts.Add(new AccountRow(a.Id, a.Name, a.Currency, a.InitialBalance + movement + transferred));
        }
        NewAccount = Accounts.FirstOrDefault(a => a.Id == selectedId) ?? Accounts.FirstOrDefault();
        NewRecAccount = Accounts.FirstOrDefault(a => a.Id == selectedRecId) ?? Accounts.FirstOrDefault();
        NewInstAccount = Accounts.FirstOrDefault(a => a.Id == selectedInstId) ?? Accounts.FirstOrDefault();
        NewInstCategory ??= ExpenseCategories.FirstOrDefault();

        ReloadRecurring(db, all);

        // movimientos del mes visible
        var next = _month.AddMonths(1);
        var inMonth = all.Where(t => t.Date >= _month && t.Date < next)
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).ToList();

        Transactions.Clear();
        foreach (var t in inMonth)
            Transactions.Add(new TransactionRow(t.Id, t.Date, t.Type.ToString(), t.Category.Name,
                t.Account.Name, t.Description,
                t.Type == TransactionType.Ingreso ? t.Amount : -t.Amount, t.Account.Currency));

        // resumen por moneda
        Summary.Clear();
        foreach (var g in inMonth.GroupBy(t => t.Account.Currency).OrderBy(g => g.Key))
            Summary.Add(new MonthSummaryRow(g.Key,
                g.Where(t => t.Type == TransactionType.Ingreso).Sum(t => t.Amount),
                g.Where(t => t.Type == TransactionType.Gasto).Sum(t => t.Amount)));

        // en qué se gasta: total por categoría y moneda
        CategoryTotals.Clear();
        foreach (var cur in inMonth.Select(t => t.Account.Currency).Distinct().OrderBy(c => c))
        {
            var expenses = inMonth.Where(t => t.Type == TransactionType.Gasto && t.Account.Currency == cur).ToList();
            var total = expenses.Sum(t => t.Amount);
            foreach (var g in expenses.GroupBy(t => t.Category.Name)
                         .Select(g => (Name: g.Key, Sum: g.Sum(t => t.Amount)))
                         .OrderByDescending(x => x.Sum))
                CategoryTotals.Add(new CategoryTotalRow(g.Name, cur, g.Sum,
                    total == 0 ? 0 : (double)(g.Sum / total * 100)));
        }

        ReloadPlanning(db, inMonth);
        ReloadTransfers(db, transfers, transferFromId, transferToId);
        ReloadOverview(accounts, all, transfers);
        ReloadBudgetAlerts(db, all);
        ReloadRules(db, all);
    }

    // Recurrentes sin registrar desde el mes actual hasta el mes visible, y proyección de saldo.
    private void ReloadRecurring(AppDbContext db, List<FinanceTransaction> all)
    {
        var recs = db.Recurrings.AsNoTracking()
            .Include(r => r.Account).Include(r => r.Category)
            .OrderBy(r => r.DayOfMonth).ToList();

        Recurrings.Clear();
        foreach (var r in recs)
            Recurrings.Add(new RecurringRow(r.Id, r.Description, r.Type.ToString(), r.Category.Name,
                r.Account.Name, r.Account.Currency, r.Amount, r.DayOfMonth));

        var installmentPending = ReloadInstallments(db, all);

        Pending.Clear();
        Projection.Clear();

        var today = DateTime.Today;
        var current = new DateTime(today.Year, today.Month, 1);
        if (_month < current) return; // los meses pasados no se proyectan

        var pending = new List<PendingRow>();
        for (var m = current; m <= _month; m = m.AddMonths(1))
        {
            var next = m.AddMonths(1);
            foreach (var r in recs)
            {
                if (all.Any(t => t.RecurringId == r.Id && t.Date >= m && t.Date < next)) continue;

                var day = Math.Min(r.DayOfMonth, DateTime.DaysInMonth(m.Year, m.Month));
                pending.Add(new PendingRow(r.Id, new DateTime(m.Year, m.Month, day), r.Description, r.Type,
                    r.AccountId, r.CategoryId, r.Category.Name, r.Account.Name,
                    r.Type == TransactionType.Ingreso ? r.Amount : -r.Amount, r.Account.Currency));
            }
        }
        // las cuotas vencidas sin registrar también cuentan: es plata que todavía se debe
        var endOfMonth = _month.AddMonths(1);
        pending.AddRange(installmentPending.Where(p => p.Date < endOfMonth));
        foreach (var p in pending.OrderBy(p => p.Date)) Pending.Add(p);

        // solo el mes en curso tiene "días restantes"
        var daysLeft = _month == current ? DateTime.DaysInMonth(today.Year, today.Month) - today.Day + 1 : 0;

        foreach (var cur in Accounts.Select(a => a.Currency).Distinct().OrderBy(c => c))
        {
            var balance = Accounts.Where(a => a.Currency == cur).Sum(a => a.Balance);
            var income = pending.Where(p => p.Currency == cur && p.SignedAmount > 0).Sum(p => p.SignedAmount);
            var expense = -pending.Where(p => p.Currency == cur && p.SignedAmount < 0).Sum(p => p.SignedAmount);
            Projection.Add(new ProjectionRow(cur, balance, income, expense, daysLeft));
        }
    }

    private void AddRecurring()
    {
        if (string.IsNullOrWhiteSpace(NewRecDescription))
        {
            MessageBox.Show("Poné una descripción (ej: Alquiler, Sueldo, Netflix).", "Recurrente");
            return;
        }
        if (!TryParseAmount(NewRecAmount, out var amount) || amount <= 0)
        {
            MessageBox.Show("Ingresá un monto válido mayor a 0.", "Recurrente");
            return;
        }
        if (!int.TryParse(NewRecDay, out var day) || day is < 1 or > 31)
        {
            MessageBox.Show("El día del mes tiene que estar entre 1 y 31.", "Recurrente");
            return;
        }
        if (NewRecAccount is null || NewRecCategory is null)
        {
            MessageBox.Show("Elegí una cuenta y una categoría.", "Recurrente");
            return;
        }

        using (var db = new AppDbContext())
        {
            if (_editingRecurringId is { } id)
            {
                // los movimientos que ya se registraron desde este recurrente no cambian: solo lo que falta
                var existing = db.Recurrings.Find(id);
                if (existing is null)
                {
                    InfoNotifier("Ese recurrente ya no existe.");
                    ClearRecurringForm();
                    return;
                }
                existing.Description = NewRecDescription.Trim();
                existing.Amount = amount;
                existing.Type = NewRecType;
                existing.DayOfMonth = day;
                existing.AccountId = NewRecAccount.Id;
                existing.CategoryId = NewRecCategory.Id;
            }
            else
            {
                db.Recurrings.Add(new RecurringTransaction
                {
                    Description = NewRecDescription.Trim(),
                    Amount = amount,
                    Type = NewRecType,
                    DayOfMonth = day,
                    AccountId = NewRecAccount.Id,
                    CategoryId = NewRecCategory.Id
                });
            }
            db.SaveChanges();
        }

        ClearRecurringForm();
    }

    private void DeleteRecurring(RecurringRow? row)
    {
        if (row is null) return;
        if (MessageBox.Show($"¿Eliminar el recurrente \"{row.Description}\"? Los movimientos ya registrados se mantienen.",
                "Eliminar", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

        using (var db = new AppDbContext())
        {
            db.Recurrings.Where(r => r.Id == row.Id).ExecuteDelete();
        }
        if (_editingRecurringId == row.Id) SetEditingRecurring(null);
        Reload();
    }

    // Registra la ocurrencia pendiente como movimiento real (y deja de proyectarse).
    private void ConfirmPending(PendingRow? row)
    {
        if (row is null) return;

        using (var db = new AppDbContext())
        {
            db.Transactions.Add(new FinanceTransaction
            {
                Date = row.Date,
                Amount = Math.Abs(row.SignedAmount),
                Type = row.Type,
                Description = row.Description,
                AccountId = row.AccountId,
                CategoryId = row.CategoryId,
                RecurringId = row.InstallmentPlanId is null ? row.RecurringId : null,
                InstallmentPlanId = row.InstallmentPlanId,
                InstallmentNumber = row.InstallmentNumber
            });
            db.SaveChanges();
        }
        Reload();
    }

    // Tope razonable para un monto: evita desbordes al sumar y números imposibles de mostrar.
    private const decimal MaxAmount = 1_000_000_000_000m;

    private static bool TryParseAmount(string text, out decimal value) =>
        (decimal.TryParse(text, NumberStyles.Number, new CultureInfo("es-AR"), out value)
         || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
        && Math.Abs(value) <= MaxAmount;

    // Agrega un movimiento nuevo o, si se está editando uno, actualiza el existente.
    private void SaveTransaction()
    {
        if (!TryParseAmount(NewAmount, out var amount) || amount <= 0)
        {
            MessageBox.Show("Ingresá un monto válido mayor a 0.", "Movimiento");
            return;
        }
        if (NewAccount is null || NewCategory is null)
        {
            MessageBox.Show("Elegí una cuenta y una categoría.", "Movimiento");
            return;
        }

        // lo necesario para detectar una corrección de categoría y ofrecer una regla al terminar
        var description = NewDescription.Trim();
        var type = NewType;
        var chosen = NewCategory;
        var ruleBefore = CategoryRuleService.FindMatch(description, type, _rules);
        int? previousCategoryId = null;

        using (var db = new AppDbContext())
        {
            if (_editingId is { } id)
            {
                var existing = db.Transactions.Find(id);
                if (existing is null)
                {
                    // se borró mientras se editaba
                    MessageBox.Show("Ese movimiento ya no existe.", "Movimiento");
                    ClearTransactionForm();
                    return;
                }
                previousCategoryId = existing.CategoryId;
                existing.Date = NewDate.Date;
                existing.Amount = amount;
                existing.Type = NewType;
                existing.Description = NewDescription.Trim();
                existing.AccountId = NewAccount.Id;
                existing.CategoryId = NewCategory.Id;
            }
            else
            {
                db.Transactions.Add(new FinanceTransaction
                {
                    Date = NewDate.Date,
                    Amount = amount,
                    Type = NewType,
                    Description = NewDescription.Trim(),
                    AccountId = NewAccount.Id,
                    CategoryId = NewCategory.Id
                });
            }
            db.SaveChanges();
        }

        // se muestra el mes donde quedó el movimiento, para que se vea lo que se acaba de guardar
        var savedMonth = new DateTime(NewDate.Year, NewDate.Month, 1);
        if (savedMonth != _month)
        {
            _month = savedMonth;
            Raise(nameof(MonthTitle));
        }

        ClearTransactionForm();

        // Una corrección es: al editar, haber cambiado la categoría; al agregar, haber elegido una distinta
        // de la que sugería una regla. En esos casos se ofrece que la app lo recuerde.
        var corrected = previousCategoryId is { } before
            ? before != chosen.Id
            : ruleBefore is not null && ruleBefore.CategoryId != chosen.Id;
        if (corrected) OfferRule(description, type, chosen, ruleBefore);
    }

    // Carga el movimiento en el formulario para modificarlo.
    private void EditTransaction(TransactionRow? row)
    {
        if (row is null) return;

        FinanceTransaction? t;
        using (var db = new AppDbContext())
            t = db.Transactions.AsNoTracking().FirstOrDefault(x => x.Id == row.Id);
        if (t is null)
        {
            Reload();
            return;
        }

        SetEditing(t.Id);
        NewType = t.Type; // al cambiar el tipo se recarga la lista de categorías
        NewCategory = FilteredCategories.FirstOrDefault(c => c.Id == t.CategoryId);
        NewAccount = Accounts.FirstOrDefault(a => a.Id == t.AccountId);
        NewDate = t.Date;
        NewAmount = t.Amount.ToString("0.00", new CultureInfo("es-AR"));
        NewDescription = t.Description;
    }

    // Vacía el formulario y sale del modo edición.
    private void ClearTransactionForm()
    {
        NewAmount = "";
        NewDescription = "";
        SetEditing(null);
        Reload();
    }

    private void SetEditing(int? id)
    {
        _editingId = id;
        Raise(nameof(IsEditing));
        Raise(nameof(TransactionFormTitle));
        Raise(nameof(SaveTransactionText));
    }

    private void DeleteTransaction(TransactionRow? row)
    {
        if (row is null) return;
        if (MessageBox.Show($"¿Eliminar \"{row.Category} {row.AmountText}\"?", "Eliminar",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

        using (var db = new AppDbContext())
        {
            db.Transactions.Where(t => t.Id == row.Id).ExecuteDelete();
        }

        // si justo se estaba editando ese movimiento, se sale del modo edición
        if (_editingId == row.Id) SetEditing(null);
        Reload();
    }

    private void AddAccount()
    {
        if (string.IsNullOrWhiteSpace(NewAccountName))
        {
            MessageBox.Show("Poné un nombre para la cuenta.", "Cuenta");
            return;
        }
        if (!TryParseAmount(NewAccountInitial, out var initial))
        {
            MessageBox.Show("El saldo inicial no es válido.", "Cuenta");
            return;
        }

        using (var db = new AppDbContext())
        {
            if (_editingAccountId is { } id)
            {
                var existing = db.Accounts.Find(id);
                if (existing is null)
                {
                    InfoNotifier("Esa cuenta ya no existe.");
                    ClearAccountForm();
                    return;
                }

                // cambiar la moneda de una cuenta con movimientos dejaría todos sus montos sin sentido
                var inUse = db.Transactions.Any(t => t.AccountId == id)
                            || db.Transfers.Any(t => t.FromAccountId == id || t.ToAccountId == id)
                            || db.Recurrings.Any(r => r.AccountId == id)
                            || db.Installments.Any(p => p.AccountId == id);
                if (existing.Currency != NewAccountCurrency && inUse)
                {
                    InfoNotifier("No se puede cambiar la moneda de una cuenta que ya tiene movimientos, transferencias, recurrentes o cuotas. " +
                                 "Podés crear una cuenta nueva en la otra moneda.");
                    return;
                }

                existing.Name = NewAccountName.Trim();
                existing.Currency = NewAccountCurrency;
                existing.InitialBalance = initial;
            }
            else
            {
                db.Accounts.Add(new Account
                {
                    Name = NewAccountName.Trim(),
                    Currency = NewAccountCurrency,
                    InitialBalance = initial
                });
            }
            db.SaveChanges();
        }

        ClearAccountForm();
    }
}
