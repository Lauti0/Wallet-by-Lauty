using ClosedXML.Excel;
using CholinanzasApp.Data;
using CholinanzasApp.Models;
using CholinanzasApp.Services;
using CholinanzasApp.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace CholinanzasApp.Tests;

/// <summary>El asistente de importación completo contra la base: vista previa, duplicados al re-importar y qué queda guardado.</summary>
public class ImportViewModelTests : DatabaseTest
{
    private const string Extracto =
        "Fecha;Descripción;Monto\n" +
        "15/10/2026;Cafe Martinez;-3500,00\n" +
        "15/10/2026;Cafe Martinez;-3500,00\n" +       // dos gastos iguales el mismo día: son dos reales
        "16/10/2026;Sueldo;1500000,00\n" +
        "17/10/2026;Supermercado Carrefour;-85000,50\n";

    private static ImportViewModel Open(string path, int? accountId = null)
    {
        var vm = new ImportViewModel();
        if (accountId is { } id) vm.SelectedAccount = vm.Accounts.First(a => a.Id == id);
        vm.LoadFile(path);
        return vm;
    }

    private static int Count(ImportViewModel vm, ImportStatus status) => vm.Preview.Count(r => r.Status == status);

    private static int TransactionCount()
    {
        using var db = new AppDbContext();
        return db.Transactions.Count();
    }

    [Fact]
    public void La_vista_previa_propone_todo_y_no_guarda_nada_hasta_confirmar()
    {
        var vm = Open(WriteFile("a.csv", Extracto));

        Assert.True(vm.IsCsv);
        Assert.Equal(4, Count(vm, ImportStatus.New));
        Assert.True(vm.CanImport);
        Assert.Equal(0, TransactionCount());
    }

    [Fact]
    public void Importar_guarda_los_movimientos_con_sus_montos_tipos_y_fechas()
    {
        var vm = Open(WriteFile("a.csv", Extracto));

        Assert.True(vm.RunImport());

        using var db = new AppDbContext();
        var saved = db.Transactions.OrderBy(t => t.Date).ThenBy(t => t.Id).ToList();
        Assert.Equal(4, saved.Count);
        Assert.Equal(4, vm.ImportedCount);
        Assert.Equal(new DateTime(2026, 10, 17), vm.NewestImportedDate);
        Assert.Contains(saved, t => t.Type == TransactionType.Ingreso && t.Amount == 1500000m && t.Description == "Sueldo");
        Assert.Contains(saved, t => t.Type == TransactionType.Gasto && t.Amount == 85000.50m);
    }

    [Fact]
    public void Importar_hace_un_respaldo_antes_de_guardar()
    {
        var before = BackupService.List().Count;

        Open(WriteFile("a.csv", Extracto)).RunImport();

        Assert.True(BackupService.List().Count > before);
    }

    [Fact]
    public void Importar_el_mismo_archivo_otra_vez_no_repite_nada()
    {
        var path = WriteFile("a.csv", Extracto);
        Open(path).RunImport();

        var again = Open(path);

        Assert.Equal(0, Count(again, ImportStatus.New));
        Assert.Equal(4, Count(again, ImportStatus.Duplicate));   // incluidos los dos cafés iguales
        Assert.False(again.CanImport);
    }

    [Fact]
    public void Un_periodo_que_se_superpone_importa_solo_lo_nuevo()
    {
        Open(WriteFile("a.csv", Extracto)).RunImport();
        var next = Open(WriteFile("b.csv",
            "Fecha;Descripción;Monto\n16/10/2026;Sueldo;1500000,00\n17/10/2026;Supermercado Carrefour;-85000,50\n" +
            "19/10/2026;Farmacia;-12000,00\n20/10/2026;Kiosco;-1500,00\n"));

        Assert.Equal(2, Count(next, ImportStatus.New));
        Assert.Equal(2, Count(next, ImportStatus.Duplicate));
        Assert.True(next.RunImport());
        Assert.Equal(6, TransactionCount());
    }

    [Fact]
    public void El_mismo_archivo_en_otra_cuenta_no_es_un_duplicado()
    {
        var path = WriteFile("a.csv", Extracto);
        Open(path).RunImport();
        var otra = AddAccount("Banco");

        var vm = Open(path, otra);

        Assert.Equal(4, Count(vm, ImportStatus.New));
    }

    [Fact]
    public void Una_descripcion_de_mas_de_200_caracteres_tambien_se_reconoce_al_re_importar()
    {
        var path = WriteFile("larga.csv", "Fecha;Descripción;Monto\n18/10/2026;" + new string('x', 300) + ";-100,00\n");
        Open(path).RunImport();

        var again = Open(path);

        Assert.Equal(1, Count(again, ImportStatus.Duplicate));
        Assert.Equal(0, Count(again, ImportStatus.New));
    }

    [Fact]
    public void Un_movimiento_cargado_a_mano_con_la_misma_descripcion_se_reconoce()
    {
        AddTransaction(FirstAccountId(), TransactionType.Gasto, 7000m, new DateTime(2026, 10, 21), "Cine");

        var vm = Open(WriteFile("c.csv", "Fecha;Descripción;Monto\n21/10/2026;CINE;-7000,00\n"));

        Assert.Equal(1, Count(vm, ImportStatus.Duplicate));
    }

    [Fact]
    public void Sin_omitir_repetidos_permite_importarlos_igual()
    {
        var path = WriteFile("a.csv", Extracto);
        Open(path).RunImport();
        var vm = Open(path);

        vm.SkipDuplicates = false;

        Assert.True(vm.CanImport);
        Assert.True(vm.RunImport());
        Assert.Equal(8, TransactionCount());
    }

