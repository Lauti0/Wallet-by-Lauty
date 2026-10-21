using System.Globalization;
using System.IO;
using CholinanzasApp.Models;
using CholinanzasApp.Services;

namespace CholinanzasApp.Tests;

/// <summary>El lector de extractos: formatos de fecha y de número, separadores, tabla dentro del archivo y duplicados.</summary>
public class CsvImportServiceTests
{
    private static decimal D(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);

    // ───────────── Fechas ─────────────

    [Theory]
    [InlineData("15/10/2026", DateStyle.DayFirst, 2026, 10, 15)]
    [InlineData("10/15/2026", DateStyle.MonthFirst, 2026, 10, 15)]
    [InlineData("2026-10-15", DateStyle.Auto, 2026, 10, 15)]
    [InlineData("2026-10-15", DateStyle.Iso, 2026, 10, 15)]
    [InlineData("15-10-2026", DateStyle.DayFirst, 2026, 10, 15)]
    [InlineData("15.10.2026", DateStyle.DayFirst, 2026, 10, 15)]
    [InlineData("2026-10-15T14:30:00", DateStyle.Auto, 2026, 10, 15)]
    [InlineData("15/10/2026 14:30", DateStyle.DayFirst, 2026, 10, 15)]
    [InlineData("15/10/26", DateStyle.DayFirst, 2026, 10, 15)]
    [InlineData("  15/10/2026  ", DateStyle.DayFirst, 2026, 10, 15)]
    public void Fechas_validas_se_leen(string text, DateStyle style, int year, int month, int day)
    {
        Assert.True(CsvImportService.TryParseDate(text, style, out var date));
        Assert.Equal(new DateTime(year, month, day), date);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("31/02/2026")]   // febrero no tiene 31
    [InlineData("32/01/2026")]
    [InlineData("15/13/2026")]
    [InlineData("15/10/1900")]   // año fuera de rango
    [InlineData("15/10/2200")]
    public void Fechas_invalidas_se_rechazan(string text)
    {
        Assert.False(CsvImportService.TryParseDate(text, DateStyle.DayFirst, out _));
    }

    [Fact]
    public void Con_formato_ISO_una_fecha_dia_mes_anio_no_se_acepta()
    {
        Assert.False(CsvImportService.TryParseDate("15/10/2026", DateStyle.Iso, out _));
    }

    [Fact]
    public void Dia_primero_o_mes_primero_se_detecta_por_un_valor_mayor_a_12()
    {
        Assert.Equal(DateStyle.DayFirst, CsvImportService.DetectDateStyle(["15/10/2026", "01/02/2026"]));
        Assert.Equal(DateStyle.MonthFirst, CsvImportService.DetectDateStyle(["10/15/2026", "01/02/2026"]));
        Assert.Equal(DateStyle.DayFirst, CsvImportService.DetectDateStyle(["01/02/2026"]));              // sin evidencia: día primero
        Assert.Equal(DateStyle.DayFirst, CsvImportService.DetectDateStyle(["15/10/2026", "10/15/2026"])); // evidencia cruzada
    }

    // ───────────── Montos ─────────────

