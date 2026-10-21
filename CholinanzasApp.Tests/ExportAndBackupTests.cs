using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using CholinanzasApp.Data;
using CholinanzasApp.Models;
using CholinanzasApp.Services;

namespace CholinanzasApp.Tests;

/// <summary>Lo que la app escribe hacia afuera: exportaciones (sin inyección de fórmulas) y respaldos (sin tocar archivos ajenos).</summary>
public class ExportTests : DatabaseTest
{
    // ───────────── CSV ─────────────

    [Fact]
    public void El_CSV_tiene_encabezado_BOM_fecha_argentina_y_gastos_en_negativo()
    {
        AddTransaction(FirstAccountId(), TransactionType.Gasto, 3500.5m, new DateTime(2026, 10, 15), "Cafe");
        AddTransaction(FirstAccountId(), TransactionType.Ingreso, 1500000m, new DateTime(2026, 10, 16), "Sueldo");
        var path = Path.Combine(DataDir, "m.csv");

        ExportService.ExportMovementsCsv(path);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3));                       // BOM: Excel en español ve bien los acentos
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        Assert.Equal("﻿Fecha;Tipo;Categoría;Cuenta;Moneda;Descripción;Monto".TrimStart('﻿'), lines[0].TrimStart('﻿'));
        Assert.Contains(lines, l => l.StartsWith("15/10/2026;Gasto;") && l.EndsWith(";Cafe;-3500,50"));
        Assert.Contains(lines, l => l.StartsWith("16/10/2026;Ingreso;") && l.EndsWith(";Sueldo;1500000,00"));
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://malo\",\"x\")")]
    [InlineData("+cmd|' /C calc'!A0")]
    [InlineData("-2+3")]
    [InlineData("@SUM(1+1)")]
    [InlineData("\t=1+1")]
    [InlineData("\r=1+1")]
    public void El_CSV_neutraliza_el_texto_que_Excel_interpretaria_como_formula(string description)
    {
        AddTransaction(FirstAccountId(), TransactionType.Gasto, 1m, new DateTime(2026, 10, 15), description);
        var path = Path.Combine(DataDir, "f.csv");

        ExportService.ExportMovementsCsv(path);

        // se lee el archivo con el mismo lector de CSV de la app: el campo de la descripción (el 6.º) tiene que quedar con una
        // comilla simple adelante, que es lo que impide que Excel lo tome por una fórmula
        var rows = CsvImportService.Parse(File.ReadAllText(path, Encoding.UTF8).TrimStart('﻿'), ';');
        Assert.Equal(2, rows.Count);                         // encabezado y un movimiento
        Assert.Equal("'" + description, rows[1][5]);
    }

    [Fact]
    public void El_CSV_encierra_entre_comillas_lo_que_tiene_punto_y_coma_comillas_o_saltos_de_linea()
    {
        AddTransaction(FirstAccountId(), TransactionType.Gasto, 1m, new DateTime(2026, 10, 15), "a;b \"c\"\nd");
        var path = Path.Combine(DataDir, "q.csv");

        ExportService.ExportMovementsCsv(path);

        Assert.Contains("\"a;b \"\"c\"\"\nd\"", File.ReadAllText(path, Encoding.UTF8));
    }

    [Fact]
    public void Lo_exportado_a_CSV_se_puede_volver_a_importar_sin_duplicar()
    {
        AddTransaction(FirstAccountId(), TransactionType.Gasto, 3500.5m, new DateTime(2026, 10, 15), "Cafe Martinez");
        AddTransaction(FirstAccountId(), TransactionType.Ingreso, 1500000m, new DateTime(2026, 10, 16), "Sueldo");
        var path = Path.Combine(DataDir, "ida-y-vuelta.csv");
        ExportService.ExportMovementsCsv(path);

        var import = new CholinanzasApp.ViewModels.ImportViewModel();
        import.LoadFile(path);

        Assert.Equal(2, import.Preview.Count(r => r.Status == ImportStatus.Duplicate));
    }

    // ───────────── Excel ─────────────

    [Fact]
    public void El_Excel_exportado_nunca_contiene_formulas_aunque_las_descripciones_lo_parezcan()
    {
        foreach (var evil in new[] { "=HYPERLINK(\"http://malo\",\"x\")", "+1+1", "-2+3", "@SUM(1)" })
            AddTransaction(FirstAccountId(), TransactionType.Gasto, 1m, new DateTime(2026, 10, 15), evil);
        var path = Path.Combine(DataDir, "e.xlsx");

        ExportService.ExportExcel(path);

        using var zip = ZipFile.OpenRead(path);
        var formulas = 0;
        foreach (var sheet in zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/") && e.Name.EndsWith(".xml")))
        {
            using var reader = new StreamReader(sheet.Open());
            formulas += Regex.Matches(reader.ReadToEnd(), "<f[ >]").Count;
        }
        Assert.Equal(0, formulas);
    }

    [Fact]
    public void El_Excel_exportado_se_puede_volver_a_leer_con_el_importador()
    {
        AddTransaction(FirstAccountId(), TransactionType.Gasto, 3500.5m, new DateTime(2026, 10, 15), "Cafe");
        var path = Path.Combine(DataDir, "e2.xlsx");

        ExportService.ExportExcel(path);

        var sheets = ExcelImportService.GetSheetNames(path);
        Assert.NotEmpty(sheets);
    }
}

public class BackupTests : DatabaseTest
{
    private static string NewName(DateTime moment) => $"finanzas-{moment:yyyyMMdd-HHmmss}.db";

