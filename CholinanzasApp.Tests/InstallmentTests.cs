using System.IO;
using CholinanzasApp.Data;
using CholinanzasApp.Models;
using CholinanzasApp.Services;
using CholinanzasApp.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace CholinanzasApp.Tests;

/// <summary>Compras en cuotas con tarjeta: cálculo de fechas y avance, pendientes, proyección y registro de cada cuota.</summary>
public class InstallmentTests : DatabaseTest
{
    private static readonly DateTime Today = DateTime.Today;

    private static (MainViewModel Vm, List<string> Informed) NewVm()
    {
        var informed = new List<string>();
        return (new MainViewModel { InfoNotifier = informed.Add, ConfirmDangerous = _ => true }, informed);
    }

    private static int AddPlan(decimal amount, int count, DateTime firstDate, int paidBefore = 0, int? accountId = null, string description = "Notebook")
    {
        using var db = new AppDbContext();
        var plan = new InstallmentPlan
        {
            Description = description, InstallmentAmount = amount, Count = count, FirstDate = firstDate.Date, PaidBefore = paidBefore,
            AccountId = accountId ?? db.Accounts.First().Id,
            CategoryId = db.Categories.First(c => c.Type == TransactionType.Gasto).Id
        };
        db.Installments.Add(plan);
        db.SaveChanges();
        return plan.Id;
    }

    private static void FillForm(MainViewModel vm, string description = "Notebook", string amount = "100000", string count = "12",
        string paidBefore = "0", DateTime? first = null)
    {
        vm.NewInstDescription = description;
        vm.NewInstAmount = amount;
        vm.NewInstCount = count;
        vm.NewInstPaidBefore = paidBefore;
        vm.NewInstFirstDate = first ?? Today;
    }

    // ───────────── Cálculos ─────────────

    [Fact]
    public void Cada_cuota_cae_un_mes_despues_y_si_el_mes_no_tiene_ese_dia_cae_el_ultimo()
    {
        var plan = new InstallmentPlan { Count = 4, FirstDate = new DateTime(2027, 1, 31) };

        Assert.Equal(new DateTime(2027, 1, 31), InstallmentService.DateOf(plan, 1));
        Assert.Equal(new DateTime(2027, 2, 28), InstallmentService.DateOf(plan, 2));
        Assert.Equal(new DateTime(2027, 3, 31), InstallmentService.DateOf(plan, 3));
        Assert.Equal(new DateTime(2027, 4, 30), InstallmentService.DateOf(plan, 4));
    }

    [Fact]
    public void El_avance_suma_las_pagadas_antes_y_las_registradas_y_lo_que_falta_se_calcula_con_eso()
    {
        var plan = new InstallmentPlan { Count = 12, InstallmentAmount = 1000m, PaidBefore = 3 };

        Assert.Equal(3, InstallmentService.Paid(plan, []));
        Assert.Equal(5, InstallmentService.Paid(plan, [4, 5]));
        Assert.Equal(9000m, InstallmentService.Remaining(plan, []));
        Assert.Equal(7000m, InstallmentService.Remaining(plan, [4, 5]));
        Assert.Equal([6, 7, 8, 9, 10, 11, 12], InstallmentService.Unregistered(plan, [4, 5]));
    }

    [Fact]
    public void Un_numero_repetido_o_fuera_de_rango_no_cuenta_dos_veces()
    {
        var plan = new InstallmentPlan { Count = 3, InstallmentAmount = 10m, PaidBefore = 1 };

        Assert.Equal(2, InstallmentService.Paid(plan, [2, 2, 1, 99]));
    }

    // ───────────── Alta desde el formulario ─────────────

    [Fact]
    public void Cargar_una_compra_en_cuotas_la_guarda_y_muestra_el_total_y_lo_que_falta()
    {
        var (vm, informed) = NewVm();
        FillForm(vm, amount: "100000", count: "12");

        vm.AddInstallmentCommand.Execute(null);

        Assert.Empty(informed);
        var row = Assert.Single(vm.Installments);
        // la primera cuota es de hoy: ya se registró sola
        Assert.Equal(("Notebook", 12, 1), (row.Description, row.Count, row.Paid));
        Assert.Equal(1_100_000m, row.Remaining);
        Assert.Equal(Money.Format(1_200_000m, Currency.ARS), row.TotalText);
        Assert.Equal(Today.Date.AddMonths(1), row.NextDate);
        Assert.Equal("", vm.NewInstDescription); // el formulario quedó vacío
    }