    [Fact]
    public void Las_filas_con_error_se_omiten_y_las_buenas_se_importan()
    {
        var vm = Open(WriteFile("e.csv", "Fecha;Descripción;Monto\n15/10/2026;Bien;-10,00\nfecha rota;Mal;-10,00\n"));

        Assert.Equal(1, Count(vm, ImportStatus.New));
        Assert.Equal(1, Count(vm, ImportStatus.Error));
        Assert.True(vm.RunImport());
        Assert.Equal(1, TransactionCount());
    }

    [Fact]
    public void La_categoria_sale_de_una_regla_y_si_no_hay_de_la_categoria_por_defecto()
    {
        using (var db = new AppDbContext())
        {
            var supermercado = db.Categories.First(c => c.Name == "Supermercado").Id;
            CategoryRuleService.Save(db, "carrefour", supermercado);
        }

        Open(WriteFile("r.csv", "Fecha;Descripción;Monto\n17/10/2026;Compra Carrefour;-100,00\n18/10/2026;Algo raro;-50,00\n")).RunImport();

        using var check = new AppDbContext();
        var byDescription = check.Transactions.Include(t => t.Category).ToDictionary(t => t.Description);
        Assert.Equal("Supermercado", byDescription["Compra Carrefour"].Category.Name);
        Assert.Equal("Otros gastos", byDescription["Algo raro"].Category.Name);
    }

    // ───────────── Excel ─────────────

    private string ExcelExtracto()
    {
        var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Extracto");
        ws.Cell("A1").Value = "Extracto de cuenta";
        ws.Cell("A2").Value = "Titular: Prueba";
        string[] headers = ["Fecha", "Descripción", "Débito", "Crédito", "Saldo"];
        for (var i = 0; i < headers.Length; i++) ws.Cell(4, i + 1).Value = headers[i];
        ws.Cell(5, 1).Value = new DateTime(2026, 10, 15, 14, 30, 0);
        ws.Cell(5, 2).Value = "Cafe Martinez";
        ws.Cell(5, 3).Value = 3500.5;
        ws.Cell(6, 1).Value = new DateTime(2026, 10, 16);
        ws.Cell(6, 2).Value = "Sueldo";
        ws.Cell(6, 4).Value = 1500000;
        ws.Cell(7, 1).Value = "17/10/2026";                  // fecha y monto como texto, formato argentino
        ws.Cell(7, 2).Value = "Supermercado Carrefour";
        ws.Cell(7, 3).Value = "85.000,50";
        var path = System.IO.Path.Combine(DataDir, "extracto.xlsx");
        wb.SaveAs(path);
        wb.Dispose();
        return path;
    }

    [Fact]
    public void Un_Excel_con_titulo_y_debito_credito_se_detecta_y_se_importa_al_centavo()
    {
        var vm = Open(ExcelExtracto());

        Assert.True(vm.IsExcel);
        Assert.False(vm.HasSeveralSheets);
        Assert.Equal("2", vm.SkipRowsText);                  // título y titular; la fila vacía no cuenta
        Assert.True(vm.HasHeader);
        Assert.True(vm.SplitColumns);
        Assert.Equal(3, Count(vm, ImportStatus.New));
        Assert.True(vm.RunImport());

        using var db = new AppDbContext();
        var saved = db.Transactions.OrderBy(t => t.Date).ToList();
        Assert.Equal([3500.50m, 1500000m, 85000.50m], saved.Select(t => t.Amount));
        Assert.Equal([TransactionType.Gasto, TransactionType.Ingreso, TransactionType.Gasto], saved.Select(t => t.Type));
    }

    [Fact]
    public void Re_importar_el_mismo_Excel_no_repite_y_un_CSV_con_lo_mismo_tambien_sale_repetido()
    {
        var path = ExcelExtracto();
        Open(path).RunImport();

        Assert.Equal(3, Count(Open(path), ImportStatus.Duplicate));

        var csv = Open(WriteFile("igual.csv",
            "Fecha;Descripción;Monto\n15/10/2026;Cafe Martinez;-3500,50\n16/10/2026;Sueldo;1500000,00\n17/10/2026;Supermercado Carrefour;-85000,50\n"));
        Assert.Equal(3, Count(csv, ImportStatus.Duplicate));
        Assert.True(csv.IsCsv);
    }

    [Fact]
    public void Un_Excel_con_varias_hojas_deja_elegir_y_recalcula_al_cambiar()
    {
        var wb = new XLWorkbook();
        wb.AddWorksheet("Resumen").Cell("A1").Value = "Solo un título";
        var datos = wb.AddWorksheet("Movimientos");
        datos.Cell(1, 1).Value = "Fecha"; datos.Cell(1, 2).Value = "Detalle"; datos.Cell(1, 3).Value = "Importe";
        datos.Cell(2, 1).Value = new DateTime(2026, 11, 2); datos.Cell(2, 2).Value = "Farmacia"; datos.Cell(2, 3).Value = -1200;
        datos.Cell(3, 1).Value = new DateTime(2026, 11, 3); datos.Cell(3, 2).Value = "Reintegro"; datos.Cell(3, 3).Value = 800;
        var path = System.IO.Path.Combine(DataDir, "hojas.xlsx");
        wb.SaveAs(path);
        wb.Dispose();

        var vm = Open(path);
        Assert.True(vm.HasSeveralSheets);
        Assert.Equal("Resumen", vm.SelectedSheet);

        vm.SelectedSheet = "Movimientos";

        Assert.Equal(2, Count(vm, ImportStatus.New));
        Assert.Contains(vm.Preview, r => r.TypeText == "Gasto");
        Assert.Contains(vm.Preview, r => r.TypeText == "Ingreso");
    }
}
