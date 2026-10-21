using CholinanzasApp.Data;
using CholinanzasApp.Models;
using CholinanzasApp.Services;
using CholinanzasApp.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace CholinanzasApp.Tests;

/// <summary>Los números que la app le muestra al usuario: saldos, resumen del mes, proyección, presupuestos y metas.</summary>
public class FinanceTests : DatabaseTest
{
    private static readonly DateTime Today = DateTime.Today;

    private static int AddTransfer(int from, int to, decimal amountOut, decimal amountIn)
    {
        using var db = new AppDbContext();
        var transfer = new Transfer { FromAccountId = from, ToAccountId = to, AmountOut = amountOut, AmountIn = amountIn, Date = Today, Description = "t" };
        db.Transfers.Add(transfer);
        db.SaveChanges();
        return transfer.Id;
    }

    private static void AddRecurring(int account, TransactionType type, decimal amount, int day, string description)
    {
        using var db = new AppDbContext();
        db.Recurrings.Add(new RecurringTransaction
        {
            AccountId = account, Type = type, Amount = amount, DayOfMonth = day, Description = description,
            CategoryId = db.Categories.First(c => c.Type == type).Id
        });
        db.SaveChanges();
    }

    private static void AddBudget(string category, decimal amount, Currency currency = Currency.ARS)
    {
        using var db = new AppDbContext();
        db.Budgets.Add(new Budget { CategoryId = db.Categories.First(c => c.Name == category).Id, Amount = amount, Currency = currency });
        db.SaveChanges();
    }

    // ───────────── Saldos ─────────────

    [Fact]
    public void El_saldo_de_una_cuenta_es_el_inicial_mas_ingresos_menos_gastos_mas_transferencias_recibidas_menos_enviadas()
    {
        var banco = AddAccount("Banco", Currency.ARS, initial: 1000m);
        var otra = AddAccount("Otra", Currency.ARS, initial: 0m);
        AddTransaction(banco, TransactionType.Ingreso, 500m, Today);
        AddTransaction(banco, TransactionType.Gasto, 200m, Today);
        AddTransfer(otra, banco, amountOut: 300m, amountIn: 300m);   // entran 300
        AddTransfer(banco, otra, amountOut: 100m, amountIn: 100m);   // salen 100

        var vm = new MainViewModel();

        Assert.Equal(1500m, vm.Accounts.Single(a => a.Id == banco).Balance);
        Assert.Equal(-200m, vm.Accounts.Single(a => a.Id == otra).Balance);   // 0 - 300 + 100
    }

    [Fact]
    public void Una_transferencia_entre_monedas_suma_lo_que_entra_y_resta_lo_que_sale_y_no_cuenta_como_ingreso_ni_gasto()
    {
        var pesos = AddAccount("Pesos", Currency.ARS, initial: 120000m);
        var dolares = AddAccount("Dólares", Currency.USD, initial: 0m);
        AddTransfer(pesos, dolares, amountOut: 120000m, amountIn: 100m);

        var vm = new MainViewModel();

        Assert.Equal(0m, vm.Accounts.Single(a => a.Id == pesos).Balance);
        Assert.Equal(100m, vm.Accounts.Single(a => a.Id == dolares).Balance);
        Assert.Empty(vm.Summary);   // no hay movimientos: la transferencia no es ingreso ni gasto
    }

    [Fact]
    public void El_patrimonio_total_convierte_los_dolares_con_la_ultima_cotizacion()
    {
        AddAccount("Pesos", Currency.ARS, initial: 1_200_000m);
        AddAccount("Dólares", Currency.USD, initial: 100m);
        using (var db = new AppDbContext())
        {
            db.Rates.Add(new ExchangeRate { Date = Today.AddDays(-5), ArsPerUsd = 1000m });   // vieja
            db.Rates.Add(new ExchangeRate { Date = Today, ArsPerUsd = 1200m });               // la última
            db.SaveChanges();
        }

        var today = new MainViewModel().NetWorth.First();

        Assert.Equal(Money.Format(1_320_000m, Currency.ARS), today.ArsText);    // 1.200.000 + 100 × 1.200
        Assert.Contains(Money.Format(1_100m, Currency.USD), today.UsdText);     // 1.320.000 / 1.200
    }