    [Theory]
    [InlineData("", "100", "12", "0")]       // sin descripción
    [InlineData("PC", "0", "12", "0")]       // cuota en cero
    [InlineData("PC", "abc", "12", "0")]     // cuota que no es número
    [InlineData("PC", "100", "0", "0")]      // sin cuotas
    [InlineData("PC", "100", "121", "0")]    // demasiadas cuotas
    [InlineData("PC", "100", "6", "6")]      // ya pagadas = todas
    [InlineData("PC", "100", "6", "-1")]     // ya pagadas negativas
    public void Los_datos_invalidos_se_rechazan_con_un_aviso_y_no_se_guarda_nada(string description, string amount, string count, string paid)
    {
        var (vm, informed) = NewVm();
        FillForm(vm, description, amount, count, paid);

        vm.AddInstallmentCommand.Execute(null);

        Assert.Single(informed);
        using var db = new AppDbContext();
        Assert.Empty(db.Installments);
    }

    // ───────────── Pendientes y proyección ─────────────

    [Fact]
    public void La_cuota_cuya_fecha_llego_se_registra_sola_y_las_de_los_meses_siguientes_quedan_pendientes_y_restan_en_la_proyeccion()
    {
        var account = AddAccount("Banco", Currency.ARS, initial: 500_000m);
        AddPlan(100_000m, 12, Today, accountId: account);

        var vm = new MainViewModel();

        // la cuota 1 (hoy) ya es un gasto real, enlazado al plan
        using (var db = new AppDbContext())
        {
            var tx = db.Transactions.Single();
            Assert.Equal(("Notebook (cuota 1/12)", 100_000m, 1), (tx.Description, tx.Amount, tx.InstallmentNumber!.Value));
            Assert.Equal(Today.Date, tx.Date);
        }
        Assert.Empty(vm.Pending);
        Assert.Equal(400_000m, vm.Accounts.Single(a => a.Id == account).Balance);

        // y la del mes que viene es un pendiente que resta en la proyección de ese mes
        vm.NextMonthCommand.Execute(null);
        var pending = Assert.Single(vm.Pending);
        Assert.Equal("Notebook (cuota 2/12)", pending.Description);
        Assert.Equal(-100_000m, pending.SignedAmount);
        Assert.Equal(100_000m, vm.Projection.Single(p => p.Currency == Currency.ARS).PendingExpense);
    }

    [Fact]
    public void Al_mirar_un_mes_futuro_se_ven_tambien_las_cuotas_de_los_meses_intermedios()
    {
        AddPlan(100m, 12, Today);
        var vm = new MainViewModel();

        vm.NextMonthCommand.Execute(null);
        vm.NextMonthCommand.Execute(null);

        // la 1 ya se registró sola: faltan la de cada uno de los dos meses siguientes
        Assert.Equal([2, 3], vm.Pending.Select(p => int.Parse(p.Description.Split("cuota ")[1].Split('/')[0])).ToList());
    }

    [Fact]
    public void Las_cuotas_ya_pagadas_antes_de_cargar_el_plan_no_se_registran_ni_quedan_pendientes()
    {
        // 12 cuotas, la primera fue hace 3 meses y ya pagué 3: la que toca hoy es la 4
        AddPlan(100m, 12, Today.AddMonths(-3), paidBefore: 3);

        var vm = new MainViewModel();

        using (var db = new AppDbContext())
            Assert.Equal(["Notebook (cuota 4/12)"], db.Transactions.Select(t => t.Description).ToList());
        Assert.Empty(vm.Pending);
        Assert.Equal(4, vm.Installments.Single().Paid);
        Assert.Equal(800m, vm.Installments.Single().Remaining);
    }