    private static string FakeBackup(string folder, DateTime moment)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, NewName(moment));
        File.WriteAllText(path, "respaldo falso");
        File.SetLastWriteTime(path, moment);
        return path;
    }

    private static int TransactionCount()
    {
        using var db = new AppDbContext();
        return db.Transactions.Count();
    }

    [Fact]
    public void Un_respaldo_tiene_el_nombre_esperado_y_es_una_base_valida()
    {
        var backup = BackupService.CreateBackup();

        Assert.Matches(@"^finanzas-\d{8}-\d{6}\.db$", Path.GetFileName(backup.Path));
        Assert.True(File.Exists(backup.Path));
        Assert.Null(BackupService.LastError);
    }

    [Fact]
    public void El_respaldo_automatico_se_hace_una_sola_vez_por_dia()
    {
        BackupService.RunAutomatic();
        var first = BackupService.List().Count;
        BackupService.RunAutomatic();

        Assert.Equal(1, first);
        Assert.Single(BackupService.List());
    }

    [Fact]
    public void La_lista_solo_incluye_archivos_con_el_nombre_exacto_de_los_respaldos()
    {
        FakeBackup(BackupService.BackupDir, new DateTime(2026, 1, 1, 10, 0, 0));
        File.WriteAllText(Path.Combine(BackupService.BackupDir, "finanzas-mio.db"), "no soy un respaldo");
        File.WriteAllText(Path.Combine(BackupService.BackupDir, "finanzas-2026.db"), "tampoco");

        var list = BackupService.List();

        Assert.Single(list);
        Assert.EndsWith("finanzas-20260101-100000.db", list[0].Path);
    }

    [Fact]
    public void Se_conservan_solo_los_30_respaldos_mas_nuevos()
    {
        var start = new DateTime(2026, 1, 1, 10, 0, 0);
        for (var i = 0; i < 35; i++) FakeBackup(BackupService.BackupDir, start.AddDays(i));

        BackupService.CreateBackup();   // al terminar, limpia los más viejos

        var list = BackupService.List();
        Assert.Equal(BackupService.KeepCount, list.Count);
        Assert.DoesNotContain(list, b => b.Path.EndsWith(NewName(start)));           // el más viejo se fue
    }

    [Fact]
    public void La_carpeta_extra_recibe_los_respaldos_y_no_se_toca_ningun_archivo_ajeno()
    {
        var extra = Path.Combine(DataDir, "extra");
        Directory.CreateDirectory(extra);
        for (var i = 0; i < 35; i++) File.WriteAllText(Path.Combine(extra, $"finanzas-familia-{i:00}.db"), "archivo personal");
        File.WriteAllText(Path.Combine(extra, "otra-cosa.txt"), "x");
        AppSettings.ExtraBackupFolder = extra;

        BackupService.CreateBackup();

        Assert.Equal(35, Directory.GetFiles(extra, "finanzas-familia-*.db").Length);     // ninguno se borró
        Assert.True(File.Exists(Path.Combine(extra, "otra-cosa.txt")));
        Assert.Single(Directory.GetFiles(extra, "finanzas-????????-??????.db"));         // y llegó el respaldo
    }

    [Fact]
    public void La_carpeta_extra_tambien_conserva_solo_los_30_respaldos_propios_mas_nuevos()
    {
        var extra = Path.Combine(DataDir, "extra2");
        var start = new DateTime(2026, 1, 1, 10, 0, 0);
        for (var i = 0; i < 35; i++) FakeBackup(extra, start.AddDays(i));
        AppSettings.ExtraBackupFolder = extra;

        BackupService.CreateBackup();

        Assert.Equal(BackupService.KeepCount, Directory.GetFiles(extra, "finanzas-????????-??????.db").Length);
    }

    [Fact]
    public void Una_carpeta_extra_que_no_se_puede_usar_no_pierde_el_respaldo_local()
    {
        // una "carpeta" que en realidad es un archivo: crear la carpeta falla
        var notAFolder = WriteFile("no-es-carpeta", "x");
        AppSettings.ExtraBackupFolder = Path.Combine(notAFolder, "sub");

        var backup = BackupService.CreateBackup();

        Assert.True(File.Exists(backup.Path));
        Assert.NotNull(BackupService.LastError);
    }

    [Fact]
    public void Restaurar_devuelve_los_datos_del_respaldo_y_antes_guarda_lo_actual()
    {
        var account = FirstAccountId();
        AddTransaction(account, TransactionType.Gasto, 100m, new DateTime(2026, 10, 15), "Antes del respaldo");
        var backup = BackupService.CreateBackup();

        AddTransaction(account, TransactionType.Gasto, 200m, new DateTime(2026, 10, 16), "Después");
        Assert.Equal(2, TransactionCount());
        var backupsBefore = BackupService.List().Count;
        Thread.Sleep(1100);   // los respaldos se nombran por segundo

        BackupService.Restore(backup.Path);

        Assert.Equal(1, TransactionCount());
        Assert.True(BackupService.List().Count > backupsBefore);   // la red de seguridad
    }

    [Fact]
    public void Un_respaldo_dañado_no_se_restaura_y_los_datos_quedan_intactos()
    {
        AddTransaction(FirstAccountId(), TransactionType.Gasto, 100m, new DateTime(2026, 10, 15), "Mi dato");
        Directory.CreateDirectory(BackupService.BackupDir);
        var broken = Path.Combine(BackupService.BackupDir, NewName(new DateTime(2020, 1, 1, 1, 1, 1)));
        File.WriteAllText(broken, "esto no es una base de datos SQLite, solo texto");

        Assert.ThrowsAny<Exception>(() => BackupService.Restore(broken));

        Assert.Equal(1, TransactionCount());
    }
}
