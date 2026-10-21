using System.IO;
using System.IO.Compression;
using ClosedXML.Excel;
using CholinanzasApp.Services;

namespace CholinanzasApp.Tests;

/// <summary>Lectura de libros de Excel (.xlsx): hojas, tipos de celda, formatos mezclados y archivos hostiles.</summary>
public class ExcelImportTests : DatabaseTest
{
    private static string Save(XLWorkbook workbook, string name)
    {
        var path = Path.Combine(DataDir, name);
        workbook.SaveAs(path);
        workbook.Dispose();
        return path;
    }

    [Fact]
    public void Lee_texto_numeros_y_fechas_de_Excel_con_la_fecha_como_anio_mes_dia()
    {
        var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Hoja");
        ws.Cell(1, 1).Value = new DateTime(2026, 10, 15, 14, 30, 0);   // con hora: solo se conserva el día
        ws.Cell(1, 2).Value = "  Cafe  ";                                // se recortan los espacios
        ws.Cell(1, 3).Value = -3500.5;
        ws.Cell(1, 4).Value = 1500000;                                   // entero: sin decimales
        var path = Save(wb, "tipos.xlsx");

        var row = ExcelImportService.ReadRows(path, "Hoja").Single();

        Assert.Equal("2026-10-15", row[0]);
        Assert.Equal("Cafe", row[1]);
        Assert.Equal("-3500.50", row[2]);
        Assert.Equal("1500000", row[3]);
    }

    [Fact]
    public void Los_montos_con_mas_de_dos_decimales_se_redondean_a_dos()
    {
        var wb = new XLWorkbook();
        wb.AddWorksheet("Hoja").Cell(1, 1).Value = 12345.678;

        Assert.Equal("12345.68", ExcelImportService.ReadRows(Save(wb, "dec.xlsx"), "Hoja")[0][0]);
    }

    [Fact]
    public void Si_la_hoja_guarda_numeros_como_texto_con_coma_los_numeros_de_Excel_salen_con_coma()
    {
        // si se mezclaran estilos, el importador elegiría uno para todos y leería mal "85.000,50"
        var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Hoja");
        ws.Cell(1, 1).Value = "85.000,50";
        ws.Cell(2, 1).Value = 3500.5;
        var rows = ExcelImportService.ReadRows(Save(wb, "mezcla.xlsx"), "Hoja");

        Assert.Equal("85.000,50", rows[0][0]);
        Assert.Equal("3500,50", rows[1][0]);
    }

    [Fact]
    public void Si_la_hoja_guarda_numeros_como_texto_con_punto_los_numeros_de_Excel_salen_con_punto()
    {
        var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Hoja");
        ws.Cell(1, 1).Value = "1,234.50";
        ws.Cell(2, 1).Value = 3500.5;
        var rows = ExcelImportService.ReadRows(Save(wb, "mezcla2.xlsx"), "Hoja");

        Assert.Equal("3500.50", rows[1][0]);
    }

    [Fact]
    public void Una_fila_llega_hasta_su_ultima_celda_con_contenido_y_las_filas_vacias_no_cuentan()
    {
        var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Hoja");
        ws.Cell(1, 1).Value = "Titulo";             // una sola celda
        ws.Cell(3, 1).Value = "a";                  // la fila 2 queda vacía
        ws.Cell(3, 3).Value = "c";                  // hueco en la columna 2

        var rows = ExcelImportService.ReadRows(Save(wb, "forma.xlsx"), "Hoja");

        Assert.Equal(2, rows.Count);
        Assert.Equal(["Titulo"], rows[0]);
        Assert.Equal(["a", "", "c"], rows[1]);
    }

    [Fact]
    public void Solo_ofrece_las_hojas_visibles_que_tienen_datos()
    {
        var wb = new XLWorkbook();
        wb.AddWorksheet("Resumen").Cell("A1").Value = "x";
        wb.AddWorksheet("Movimientos").Cell("A1").Value = "y";
        var oculta = wb.AddWorksheet("Oculta");
        oculta.Cell("A1").Value = "secreto";
        oculta.Visibility = XLWorksheetVisibility.Hidden;
        wb.AddWorksheet("Vacia");

        Assert.Equal(["Resumen", "Movimientos"], ExcelImportService.GetSheetNames(Save(wb, "hojas.xlsx")));
    }

    [Fact]
    public void Un_texto_que_parece_formula_se_lee_como_texto_sin_evaluarse()
    {
        var wb = new XLWorkbook();
        var cell = wb.AddWorksheet("F").Cell(1, 1);
        cell.Style.NumberFormat.Format = "@";
        cell.Value = "=HYPERLINK(\"http://malo\",\"x\")";

        var row = ExcelImportService.ReadRows(Save(wb, "formula.xlsx"), "F")[0];

        Assert.Equal("=HYPERLINK(\"http://malo\",\"x\")", row[0]);
    }

    [Fact]
    public void Una_celda_larguisima_se_recorta()
    {
        var wb = new XLWorkbook();
        wb.AddWorksheet("Hoja").Cell(1, 1).Value = new string('a', 900);

        Assert.Equal(CsvImportService.MaxFieldLength, ExcelImportService.ReadRows(Save(wb, "larga.xlsx"), "Hoja")[0][0].Length);
    }

    // ───────────── Archivos problemáticos ─────────────

    [Fact]
    public void Un_archivo_que_no_es_Excel_se_rechaza_con_un_mensaje_claro()
    {
        var path = WriteFile("falso.xlsx", "esto no es un excel");

        var error = Assert.Throws<InvalidDataException>(() => ExcelImportService.GetSheetNames(path));

        Assert.Contains("No se pudo leer el archivo Excel", error.Message);
    }

    [Fact]
    public void Un_zip_que_declara_demasiado_espacio_descomprimido_se_rechaza()
    {
        var path = Path.Combine(DataDir, "bomba.xlsx");
        using (var file = File.Create(path))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        using (var entry = zip.CreateEntry("relleno.bin", CompressionLevel.SmallestSize).Open())
        {
            var chunk = new byte[1024 * 1024];
            for (var i = 0; i < 150; i++) entry.Write(chunk); // 150 MB que comprimen a unos pocos KB
        }

        var error = Assert.Throws<InvalidDataException>(() => ExcelImportService.GetSheetNames(path));

        Assert.Contains("demasiado grande", error.Message);
    }

    [Fact]
    public void Una_hoja_con_demasiadas_filas_se_rechaza()
    {
        var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Larga");
        ws.Cell(1, 1).Value = "Fecha";
        ws.Cell(CsvImportService.MaxRows + 10, 1).Value = "x";
        var path = Save(wb, "filas.xlsx");

        Assert.Throws<InvalidDataException>(() => ExcelImportService.ReadRows(path, "Larga"));
    }

    [Fact]
    public void Una_hoja_con_demasiadas_columnas_se_rechaza()
    {
        var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Ancha");
        ws.Cell(1, 1).Value = "Fecha";
        ws.Cell(1, CsvImportService.MaxColumns + 10).Value = "x";
        var path = Save(wb, "columnas.xlsx");

        Assert.Throws<InvalidDataException>(() => ExcelImportService.ReadRows(path, "Ancha"));
    }

    [Fact]
    public void Pedir_una_hoja_que_no_existe_da_un_error_claro()
    {
        var wb = new XLWorkbook();
        wb.AddWorksheet("Hoja").Cell(1, 1).Value = "x";
        var path = Save(wb, "una.xlsx");

        Assert.Throws<InvalidDataException>(() => ExcelImportService.ReadRows(path, "No existe"));
    }
}