    [Fact]
    public void Una_compra_vieja_registra_cada_cuota_vencida_con_su_fecha_original()
    {
        AddPlan(100m, 3, Today.AddMonths(-2));

        var vm = new MainViewModel();

        using var db = new AppDbContext();
        Assert.Equal(3, db.Transactions.Count());
        Assert.Equal(Today.AddMonths(-2).Date, db.Transactions.Min(t => t.Date));
        Assert.Empty(vm.Pending);
        Assert.Equal("3 de 3 · pagado", vm.Installments.Single().ProgressText);
    }

    [Fact]
    public void El_registro_automatico_no_duplica_cuotas_ni_registra_las_futuras()
    {
        AddPlan(100m, 12, Today);
        using var db = new AppDbContext();

        var first = InstallmentService.RegisterDue(db, Today);
        var second = InstallmentService.RegisterDue(db, Today);
        var inAMonth = InstallmentService.RegisterDue(db, Today.AddMonths(1));

        Assert.Equal((1, 0, 1), (first, second, inAMonth));
        Assert.Equal([1, 2], db.Transactions.Select(t => t.InstallmentNumber!.Value).OrderBy(n => n).ToList());
    }

    [Fact]
    public void Si_el_usuario_borra_a_mano_el_gasto_de_una_cuota_no_se_vuelve_a_crear_y_queda_pendiente()
    {
        AddPlan(100m, 3, Today);
        var (vm, _) = NewVm();
        vm.DeleteTransactionCommand.Execute(vm.Transactions.Single());

        var again = new MainViewModel();

        using var db = new AppDbContext();
        Assert.Empty(db.Transactions);                       // no volvió a aparecer solo
        var pending = Assert.Single(again.Pending);          // pero la cuota sigue figurando como pendiente
        Assert.Equal("Notebook (cuota 1/3)", pending.Description);
    }

    // ───────────── Registrar una cuota ─────────────

    [Fact]
    public void Registrar_a_mano_una_cuota_futura_crea_el_gasto_enlazado_la_saca_de_pendientes_y_avanza_el_plan()
    {
        var account = AddAccount("Visa", Currency.ARS, initial: 0m);
        var planId = AddPlan(100m, 3, Today.AddMonths(1), accountId: account);
        var vm = new MainViewModel();
        vm.NextMonthCommand.Execute(null);

        vm.ConfirmPendingCommand.Execute(vm.Pending.Single());

        using (var db = new AppDbContext())
        {
            var tx = db.Transactions.Single();
            Assert.Equal((TransactionType.Gasto, 100m, account), (tx.Type, tx.Amount, tx.AccountId));
            Assert.Equal((planId, 1), (tx.InstallmentPlanId!.Value, tx.InstallmentNumber!.Value));
            Assert.Null(tx.RecurringId);
        }
        Assert.DoesNotContain(vm.Pending, p => p.Description.Contains("cuota 1/"));
        Assert.Equal(1, vm.Installments.Single().Paid);
        Assert.Equal(200m, vm.Installments.Single().Remaining);
        Assert.Equal(-100m, vm.Accounts.Single(a => a.Id == account).Balance);
    }

    [Fact]
    public void Cuando_se_registran_todas_las_cuotas_el_plan_queda_pagado_sin_proxima_fecha()
    {
        AddPlan(100m, 2, Today.AddMonths(-1));

        var vm = new MainViewModel();

        var plan = vm.Installments.Single();
        Assert.Empty(vm.Pending);
        Assert.Equal(("2 de 2 · pagado", 0m, null), (plan.ProgressText, plan.Remaining, plan.NextDate));
    }

    // ───────────── Edición y borrado ─────────────

    [Fact]
    public void Editar_un_plan_cambia_lo_que_falta_y_no_toca_las_cuotas_ya_registradas()
    {
        AddPlan(100m, 3, Today);
        var (vm, _) = NewVm();                                   // al abrir se registra sola la cuota 1, a $100

        vm.EditInstallmentCommand.Execute(vm.Installments.Single());
        vm.NewInstAmount = "150";
        vm.AddInstallmentCommand.Execute(null);

        using var db = new AppDbContext();
        Assert.Equal(150m, db.Installments.Single().InstallmentAmount);
        Assert.Equal(100m, db.Transactions.Single().Amount);
        Assert.Equal(300m, vm.Installments.Single().Remaining);  // 2 cuotas que faltan a $150
    }

