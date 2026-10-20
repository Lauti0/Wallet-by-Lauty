using System.Globalization;
using System.IO;
using System.Text;
using CholinanzasApp.Data;
using CholinanzasApp.Models;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace CholinanzasApp.Services;

/// <summary>Exporta los datos de la app a Excel (.xlsx) o CSV. Lee todo directo de la base.</summary>
public static class ExportService
{
    private const string MoneyFormat = "#,##0.00";
    private const string DateFormat = "dd/mm/yyyy";
    private static readonly CultureInfo Ar = new("es-AR");

    // ───────────── Excel ─────────────

    public static void ExportExcel(string path)
    {
        using var db = new AppDbContext();
        var accounts = db.Accounts.AsNoTracking().ToList().OrderBy(a => a.Name).ToList();
        var transactions = db.Transactions.AsNoTracking().Include(t => t.Account).Include(t => t.Category)
            .ToList().OrderBy(t => t.Date).ThenBy(t => t.Id).ToList();
        var transfers = db.Transfers.AsNoTracking().Include(t => t.FromAccount).Include(t => t.ToAccount)
            .ToList().OrderBy(t => t.Date).ThenBy(t => t.Id).ToList();
        var budgets = db.Budgets.AsNoTracking().Include(b => b.Category).ToList()
            .OrderBy(b => b.Category.Name).ThenBy(b => b.Currency).ToList();
        var goals = db.Goals.AsNoTracking().ToList().OrderBy(g => g.TargetDate).ToList();
        var recurrings = db.Recurrings.AsNoTracking().Include(r => r.Account).Include(r => r.Category)
            .ToList().OrderBy(r => r.DayOfMonth).ToList();

        using var workbook = new XLWorkbook();
        WriteMovements(workbook, transactions);
        WriteTransfers(workbook, transfers);
        WriteAccounts(workbook, accounts, transactions, transfers);
        WriteMonthly(workbook, transactions);
        WriteCategories(workbook, transactions);
        WriteBudgets(workbook, budgets);
        WriteGoals(workbook, goals);
        WriteRecurrings(workbook, recurrings);
        workbook.SaveAs(path);
    }