    [Fact]
    public void Con_dolares_y_sin_cotizacion_el_patrimonio_pide_cargarla_en_vez_de_inventar_un_numero()
    {
        AddAccount("Dólares", Currency.USD, initial: 100m);

        var row = new MainViewModel().NetWorth.First();

        Assert.Contains("cotización", row.Label);
        Assert.Equal("—", row.ArsText);
    }

    // ───────────── Resumen del mes ─────────────

    [Fact]
    public void El_resumen_del_mes_separa_ingresos_y_gastos_por_moneda_y_deja_afuera_otros_meses()
    {
        var pesos = FirstAccountId();
        var dolares = AddAccount("Dólares", Currency.USD);
        var firstOfMonth = new DateTime(Today.Year, Today.Month, 1);
        AddTransaction(pesos, TransactionType.Ingreso, 1000m, firstOfMonth);
        AddTransaction(pesos, TransactionType.Gasto, 300m, firstOfMonth);
        AddTransaction(dolares, TransactionType.Ingreso, 50m, firstOfMonth);
        AddTransaction(pesos, TransactionType.Gasto, 9999m, firstOfMonth.AddMonths(-1));   // mes anterior: no cuenta

        var summary = new MainViewModel().Summary.ToDictionary(s => s.Currency);

        Assert.Equal(1000m, summary[Currency.ARS].Income);
        Assert.Equal(300m, summary[Currency.ARS].Expense);
        Assert.Equal(50m, summary[Currency.USD].Income);
        Assert.Equal(0m, summary[Currency.USD].Expense);
    }

    // ───────────── Proyección a fin de mes ─────────────

    [Fact]
    public void La_proyeccion_suma_los_recurrentes_pendientes_y_divide_lo_proyectado_por_los_dias_que_quedan()
    {
        AddAccount("Banco", Currency.ARS, initial: 10000m);
        var account = FirstAccountId();
        AddRecurring(account, TransactionType.Ingreso, 5000m, day: 1, "Sueldo");
        AddRecurring(account, TransactionType.Gasto, 1000m, day: 15, "Alquiler");

        var projection = new MainViewModel().Projection.Single(p => p.Currency == Currency.ARS);

        Assert.Equal(10000m, projection.Balance);
        Assert.Equal(5000m, projection.PendingIncome);
        Assert.Equal(1000m, projection.PendingExpense);
        Assert.Equal(14000m, projection.Projected);
        // los días que quedan incluyen hoy
        var daysLeft = DateTime.DaysInMonth(Today.Year, Today.Month) - Today.Day + 1;
        Assert.Equal(daysLeft, projection.DaysLeft);
        Assert.Equal(Money.Format(14000m / daysLeft, Currency.ARS), projection.DailyText);
    }

    [Fact]
    public void Registrar_un_recurrente_lo_saca_de_pendientes_y_la_proyeccion_final_no_cambia()
    {
        AddAccount("Banco", Currency.ARS, initial: 10000m);
        var account = FirstAccountId();
        AddRecurring(account, TransactionType.Gasto, 1000m, day: 15, "Alquiler");
        int recurringId;
        using (var db = new AppDbContext()) recurringId = db.Recurrings.Single().Id;
        var before = new MainViewModel().Projection.Single(p => p.Currency == Currency.ARS);

        // el usuario lo registra: se crea el movimiento real, enlazado al recurrente
        AddTransaction(account, TransactionType.Gasto, 1000m, Today, "Alquiler", recurringId: recurringId);
        var vm = new MainViewModel();
        var after = vm.Projection.Single(p => p.Currency == Currency.ARS);

        Assert.Empty(vm.Pending);
        Assert.Equal(0m, after.PendingExpense);
        Assert.Equal(before.Balance - 1000m, after.Balance);   // el saldo de hoy bajó
        Assert.Equal(before.Projected, after.Projected);       // pero lo que vas a tener a fin de mes es lo mismo
    }