    [Theory]
    [InlineData("1.234,56", DecimalStyle.CommaDecimal, "1234.56")]
    [InlineData("1,234.56", DecimalStyle.DotDecimal, "1234.56")]
    [InlineData("3500,50", DecimalStyle.CommaDecimal, "3500.50")]
    [InlineData("-3500,50", DecimalStyle.CommaDecimal, "-3500.50")]
    [InlineData("3500,50-", DecimalStyle.CommaDecimal, "-3500.50")]     // signo atrás
    [InlineData("(100,00)", DecimalStyle.CommaDecimal, "-100.00")]      // negativo entre paréntesis
    [InlineData("$ 1.500,00", DecimalStyle.CommaDecimal, "1500.00")]
    [InlineData("US$ 20", DecimalStyle.CommaDecimal, "20")]
    [InlineData("ARS 10,5", DecimalStyle.CommaDecimal, "10.5")]
    [InlineData("+5", DecimalStyle.CommaDecimal, "5")]
    [InlineData("1500000", DecimalStyle.CommaDecimal, "1500000")]
    public void Montos_validos_se_leen(string text, DecimalStyle style, string expected)
    {
        Assert.True(CsvImportService.TryParseAmount(text, style, out var value));
        Assert.Equal(D(expected), value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("-")]
    [InlineData("$")]
    public void Montos_invalidos_se_rechazan(string text)
    {
        Assert.False(CsvImportService.TryParseAmount(text, DecimalStyle.CommaDecimal, out _));
    }

    [Fact]
    public void Coma_o_punto_decimal_se_detecta_por_los_montos()
    {
        Assert.Equal(DecimalStyle.CommaDecimal, CsvImportService.DetectDecimalStyle(["1.234,56", "10,5"]));
        Assert.Equal(DecimalStyle.DotDecimal, CsvImportService.DetectDecimalStyle(["1,234.56", "10.5"]));
        Assert.Equal(DecimalStyle.CommaDecimal, CsvImportService.DetectDecimalStyle([]));       // sin evidencia: coma
        Assert.Equal(DecimalStyle.CommaDecimal, CsvImportService.DetectDecimalStyle(["3500"]));
    }

    // ───────────── Separador y partición en filas ─────────────

    [Theory]
    [InlineData("a;b;c\n1;2;3\n4;5;6", ';')]
    [InlineData("a,b,c\n1,2,3\n4,5,6", ',')]
    [InlineData("a\tb\tc\n1\t2\t3", '\t')]
    [InlineData("a|b|c\n1|2|3", '|')]
    [InlineData("\"a;x\",b,c\n\"1;2\",3,4", ',')]   // el ";" entre comillas no cuenta como separador
    public void Detecta_el_separador(string text, char expected)
    {
        Assert.Equal(expected, CsvImportService.DetectDelimiter(text));
    }

    [Fact]
    public void Parse_maneja_comillas_comillas_dobles_y_saltos_de_linea_dentro_de_comillas()
    {
        var rows = CsvImportService.Parse("a;\"b;c\";\"d\"\"e\"\n\"x\ny\";2;3\n", ';');

        Assert.Equal(2, rows.Count);
        Assert.Equal(["a", "b;c", "d\"e"], rows[0]);
        Assert.Equal(["x\ny", "2", "3"], rows[1]);
    }

    [Fact]
    public void Parse_ignora_las_lineas_vacias_y_acepta_saltos_de_linea_de_Windows()
    {
        var rows = CsvImportService.Parse("a;b\r\n\r\nc;d\r\n", ';');
        Assert.Equal(2, rows.Count);
        Assert.Equal(["c", "d"], rows[1]);
    }

    [Fact]
    public void Parse_recorta_los_campos_larguisimos()
    {
        var rows = CsvImportService.Parse(new string('x', 900) + ";b", ';');
        Assert.Equal(CsvImportService.MaxFieldLength, rows[0][0].Length);
    }

    [Fact]
    public void Parse_rechaza_mas_columnas_de_las_permitidas()
    {
        var line = string.Join(';', Enumerable.Repeat("x", CsvImportService.MaxColumns + 1));
        Assert.Throws<InvalidDataException>(() => CsvImportService.Parse(line, ';'));
    }

    [Fact]
    public void Parse_rechaza_mas_filas_de_las_permitidas()
    {
        var text = string.Join('\n', Enumerable.Repeat("a;b", CsvImportService.MaxRows + 1));
        Assert.Throws<InvalidDataException>(() => CsvImportService.Parse(text, ';'));
    }

    // ───────────── Dónde empieza la tabla y qué es cada columna ─────────────

    [Fact]
    public void Salta_los_datos_de_la_cuenta_que_el_banco_pone_antes_de_la_tabla()
    {
        List<string[]> rows =
        [
            ["Extracto de cuenta"],
            ["Titular", "Prueba"],
            ["Fecha", "Descripción", "Monto"],
            ["15/10/2026", "Cafe", "-3500,50"],
            ["16/10/2026", "Sueldo", "1500000,00"]
        ];

        var (skip, header) = CsvImportService.GuessLayout(rows);

        Assert.Equal(2, skip);
        Assert.True(header);
    }

    [Fact]
    public void Las_filas_de_distinto_largo_no_confunden_el_inicio_de_la_tabla()
    {
        // como en un Excel: un gasto no tiene crédito, así que su fila es más corta que la de un ingreso
        List<string[]> rows =
        [
            ["Extracto de cuenta"],
            ["Fecha", "Descripción", "Débito", "Crédito", "Saldo"],
            ["15/10/2026", "Cafe", "3500,50"],
            ["16/10/2026", "Sueldo", "", "1500000"],
            ["17/10/2026", "Super", "85000,50"]
        ];

        var (skip, header) = CsvImportService.GuessLayout(rows);

        Assert.Equal(1, skip);
        Assert.True(header);
    }

    [Fact]
    public void Una_fila_suelta_con_fecha_y_monto_antes_de_los_titulos_no_se_toma_por_el_inicio_de_la_tabla()
    {
        List<string[]> rows =
        [
            ["Saldo al", "30/09/2026", "", "100000,00"],          // parece un movimiento, pero la que sigue no
            ["Fecha", "Descripción", "Monto"],
            ["15/10/2026", "Cafe", "-3500,50"],
            ["16/10/2026", "Sueldo", "1500000,00"]
        ];

        var (skip, header) = CsvImportService.GuessLayout(rows);

        Assert.Equal(1, skip);
        Assert.True(header);
    }

    [Fact]
    public void Una_sola_fila_de_datos_sin_titulos_alcanza()
    {
        var (skip, header) = CsvImportService.GuessLayout([["15/10/2026", "Cafe", "-3500,50"]]);

        Assert.Equal(0, skip);
        Assert.False(header);
    }

    [Fact]
    public void Sin_ninguna_fila_que_parezca_un_movimiento_usa_el_largo_mas_comun()
    {
        List<string[]> rows = [["Titulo"], ["a", "b", "c"], ["d", "e", "f"]];

        var (skip, _) = CsvImportService.GuessLayout(rows);

        Assert.Equal(1, skip);
    }

    [Fact]
    public void Sin_titulos_detecta_que_la_primera_fila_ya_es_un_dato()
    {
        List<string[]> rows = [["15/10/2026", "Cafe", "-3500,50"], ["16/10/2026", "Sueldo", "1500000,00"]];

        var (skip, header) = CsvImportService.GuessLayout(rows);

        Assert.Equal(0, skip);
        Assert.False(header);
    }

    [Fact]
    public void Reconoce_las_columnas_por_el_nombre_del_encabezado()
    {
        var guess = CsvImportService.GuessColumns(["Fecha", "Descripción", "Débito", "Crédito", "Saldo"],
            [["15/10/2026", "Cafe", "3500,50", "", "100"]]);

        Assert.Equal(0, guess.Date);
        Assert.Equal(1, guess.Description);
        Assert.Equal(2, guess.Debit);
        Assert.Equal(3, guess.Credit);
        Assert.True(guess.Split);
    }

    [Fact]
    public void Reconoce_un_solo_monto_por_el_nombre_del_encabezado()
    {
        var guess = CsvImportService.GuessColumns(["Fecha", "Detalle", "Importe"], [["15/10/2026", "Cafe", "-3500,50"]]);

        Assert.Equal(0, guess.Date);
        Assert.Equal(1, guess.Description);
        Assert.Equal(2, guess.Amount);
        Assert.False(guess.Split);
    }

    [Fact]
    public void Sin_encabezado_reconoce_las_columnas_por_su_contenido()
    {
        List<string[]> data =
        [
            ["15/10/2026", "Cafe Martinez", "-3500,50"],
            ["16/10/2026", "Sueldo de octubre", "1500000,00"],
            ["17/10/2026", "Supermercado Carrefour", "-85000,50"]
        ];

        var guess = CsvImportService.GuessColumns(null, data);

        Assert.Equal(0, guess.Date);
        Assert.Equal(2, guess.Amount);
        Assert.Equal(1, guess.Description);
    }

    // ───────────── Convertir filas en movimientos ─────────────

    private static ImportOptions Single() => new() { DateColumn = 0, DescriptionColumn = 1, AmountColumn = 2 };

    private static readonly IReadOnlyDictionary<DuplicateKey, int> NoExisting = new Dictionary<DuplicateKey, int>();

    [Fact]
    public void Gasto_negativo_ingreso_positivo_y_monto_siempre_positivo()
    {
        var rows = CsvImportService.Evaluate(
            [["15/10/2026", "Cafe", "-3500,50"], ["16/10/2026", "Sueldo", "1500000,00"]], Single(), 5, NoExisting);

        Assert.Equal(TransactionType.Gasto, rows[0].Type);
        Assert.Equal(3500.50m, rows[0].Amount);
        Assert.Equal(TransactionType.Ingreso, rows[1].Type);
        Assert.Equal(1500000m, rows[1].Amount);
        Assert.All(rows, r => Assert.Equal(ImportStatus.New, r.Status));
    }

    [Fact]
    public void Invertir_el_signo_da_vuelta_gastos_e_ingresos()
    {
        var options = Single();
        options.InvertSign = true;

        var rows = CsvImportService.Evaluate([["15/10/2026", "Cafe", "3500,50"]], options, 1, NoExisting);

        Assert.Equal(TransactionType.Gasto, rows[0].Type);
    }

    [Fact]
    public void Las_filas_con_problemas_quedan_como_error_con_su_numero_de_linea()
    {
        var rows = CsvImportService.Evaluate(
        [
            ["15/10/2026", "Bien", "-10,00"],
            ["no es fecha", "Mala fecha", "-10,00"],
            ["16/10/2026", "Monto cero", "0"],
            ["17/10/2026", "Sin monto", ""],
            ["18/10/2026", "Monto raro", "abc"],
            ["19/10/2026", "Enorme", "2000000000000"]
        ], Single(), firstLine: 10, NoExisting);

        Assert.Equal(ImportStatus.New, rows[0].Status);
        Assert.Equal([ImportStatus.Error, ImportStatus.Error, ImportStatus.Error, ImportStatus.Error, ImportStatus.Error],
            rows.Skip(1).Select(r => r.Status));
        Assert.Equal([11, 12, 13, 14, 15], rows.Skip(1).Select(r => r.Line));
        Assert.Contains("Fecha no válida", rows[1].Error);
        Assert.Contains("cero", rows[2].Error);
        Assert.Contains("grande", rows[5].Error);
    }

    [Fact]
    public void Una_descripcion_vacia_queda_como_Importado()
    {
        var rows = CsvImportService.Evaluate([["15/10/2026", "", "-5,00"]], Single(), 1, NoExisting);
        Assert.Equal("Importado", rows[0].Description);
    }

    [Fact]
    public void La_descripcion_se_recorta_a_200_caracteres()
    {
        var rows = CsvImportService.Evaluate([["15/10/2026", new string('x', 300), "-5,00"]], Single(), 1, NoExisting);
        Assert.Equal(CsvImportService.MaxDescriptionLength, rows[0].Description.Length);
    }

    [Fact]
    public void Debito_y_credito_en_columnas_separadas()
    {
        var options = new ImportOptions { DateColumn = 0, DescriptionColumn = 1, DebitColumn = 2, CreditColumn = 3, SplitColumns = true };

        var rows = CsvImportService.Evaluate(
        [
            ["15/10/2026", "Solo débito", "100,00", ""],
            ["16/10/2026", "Solo crédito", "", "50,00"],
            ["17/10/2026", "Los dos", "30,00", "100,00"]
        ], options, 1, NoExisting);

        Assert.Equal((TransactionType.Gasto, 100m), (rows[0].Type, rows[0].Amount));
        Assert.Equal((TransactionType.Ingreso, 50m), (rows[1].Type, rows[1].Amount));
        Assert.Equal((TransactionType.Ingreso, 70m), (rows[2].Type, rows[2].Amount)); // 100 - 30
    }

    [Fact]
    public void Lee_el_formato_de_fecha_y_de_numero_que_traiga_el_archivo()
    {
        // mes/día/año y punto decimal, detectados solos
        var rows = CsvImportService.Evaluate([["10/15/2026", "Coffee", "-1,234.50"], ["10/16/2026", "Pay", "2,000.00"]],
            Single(), 1, NoExisting);

        Assert.Equal(new DateTime(2026, 10, 15), rows[0].Date);
        Assert.Equal(1234.50m, rows[0].Amount);
        Assert.Equal(2000m, rows[1].Amount);
    }

    private static DuplicateKey Key(string description, decimal amount = 3500.50m, TransactionType type = TransactionType.Gasto) =>
        new(new DateTime(2026, 10, 15), type, amount, description);

    [Fact]
    public void Cada_movimiento_existente_absorbe_una_sola_fila_igual_del_archivo()
    {
        List<string[]> twoCoffees = [["15/10/2026", "Cafe", "-3500,50"], ["15/10/2026", "Cafe", "-3500,50"]];

        var one = CsvImportService.Evaluate(twoCoffees, Single(), 1, new Dictionary<DuplicateKey, int> { [Key("cafe")] = 1 });
        var two = CsvImportService.Evaluate(twoCoffees, Single(), 1, new Dictionary<DuplicateKey, int> { [Key("cafe")] = 2 });
        var none = CsvImportService.Evaluate(twoCoffees, Single(), 1, NoExisting);

        Assert.Equal([ImportStatus.Duplicate, ImportStatus.New], one.Select(r => r.Status));
        Assert.Equal([ImportStatus.Duplicate, ImportStatus.Duplicate], two.Select(r => r.Status));
        Assert.Equal([ImportStatus.New, ImportStatus.New], none.Select(r => r.Status));
    }

    [Fact]
    public void Un_duplicado_ignora_mayusculas_acentos_y_espacios_de_mas()
    {
        var existing = CsvImportService.BuildExistingMap(
        [
            new FinanceTransaction { Date = new DateTime(2026, 10, 15), Type = TransactionType.Gasto, Amount = 3500.50m, Description = "  CAFÉ   Martínez " }
        ]);

        var rows = CsvImportService.Evaluate([["15/10/2026", "cafe martinez", "-3500,50"]], Single(), 1, existing);

        Assert.Equal(ImportStatus.Duplicate, rows[0].Status);
    }

    [Theory]
    [InlineData("16/10/2026", "Cafe", "-3500,50")]   // otra fecha
    [InlineData("15/10/2026", "Cafe", "-3500,51")]   // otro monto
    [InlineData("15/10/2026", "Cafe", "3500,50")]    // otro tipo (ingreso)
    [InlineData("15/10/2026", "Te", "-3500,50")]     // otra descripción
    public void Si_cambia_algo_no_es_un_duplicado(string date, string description, string amount)
    {
        var existing = new Dictionary<DuplicateKey, int> { [Key("cafe")] = 1 };

        var rows = CsvImportService.Evaluate([[date, description, amount]], Single(), 1, existing);

        Assert.Equal(ImportStatus.New, rows[0].Status);
    }

    [Fact]
    public void Normalize_deja_minusculas_sin_acentos_y_con_espacios_simples()
    {
        Assert.Equal("cafe martinez", CsvImportService.Normalize("  CAFÉ   Martínez "));
        Assert.Equal("ano nuevo", CsvImportService.Normalize("AÑO\tNuevo"));   // la eñe pierde la tilde: se compara sin ella
    }
}
