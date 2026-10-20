using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CholinanzasApp.Models;

namespace CholinanzasApp.ViewModels;

/// <summary>Totales de toda la historia (hasta el mes en curso) y promedio mensual de una moneda.</summary>
public record OverviewRow(Currency Currency, decimal Income, decimal Expense, decimal AvgIncome,
    decimal AvgExpense, int Months)
{
    public decimal Net => Income - Expense;
    public decimal AvgNet => AvgIncome - AvgExpense;
    public bool NetIsNegative => Net < 0;
    public bool AvgNetIsNegative => AvgNet < 0;

    public string IncomeText => Money.Format(Income, Currency);
    public string ExpenseText => Money.Format(Expense, Currency);
    public string NetText => Money.Format(Net, Currency);
    public string AvgIncomeText => Money.Format(AvgIncome, Currency);
    public string AvgExpenseText => Money.Format(AvgExpense, Currency);
    public string AvgNetText => Money.Format(AvgNet, Currency);
    public string AvgTitle => Months == 1 ? "Promedio mensual (último mes)" : $"Promedio mensual (últimos {Months} meses)";
}

public record MonthlyRow(DateTime Month, Currency Currency, decimal Income, decimal Expense)
{
    public decimal Net => Income - Expense;
    public bool NetIsNegative => Net < 0;

    public string MonthText
    {
        get
        {
            var text = Month.ToString("MMMM yyyy", new CultureInfo("es-AR"));
            return char.ToUpper(text[0]) + text[1..];
        }
    }

    public string IncomeText => Money.Format(Income, Currency);
    public string ExpenseText => Money.Format(Expense, Currency);
    public string NetText => Money.Format(Net, Currency);

    // Qué parte de lo que entró quedó ahorrada ("—" si no hubo ingresos ese mes).
    public string SavingsText => Income == 0 ? "—" : $"{Net / Income * 100:0}%";
}

// Gráfico de barras: alturas ya escaladas a píxeles.
public record BarMonth(string Label, double IncomeHeight, double ExpenseHeight, string Tooltip);
public record BarChart(Currency Currency, List<BarMonth> Months, string Summary); // Summary: texto alternativo

// Gráfico de línea: coordenadas ya escaladas al lienzo de LineWidth x LineHeight.
public record ChartDot(double X, double Y);
public record LinePoint(string Label, string ValueText, bool IsNegative);
public record LineChart(Currency Currency, PointCollection Line, List<ChartDot> Dots, List<LinePoint> Months,
    string Summary) // Summary: texto alternativo
{
    public bool HasLine => Line.Count >= 2;
}

public partial class MainViewModel
{
    private const int OverviewMonths = 6;
    private const double BarAreaHeight = 165;
    private const double LineWidth = 440;
    private const double LineHeight = 150;

    private static readonly CultureInfo Ar = new("es-AR");

    public ObservableCollection<OverviewRow> OverviewTotals { get; } = [];
    public ObservableCollection<MonthlyRow> OverviewHistory { get; } = [];
    public ObservableCollection<CategoryTotalRow> OverviewCategories { get; } = [];
    public ObservableCollection<BarChart> OverviewBars { get; } = [];
    public ObservableCollection<LineChart> OverviewLines { get; } = [];

    // all: todos los movimientos (con Account y Category cargados). No depende del mes visible.
    private void ReloadOverview(List<Account> accounts, List<FinanceTransaction> all, List<Transfer> transfers)
    {
        OverviewTotals.Clear();
        OverviewHistory.Clear();
        OverviewCategories.Clear();
        OverviewBars.Clear();
        OverviewLines.Clear();

        // la historia llega hasta el fin del mes en curso; los movimientos de meses futuros no cuentan
        var current = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var history = all.Where(t => t.Date < current.AddMonths(1)).ToList();
        if (history.Count == 0) return;

        var first = history.Min(t => new DateTime(t.Date.Year, t.Date.Month, 1));
        var months = Math.Min(OverviewMonths, (current.Year - first.Year) * 12 + current.Month - first.Month + 1);
        var windowStart = current.AddMonths(-(months - 1));

        foreach (var cur in history.Select(t => t.Account.Currency).Distinct().OrderBy(c => c))
        {
            var tx = history.Where(t => t.Account.Currency == cur).ToList();
            var window = tx.Where(t => t.Date >= windowStart).ToList();

            OverviewTotals.Add(new OverviewRow(cur,
                SumOf(tx, TransactionType.Ingreso), SumOf(tx, TransactionType.Gasto),
                SumOf(window, TransactionType.Ingreso) / months, SumOf(window, TransactionType.Gasto) / months,
                months));

            // mes a mes, del más viejo al más nuevo
            var rows = new List<(DateTime Month, decimal Income, decimal Expense)>();
            for (var i = months - 1; i >= 0; i--)
            {
                var month = current.AddMonths(-i);
                var inMonth = tx.Where(t => t.Date >= month && t.Date < month.AddMonths(1)).ToList();
                rows.Add((month, SumOf(inMonth, TransactionType.Ingreso), SumOf(inMonth, TransactionType.Gasto)));
            }

            // la tabla lista del más reciente al más viejo
            foreach (var r in Enumerable.Reverse(rows))
                OverviewHistory.Add(new MonthlyRow(r.Month, cur, r.Income, r.Expense));

            OverviewBars.Add(BuildBarChart(cur, rows));
            OverviewLines.Add(BuildLineChart(cur, rows.Select(r => r.Month).ToList(), accounts, all, transfers));

            // en qué se gasta más en esos meses
            var expenses = window.Where(t => t.Type == TransactionType.Gasto).ToList();
            var total = expenses.Sum(t => t.Amount);
            foreach (var g in expenses.GroupBy(t => t.Category.Name)
                         .Select(g => (Name: g.Key, Sum: g.Sum(t => t.Amount)))
                         .OrderByDescending(x => x.Sum).Take(6))
                OverviewCategories.Add(new CategoryTotalRow(g.Name, cur, g.Sum,
                    total == 0 ? 0 : (double)(g.Sum / total * 100)));
        }
    }