    [Fact]
    public void Borrar_un_plan_conserva_las_cuotas_ya_registradas_y_deja_de_proyectar_el_resto()
    {
        AddPlan(100m, 3, Today);
        var (vm, _) = NewVm();                                   // la cuota 1 ya se registró sola

        vm.DeleteInstallmentCommand.Execute(vm.Installments.Single());

        using var db = new AppDbContext();
        Assert.Empty(db.Installments);
        Assert.Single(db.Transactions);
        Assert.Empty(vm.Pending);
        Assert.Empty(vm.Installments);
    }

    [Fact]
    public void Eliminar_una_cuenta_avisa_y_borra_tambien_sus_compras_en_cuotas()
    {
        var visa = AddAccount("Visa");
        AddPlan(100m, 3, Today, accountId: visa);
        var asked = new List<string>();
        var vm = new MainViewModel { ConfirmDangerous = text => { asked.Add(text); return true; }, InfoNotifier = _ => { } };

        vm.DeleteAccount(vm.Accounts.Single(a => a.Id == visa));

        Assert.Contains("1 compra en cuotas", Assert.Single(asked));
        using var db = new AppDbContext();
        Assert.Empty(db.Installments);
        Assert.Empty(vm.Installments);
    }

    [Fact]
    public void No_se_puede_cambiar_la_moneda_de_una_cuenta_que_tiene_cuotas()
    {
        var visa = AddAccount("Visa");
        AddPlan(100m, 3, Today, accountId: visa);
        var informed = new List<string>();
        var vm = new MainViewModel { InfoNotifier = informed.Add };

        vm.EditAccountCommand.Execute(vm.Accounts.Single(a => a.Id == visa));
        vm.NewAccountCurrency = Currency.USD;
        vm.AddAccountCommand.Execute(null);

        Assert.Contains("cuotas", Assert.Single(informed));
        using var db = new AppDbContext();
        Assert.Equal(Currency.ARS, db.Accounts.Single(a => a.Id == visa).Currency);
    }

    // ───────────── Exportación y base existente ─────────────

    [Fact]
    public void El_Excel_exportado_incluye_la_hoja_de_cuotas()
    {
        AddPlan(100m, 3, Today);
        var path = Path.Combine(DataDir, "cuotas.xlsx");

        ExportService.ExportExcel(path);

        Assert.Contains("Cuotas", ExcelImportService.GetSheetNames(path));
    }

    [Fact]
    public void Una_base_con_cuotas_de_la_version_anterior_gana_la_columna_nueva_y_conserva_los_planes()
    {
        AddPlan(100m, 3, Today.AddMonths(1));
        using (var db = new AppDbContext())
            db.Database.ExecuteSqlRaw("""ALTER TABLE "Installments" DROP COLUMN "AutoRegisteredUpTo" """);

        AppDbContext.Initialize();

        using var after = new AppDbContext();
        var plan = Assert.Single(after.Installments);
        Assert.Equal((100m, 0), (plan.InstallmentAmount, plan.AutoRegisteredUpTo));
    }

    [Fact]
    public void Una_base_anterior_sin_cuotas_se_actualiza_sin_perder_datos()
    {
        var account = FirstAccountId();
        AddTransaction(account, TransactionType.Gasto, 50m, Today, "viejo");
        using (var db = new AppDbContext())
        {
            // se simula una base de una versión vieja: sin la tabla de cuotas ni las columnas nuevas
            db.Database.ExecuteSqlRaw("""DROP TABLE "Installments" """);
            db.Database.ExecuteSqlRaw("""ALTER TABLE "Transactions" DROP COLUMN "InstallmentPlanId" """);
            db.Database.ExecuteSqlRaw("""ALTER TABLE "Transactions" DROP COLUMN "InstallmentNumber" """);
        }

        AppDbContext.Initialize();

        using var after = new AppDbContext();
        Assert.Equal("viejo", after.Transactions.Single().Description);
        Assert.Empty(after.Installments);
    }
}