    [Fact]
    public void Sin_recurrentes_lo_proyectado_es_el_saldo_de_hoy()
    {
        AddAccount("Banco", Currency.ARS, initial: 777m);

        var projection = new MainViewModel().Projection.Single(p => p.Currency == Currency.ARS);

        Assert.Equal(projection.Balance, projection.Projected);
    }

    // ───────────── Presupuestos ─────────────

    [Theory]
    [InlineData("79999", "OK")]
    [InlineData("80000", "Cerca del límite")]
    [InlineData("100000", "Cerca del límite")]   // justo en el límite todavía no está excedido
    [InlineData("100001", "Excedido")]
    public void El_estado_de_un_presupuesto_cambia_al_80_por_ciento_y_al_pasarse_del_limite(string spent, string status)
    {
        var row = new BudgetRow(1, "Supermercado", Currency.ARS, Limit: 100000m, Spent: decimal.Parse(spent));

        Assert.Equal(status, row.Status);
    }

    [Fact]
    public void Un_presupuesto_cerca_del_limite_o_excedido_genera_una_alerta_con_su_gravedad()
    {
        AddBudget("Supermercado", 100000m);
        var account = FirstAccountId();
        var category = CategoryId(TransactionType.Gasto, "Supermercado");

        Assert.Empty(new MainViewModel().BudgetAlerts);                                         // sin gastos, sin alerta

        AddTransaction(account, TransactionType.Gasto, 85000m, Today, "compra", category);
        var near = new MainViewModel().BudgetAlerts.Single();
        Assert.False(near.IsExceeded);
        Assert.Contains("cerca del límite", near.Text);

        AddTransaction(account, TransactionType.Gasto, 20000m, Today, "otra compra", category);
        var over = new MainViewModel().BudgetAlerts.Single();
        Assert.True(over.IsExceeded);
        Assert.Contains("Superaste el presupuesto", over.Text);
    }

    [Fact]
    public void Las_alertas_visibles_tienen_tope_y_el_resto_se_resume_en_una_linea()
    {
        var account = FirstAccountId();
        foreach (var name in new[] { "Supermercado", "Transporte", "Salidas", "Salud", "Educación" })
        {
            AddBudget(name, 100m);
            AddTransaction(account, TransactionType.Gasto, 500m, Today, "gasto", CategoryId(TransactionType.Gasto, name));
        }

        var alerts = new MainViewModel().BudgetAlerts;

        Assert.Equal(4, alerts.Count);                       // 3 alertas y la línea "Y 2 alertas más"
        Assert.Contains("2 alertas más", alerts.Last().Text);
    }

    // ───────────── Metas de ahorro ─────────────

    [Fact]
    public void Una_meta_calcula_cuanto_falta_y_cuanto_ahorrar_por_mes()
    {
        var goal = new GoalRow(1, "Viaje", Currency.ARS, Target: 120000m, Saved: 30000m, TargetDate: Today.AddMonths(6), MonthsLeft: 6);

        Assert.Equal(90000m, goal.Remaining);
        Assert.Equal(15000m, goal.MonthlyNeeded);
        Assert.Equal(25d, goal.Percent);
        Assert.Equal("6 meses", goal.Status);
    }

    [Fact]
    public void Una_meta_cumplida_no_necesita_ahorro_y_una_vencida_necesita_todo_ya()
    {
        var done = new GoalRow(1, "Hecha", Currency.ARS, 1000m, 1500m, Today, 0);
        var late = new GoalRow(2, "Vencida", Currency.ARS, 1000m, 400m, Today.AddDays(-5), 0);

        Assert.Equal(0m, done.MonthlyNeeded);
        Assert.Equal("Cumplida", done.Status);
        Assert.Equal(100d, done.Percent);              // nunca pasa de 100
        Assert.Equal(600m, late.MonthlyNeeded);
        Assert.Equal("Vencida", late.Status);
    }

    // ───────────── Formato de dinero ─────────────