    private static decimal SumOf(IEnumerable<FinanceTransaction> items, TransactionType type) =>
        items.Where(t => t.Type == type).Sum(t => t.Amount);

    private static string ShortMonth(DateTime month)
    {
        var text = month.ToString("MMM", Ar).TrimEnd('.');
        return $"{char.ToUpper(text[0])}{text[1..]} '{month:yy}";
    }

    // Monto corto para etiquetas chicas: $1,5 M, $250 mil, US$3 mil.
    private static string Compact(decimal value, Currency currency)
    {
        var abs = Math.Abs(value);
        var body = abs >= 1_000_000 ? $"{(abs / 1_000_000).ToString("0.##", Ar)} M"
            : abs >= 1_000 ? $"{(abs / 1_000).ToString("0.#", Ar)} mil"
            : abs.ToString("0", Ar);
        return $"{(value < 0 ? "−" : "")}{(currency == Currency.USD ? "US$" : "$")}{body}";
    }

    private static BarChart BuildBarChart(Currency cur, List<(DateTime Month, decimal Income, decimal Expense)> rows)
    {
        var max = rows.Max(r => Math.Max(r.Income, r.Expense));
        double Height(decimal v) => max == 0 || v <= 0 ? 0 : Math.Max(3, (double)(v / max) * BarAreaHeight);

        var summary = $"Gráfico de barras de ingresos y gastos por mes en {cur}. " + string.Join(". ",
            rows.Select(r => $"{ShortMonth(r.Month)}: ingresos {Money.Format(r.Income, cur)}, gastos {Money.Format(r.Expense, cur)}"));

        return new BarChart(cur, rows.Select(r => new BarMonth(ShortMonth(r.Month),
            Height(r.Income), Height(r.Expense),
            $"{ShortMonth(r.Month)}\nIngresos: {Money.Format(r.Income, cur)}\nGastos: {Money.Format(r.Expense, cur)}")).ToList(),
            summary);
    }

    // Saldo de la moneda al cierre de cada mes: saldo inicial + movimientos + transferencias hasta esa fecha.
    private static LineChart BuildLineChart(Currency cur, List<DateTime> months, List<Account> accounts,
        List<FinanceTransaction> all, List<Transfer> transfers)
    {
        var ids = accounts.Where(a => a.Currency == cur).Select(a => a.Id).ToHashSet();
        var initial = accounts.Where(a => a.Currency == cur).Sum(a => a.InitialBalance);

        decimal BalanceAt(DateTime endExclusive) =>
            initial
            + all.Where(t => ids.Contains(t.AccountId) && t.Date < endExclusive)
                .Sum(t => t.Type == TransactionType.Ingreso ? t.Amount : -t.Amount)
            + transfers.Where(t => t.Date < endExclusive)
                .Sum(t => (ids.Contains(t.ToAccountId) ? t.AmountIn : 0) - (ids.Contains(t.FromAccountId) ? t.AmountOut : 0));

        var values = months.Select(m => BalanceAt(m.AddMonths(1))).ToList();
        var min = values.Min();
        var max = values.Max();
        var span = max - min;

        var line = new PointCollection();
        var dots = new List<ChartDot>();
        for (var i = 0; i < values.Count; i++)
        {
            // una columna por mes, centrada, para que alinee con las etiquetas de abajo
            var x = LineWidth * (i + 0.5) / values.Count;
            var y = span == 0 ? LineHeight / 2 : 8 + (double)((max - values[i]) / span) * (LineHeight - 16);
            line.Add(new Point(x, y));
            dots.Add(new ChartDot(x, y));
        }

        var labels = months.Select((m, i) => new LinePoint(ShortMonth(m), Compact(values[i], cur), values[i] < 0)).ToList();
        var summary = $"Gráfico de línea del saldo en {cur} al cierre de cada mes. " + string.Join(". ",
            months.Select((m, i) => $"{ShortMonth(m)}: {Money.Format(values[i], cur)}"));
        return new LineChart(cur, line, dots, labels, summary);
    }
}