    private static IXLWorksheet NewSheet(XLWorkbook workbook, string name, params string[] headers)
    {
        var sheet = workbook.Worksheets.Add(name);
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(1, i + 1).Value = headers[i];

        var header = sheet.Range(1, 1, 1, headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#4F6BED");
        sheet.SheetView.FreezeRows(1);
        return sheet;
    }

    // Da formato a las columnas numéricas/fecha, agrega filtros y ajusta los anchos.
    private static void Finish(IXLWorksheet sheet, int columns, int lastRow, int[]? moneyColumns = null,
        int[]? dateColumns = null)
    {
        foreach (var c in moneyColumns ?? [])
            sheet.Range(2, c, Math.Max(lastRow, 2), c).Style.NumberFormat.Format = MoneyFormat;
        foreach (var c in dateColumns ?? [])
            sheet.Range(2, c, Math.Max(lastRow, 2), c).Style.DateFormat.Format = DateFormat;

        if (lastRow > 1) sheet.Range(1, 1, lastRow, columns).SetAutoFilter();
        sheet.Columns().AdjustToContents();
    }

    private static void WriteMovements(XLWorkbook workbook, List<FinanceTransaction> transactions)
    {
        var sheet = NewSheet(workbook, "Movimientos",
            "Fecha", "Tipo", "Categoría", "Cuenta", "Moneda", "Descripción", "Monto");
        var row = 1;
        foreach (var t in transactions)
        {
            row++;
            sheet.Cell(row, 1).Value = t.Date;
            sheet.Cell(row, 2).Value = t.Type.ToString();
            sheet.Cell(row, 3).Value = t.Category.Name;
            sheet.Cell(row, 4).Value = t.Account.Name;
            sheet.Cell(row, 5).Value = t.Account.Currency.ToString();
            sheet.Cell(row, 6).Value = t.Description;
            sheet.Cell(row, 7).Value = Signed(t); // ingresos suman, gastos restan
        }
        Finish(sheet, 7, row, moneyColumns: [7], dateColumns: [1]);
    }

    private static void WriteTransfers(XLWorkbook workbook, List<Transfer> transfers)
    {
        var sheet = NewSheet(workbook, "Transferencias",
            "Fecha", "Desde", "Hacia", "Sale", "Moneda origen", "Entra", "Moneda destino", "Cotización", "Descripción");
        var row = 1;
        foreach (var t in transfers)
        {
            row++;
            sheet.Cell(row, 1).Value = t.Date;
            sheet.Cell(row, 2).Value = t.FromAccount.Name;
            sheet.Cell(row, 3).Value = t.ToAccount.Name;
            sheet.Cell(row, 4).Value = t.AmountOut;
            sheet.Cell(row, 5).Value = t.FromAccount.Currency.ToString();
            sheet.Cell(row, 6).Value = t.AmountIn;
            sheet.Cell(row, 7).Value = t.ToAccount.Currency.ToString();
            if (t.FromAccount.Currency != t.ToAccount.Currency && t.AmountOut > 0 && t.AmountIn > 0)
            {
                // pesos por dólar de esa operación
                sheet.Cell(row, 8).Value = t.FromAccount.Currency == Currency.USD
                    ? t.AmountIn / t.AmountOut
                    : t.AmountOut / t.AmountIn;
            }
            sheet.Cell(row, 9).Value = t.Description;
        }
        Finish(sheet, 9, row, moneyColumns: [4, 6, 8], dateColumns: [1]);
    }

    private static void WriteAccounts(XLWorkbook workbook, List<Account> accounts,
        List<FinanceTransaction> transactions, List<Transfer> transfers)
    {
        var sheet = NewSheet(workbook, "Cuentas", "Cuenta", "Moneda", "Saldo inicial", "Saldo actual");
        var row = 1;
        foreach (var a in accounts)
        {
            row++;
            var movements = transactions.Where(t => t.AccountId == a.Id).Sum(Signed);
            var transferred = transfers.Where(t => t.ToAccountId == a.Id).Sum(t => t.AmountIn)
                              - transfers.Where(t => t.FromAccountId == a.Id).Sum(t => t.AmountOut);
            sheet.Cell(row, 1).Value = a.Name;
            sheet.Cell(row, 2).Value = a.Currency.ToString();
            sheet.Cell(row, 3).Value = a.InitialBalance;
            sheet.Cell(row, 4).Value = a.InitialBalance + movements + transferred;
        }
        Finish(sheet, 4, row, moneyColumns: [3, 4]);
    }

    private static void WriteMonthly(XLWorkbook workbook, List<FinanceTransaction> transactions)
    {
        var sheet = NewSheet(workbook, "Mes a mes", "Mes", "Moneda", "Ingresos", "Gastos", "Neto");
        var row = 1;
        foreach (var g in transactions
                     .GroupBy(t => (Month: new DateTime(t.Date.Year, t.Date.Month, 1), t.Account.Currency))
                     .OrderBy(g => g.Key.Month).ThenBy(g => g.Key.Currency))
        {
            row++;
            var income = g.Where(t => t.Type == TransactionType.Ingreso).Sum(t => t.Amount);
            var expense = g.Where(t => t.Type == TransactionType.Gasto).Sum(t => t.Amount);
            sheet.Cell(row, 1).Value = g.Key.Month;
            sheet.Cell(row, 2).Value = g.Key.Currency.ToString();
            sheet.Cell(row, 3).Value = income;
            sheet.Cell(row, 4).Value = expense;
            sheet.Cell(row, 5).Value = income - expense;
        }
        Finish(sheet, 5, row, moneyColumns: [3, 4, 5]);
        sheet.Range(2, 1, Math.Max(row, 2), 1).Style.DateFormat.Format = "mmmm yyyy";
        sheet.Columns().AdjustToContents();
    }

    private static void WriteCategories(XLWorkbook workbook, List<FinanceTransaction> transactions)
    {
        var sheet = NewSheet(workbook, "Por categoría", "Tipo", "Categoría", "Moneda", "Total");
        var row = 1;
        foreach (var g in transactions
                     .GroupBy(t => (t.Type, Category: t.Category.Name, t.Account.Currency))
                     .OrderBy(g => g.Key.Type).ThenBy(g => g.Key.Currency)
                     .ThenByDescending(g => g.Sum(t => t.Amount)))
        {
            row++;
            sheet.Cell(row, 1).Value = g.Key.Type.ToString();
            sheet.Cell(row, 2).Value = g.Key.Category;
            sheet.Cell(row, 3).Value = g.Key.Currency.ToString();
            sheet.Cell(row, 4).Value = g.Sum(t => t.Amount);
        }
        Finish(sheet, 4, row, moneyColumns: [4]);
    }

    private static void WriteBudgets(XLWorkbook workbook, List<Budget> budgets)
    {
        var sheet = NewSheet(workbook, "Presupuestos", "Categoría", "Moneda", "Límite mensual");
        var row = 1;
        foreach (var b in budgets)
        {
            row++;
            sheet.Cell(row, 1).Value = b.Category.Name;
            sheet.Cell(row, 2).Value = b.Currency.ToString();
            sheet.Cell(row, 3).Value = b.Amount;
        }
        Finish(sheet, 3, row, moneyColumns: [3]);
    }

    private static void WriteGoals(XLWorkbook workbook, List<SavingsGoal> goals)
    {
        var sheet = NewSheet(workbook, "Metas", "Meta", "Moneda", "Objetivo", "Ahorrado", "Falta", "Fecha objetivo");
        var row = 1;
        foreach (var g in goals)
        {
            row++;
            sheet.Cell(row, 1).Value = g.Name;
            sheet.Cell(row, 2).Value = g.Currency.ToString();
            sheet.Cell(row, 3).Value = g.TargetAmount;
            sheet.Cell(row, 4).Value = g.SavedAmount;
            sheet.Cell(row, 5).Value = Math.Max(0, g.TargetAmount - g.SavedAmount);
            sheet.Cell(row, 6).Value = g.TargetDate;
        }
        Finish(sheet, 6, row, moneyColumns: [3, 4, 5], dateColumns: [6]);
    }

    private static void WriteRecurrings(XLWorkbook workbook, List<RecurringTransaction> recurrings)
    {
        var sheet = NewSheet(workbook, "Recurrentes",
            "Descripción", "Tipo", "Día del mes", "Categoría", "Cuenta", "Moneda", "Monto");
        var row = 1;
        foreach (var r in recurrings)
        {
            row++;
            sheet.Cell(row, 1).Value = r.Description;
            sheet.Cell(row, 2).Value = r.Type.ToString();
            sheet.Cell(row, 3).Value = r.DayOfMonth;
            sheet.Cell(row, 4).Value = r.Category.Name;
            sheet.Cell(row, 5).Value = r.Account.Name;
            sheet.Cell(row, 6).Value = r.Account.Currency.ToString();
            sheet.Cell(row, 7).Value = r.Amount;
        }
        Finish(sheet, 7, row, moneyColumns: [7]);
    }

    // ───────────── CSV ─────────────

    /// <summary>
    /// Movimientos en CSV listo para abrir en Excel en español: separador ";", coma decimal,
    /// fecha dd/MM/yyyy y UTF-8 con BOM (para que se vean bien los acentos).
    /// </summary>
    public static void ExportMovementsCsv(string path)
    {
        using var db = new AppDbContext();
        var transactions = db.Transactions.AsNoTracking().Include(t => t.Account).Include(t => t.Category)
            .ToList().OrderBy(t => t.Date).ThenBy(t => t.Id).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("Fecha;Tipo;Categoría;Cuenta;Moneda;Descripción;Monto");
        foreach (var t in transactions)
        {
            sb.AppendLine(string.Join(';',
                t.Date.ToString("dd/MM/yyyy", Ar),
                t.Type,
                Text(t.Category.Name),
                Text(t.Account.Name),
                t.Account.Currency,
                Text(t.Description),
                Signed(t).ToString("0.00", Ar)));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    // Escapa para CSV y evita que Excel interprete texto como fórmula (=, +, -, @ al inicio).
    private static string Text(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@')
            value = "'" + value;
        return value.IndexOfAny([';', '"', '\n', '\r']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    private static decimal Signed(FinanceTransaction t) =>
        t.Type == TransactionType.Ingreso ? t.Amount : -t.Amount;
}