    [Fact]
    public void El_dinero_se_muestra_con_el_formato_argentino()
    {
        Assert.Equal("$ 1.234,50", Money.Format(1234.5m, Currency.ARS));
        Assert.Equal("US$ 20,00", Money.Format(20m, Currency.USD));
        Assert.Equal("$ -3.500,00", Money.Format(-3500m, Currency.ARS));
    }

    // ───────────── Eliminar cuentas ─────────────

    private static (MainViewModel Vm, List<string> Asked, List<string> Informed) VmWithAnswers(bool confirm)
    {
        var asked = new List<string>();
        var informed = new List<string>();
        var vm = new MainViewModel
        {
            ConfirmDangerous = text => { asked.Add(text); return confirm; },
            InfoNotifier = informed.Add
        };
        return (vm, asked, informed);
    }

    private (int Banco, int Dolares) SeedForDeletion()
    {
        var efectivo = FirstAccountId();
        var banco = AddAccount("Banco");
        var dolares = AddAccount("Dólares", Currency.USD);
        AddTransaction(efectivo, TransactionType.Gasto, 100m, Today, "efectivo");
        AddTransaction(banco, TransactionType.Gasto, 200m, Today, "banco 1");
        AddTransaction(banco, TransactionType.Gasto, 300m, Today, "banco 2");
        AddTransfer(efectivo, banco, 50m, 50m);
        AddTransfer(efectivo, dolares, 10m, 10m);
        AddRecurring(banco, TransactionType.Gasto, 5m, 1, "alquiler");
        return (banco, dolares);
    }

    [Fact]
    public void Al_eliminar_una_cuenta_se_avisa_que_se_pierde_con_ella()
    {
        var (banco, _) = SeedForDeletion();
        var (vm, asked, _) = VmWithAnswers(confirm: false);

        vm.DeleteAccount(vm.Accounts.Single(a => a.Id == banco));

        var message = Assert.Single(asked);
        Assert.Contains("2 movimientos", message);
        Assert.Contains("1 transferencia", message);
        Assert.Contains("1 movimiento recurrente", message);
    }

    [Fact]
    public void Si_el_usuario_dice_que_no_no_se_borra_nada_ni_se_hace_respaldo()
    {
        var (banco, _) = SeedForDeletion();
        var (vm, _, _) = VmWithAnswers(confirm: false);
        var backups = BackupService.List().Count;

        vm.DeleteAccount(vm.Accounts.Single(a => a.Id == banco));

        using var db = new AppDbContext();
        Assert.Equal((3, 3, 2, 1), (db.Accounts.Count(), db.Transactions.Count(), db.Transfers.Count(), db.Recurrings.Count()));
        Assert.Equal(backups, BackupService.List().Count);
    }

    [Fact]
    public void Si_el_usuario_confirma_se_borra_la_cuenta_con_sus_cosas_y_antes_se_hace_un_respaldo()
    {
        var (banco, _) = SeedForDeletion();
        var (vm, _, _) = VmWithAnswers(confirm: true);
        var backups = BackupService.List().Count;

        vm.DeleteAccount(vm.Accounts.Single(a => a.Id == banco));

        using var db = new AppDbContext();
        Assert.DoesNotContain(db.Accounts.ToList(), a => a.Id == banco);
        Assert.Equal(["efectivo"], db.Transactions.Select(t => t.Description).ToList());           // se fueron sus 2 movimientos
        Assert.Equal(["Dólares"], db.Transfers.Include(t => t.ToAccount).Select(t => t.ToAccount.Name).ToList()); // y la transferencia que la tocaba
        Assert.Empty(db.Recurrings);
        Assert.True(BackupService.List().Count > backups);
        Assert.DoesNotContain(vm.Accounts, a => a.Id == banco);                                     // y la pantalla se actualizó
    }

    [Fact]
    public void La_ultima_cuenta_no_se_puede_eliminar_y_se_explica_por_que()
    {
        var (vm, asked, informed) = VmWithAnswers(confirm: true);

        vm.DeleteAccount(vm.Accounts.Single());

        Assert.Empty(asked);                                  // ni siquiera se pregunta
        Assert.Contains("única cuenta", Assert.Single(informed));
        Assert.Single(vm.Accounts);
    }
}
