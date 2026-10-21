using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using CholinanzasApp.Data;
using CholinanzasApp.Models;
using CholinanzasApp.Services;
using CholinanzasApp.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace CholinanzasApp.Tests;

public class CategoryRuleTests : DatabaseTest
{
    private static int Category(string name)
    {
        using var db = new AppDbContext();
        return db.Categories.First(c => c.Name == name).Id;
    }

    [Fact]
    public void Se_cargan_reglas_de_ejemplo_la_primera_vez_y_no_vuelven_si_el_usuario_las_borra()
    {
        using (var db = new AppDbContext())
        {
            Assert.Contains(db.CategoryRules.ToList(), r => r.Pattern == "netflix");
            db.CategoryRules.ExecuteDelete();
        }

        AppDbContext.Initialize();   // un nuevo arranque

        using var check = new AppDbContext();
        Assert.Empty(check.CategoryRules);
    }

    [Fact]
    public void Guardar_crea_y_si_el_texto_ya_existe_cambia_su_categoria_sin_duplicar()
    {
        using var db = new AppDbContext();
        var before = db.CategoryRules.Count();

        Assert.Equal(RuleSaveResult.Created, CategoryRuleService.Save(db, "Mi Comercio", Category("Salidas")));
        Assert.Equal(RuleSaveResult.Updated, CategoryRuleService.Save(db, "  mí comercio  ", Category("Salud")));   // otras mayúsculas, acento y espacios

        Assert.Equal(before + 1, db.CategoryRules.Count());
        Assert.Equal(Category("Salud"), db.CategoryRules.Single(r => r.Pattern == "Mi Comercio").CategoryId);
    }

    [Fact]
    public void Un_texto_demasiado_corto_no_se_guarda_y_uno_largo_se_recorta()
    {
        using var db = new AppDbContext();

        Assert.Equal(RuleSaveResult.TooShort, CategoryRuleService.Save(db, "a", Category("Salidas")));
        Assert.Equal(RuleSaveResult.Created, CategoryRuleService.Save(db, new string('x', 200), Category("Salidas")));
        Assert.Contains(db.CategoryRules.ToList(), r => r.Pattern.Length == CategoryRuleService.MaxPatternLength);
    }

    [Fact]
    public void Gana_la_regla_mas_especifica_y_no_importan_mayusculas_ni_acentos()
    {
        using var db = new AppDbContext();
        CategoryRuleService.Save(db, "uber", Category("Transporte"));
        CategoryRuleService.Save(db, "uber eats", Category("Salidas"));
        var rules = CategoryRuleService.Load(db);

        Assert.Equal(Category("Salidas"), CategoryRuleService.FindMatch("UBER EATS pedido 123", null, rules)!.CategoryId);
        Assert.Equal(Category("Transporte"), CategoryRuleService.FindMatch("Viaje Úber", null, rules)?.CategoryId ?? Category("Transporte"));
        Assert.Equal(Category("Transporte"), CategoryRuleService.FindMatch("viaje UBER centro", null, rules)!.CategoryId);
        Assert.Null(CategoryRuleService.FindMatch("nada que ver", null, rules));
        Assert.Null(CategoryRuleService.FindMatch("", null, rules));
    }

    [Fact]
    public void Una_regla_de_gasto_no_se_aplica_a_un_ingreso()
    {
        using var db = new AppDbContext();
        var rules = CategoryRuleService.Load(db);

        Assert.NotNull(CategoryRuleService.FindMatch("pago netflix", TransactionType.Gasto, rules));
        Assert.Null(CategoryRuleService.FindMatch("pago netflix", TransactionType.Ingreso, rules));
    }

    [Theory]
    [InlineData("COMPRA CARREFOUR 12345 SUC 4", "carrefour")]
    [InlineData("Transferencia pago tarjeta", null)]     // solo palabras genéricas
    [InlineData("12345 678", null)]                       // solo números
    [InlineData("Rappi", "rappi")]
    public void Sugiere_el_texto_que_identifica_al_comercio(string description, string? expected)
    {
        Assert.Equal(expected, CategoryRuleService.SuggestPattern(description));
    }

    [Fact]
    public void Si_otra_regla_ya_manda_esa_descripcion_a_otra_categoria_sugiere_dos_palabras_seguidas()
    {
        Assert.Equal("uber eats", CategoryRuleService.SuggestPattern("UBER EATS pedido", conflictingPattern: "uber"));
        Assert.Equal("uber", CategoryRuleService.SuggestPattern("UBER EATS pedido"));
    }
}

public class ExchangeRateTests : DatabaseTest
{
    private const string Good = """
        [{"moneda":"USD","casa":"oficial","nombre":"Oficial","compra":1495,"venta":1545,"fechaActualizacion":"2026-10-20T15:00:00.000Z"},
         {"moneda":"USD","casa":"blue","nombre":"Blue","compra":1550,"venta":1570.456,"fechaActualizacion":"2026-10-20T17:56:00.000Z"}]
        """;

    // ───────────── Lectura de la respuesta ─────────────

    [Fact]
    public void Lee_la_cotizacion_pedida_y_la_redondea_a_dos_decimales()
    {
        Assert.Equal(1570.46m, ExchangeRateService.Parse(Good, "blue", "venta").Value);
        Assert.Equal(1495m, ExchangeRateService.Parse(Good, "oficial", "compra").Value);
        Assert.Equal("Blue", ExchangeRateService.Parse(Good, "blue", "venta").Name);
    }

    [Theory]
    [InlineData("[]", "blue", "venta")]                                                    // no está el dólar pedido
    [InlineData("{{", "blue", "venta")]                                                    // JSON roto
    [InlineData("{}", "blue", "venta")]                                                    // no es una lista
    [InlineData("[{\"casa\":\"blue\",\"venta\":-5}]", "blue", "venta")]                    // negativo
    [InlineData("[{\"casa\":\"blue\",\"venta\":0}]", "blue", "venta")]                     // cero
    [InlineData("[{\"casa\":\"blue\",\"venta\":99999999999}]", "blue", "venta")]           // absurdo
    [InlineData("[{\"casa\":\"blue\",\"venta\":\"x\"}]", "blue", "venta")]                 // texto
    [InlineData("[{\"casa\":\"blue\"}]", "blue", "venta")]                                 // sin el campo
    public void Una_respuesta_que_no_sirve_se_rechaza(string json, string casa, string price)
    {
        Assert.ThrowsAny<Exception>(() => ExchangeRateService.Parse(json, casa, price));
    }

    [Fact]
    public void Solo_acepta_venta_o_compra()
    {
        Assert.Throws<ArgumentException>(() => ExchangeRateService.Parse(Good, "blue", "promedio"));
    }

    // ───────────── Con un servidor local falso ─────────────

    private sealed class FakeServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        public string Url { get; }
        public int Status = 200;
        public string Body = Good;
        public int Hits;

        public FakeServer()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(Url);
            _listener.Start();
            _ = Task.Run(Serve);
        }

        private async Task Serve()
        {
            while (_listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    Interlocked.Increment(ref Hits);
                    context.Response.StatusCode = Status;
                    context.Response.OutputStream.Write(Encoding.UTF8.GetBytes(Body));
                    context.Response.Close();
                }
                catch (Exception) { break; }
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();
        }
    }

    private static T WithServer<T>(FakeServer server, Func<T> action)
    {
        var previous = Environment.GetEnvironmentVariable(ExchangeRateService.UrlOverrideVariable);
        Environment.SetEnvironmentVariable(ExchangeRateService.UrlOverrideVariable, server.Url);
        try { return action(); }
        finally { Environment.SetEnvironmentVariable(ExchangeRateService.UrlOverrideVariable, previous); }
    }

    [Fact]
    public async Task Pide_la_cotizacion_a_un_servidor_y_la_lee()
    {
        using var server = new FakeServer();

        var quote = await WithServer(server, () => ExchangeRateService.FetchAsync("blue", "venta"));

        Assert.Equal(1570.46m, quote.Value);
        Assert.Equal(1, server.Hits);
    }

    [Fact]
    public async Task Un_error_del_servidor_se_informa_como_error_de_conexion()
    {
        using var server = new FakeServer { Status = 500 };

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => WithServer(server, () => ExchangeRateService.FetchAsync("blue", "venta")));

        Assert.Contains("500", ExchangeRateService.Explain(error));
    }

    // ───────────── Desde la pantalla (ViewModel) ─────────────

    [Fact]
    public async Task Actualizar_guarda_la_cotizacion_con_su_origen_y_la_muestra()
    {
        using var server = new FakeServer();
        var vm = new MainViewModel();

        var ok = await WithServer(server, () => vm.UpdateRateAsync(manual: true));

        Assert.True(ok);
        Assert.Contains("Blue (venta)", vm.RateInfo);
        Assert.Contains(Money.Format(1570.46m, Currency.ARS), vm.RateInfo);
        using var db = new AppDbContext();
        Assert.Equal("Blue (venta)", db.Rates.Single().Source);
    }

    [Fact]
    public async Task La_misma_cotizacion_del_mismo_dia_se_actualiza_en_vez_de_duplicarse_y_otra_fuente_suma()
    {
        using var server = new FakeServer();
        var vm = new MainViewModel();

        await WithServer(server, () => vm.UpdateRateAsync(manual: true));
        server.Body = Good.Replace("1570.456", "1600");
        await WithServer(server, () => vm.UpdateRateAsync(manual: true));
        using (var db = new AppDbContext()) Assert.Equal(1600m, db.Rates.Single().ArsPerUsd);

        vm.SelectedRateSource = vm.RateSources.First(s => s.Key == "oficial");
        vm.SelectedRatePrice = vm.RatePrices.First(p => p.Key == "compra");
        await WithServer(server, () => vm.UpdateRateAsync(manual: true));

        using var check = new AppDbContext();
        Assert.Equal(2, check.Rates.Count());
        Assert.Contains("Oficial (compra)", vm.RateInfo);
    }

    [Fact]
    public async Task Si_falla_se_conserva_la_cotizacion_anterior_y_se_avisa_solo_si_lo_pidio_el_usuario()
    {
        using var server = new FakeServer();
        var vm = new MainViewModel();
        string? shown = null;
        vm.InfoNotifier = text => shown = text;
        await WithServer(server, () => vm.UpdateRateAsync(manual: true));
        var before = vm.RateInfo;
        server.Status = 500;

        var automatic = await WithServer(server, () => vm.UpdateRateAsync(manual: false));
        Assert.False(automatic);
        Assert.Null(shown);                                   // la actualización automática no interrumpe

        var manual = await WithServer(server, () => vm.UpdateRateAsync(manual: true));
        Assert.False(manual);
        Assert.NotNull(shown);
        Assert.Equal(before, vm.RateInfo);                    // sigue la cotización de antes
        using var db = new AppDbContext();
        Assert.Equal(1, db.Rates.Count());
    }

    [Fact]
    public void La_cotizacion_cargada_a_mano_queda_marcada_como_manual()
    {
        var vm = new MainViewModel { NewRate = "1.250,50" };

        vm.SaveRateCommand.Execute(null);

        using var db = new AppDbContext();
        var saved = db.Rates.Single();
        Assert.Equal("Manual", saved.Source);
        Assert.Equal(1250.50m, saved.ArsPerUsd);
    }
}

public class SettingsAndLegalTests : DatabaseTest
{
    private static string SettingsFile => Path.Combine(DataDir, "settings.json");

    // ───────────── Preferencias ─────────────

    [Fact]
    public void Sin_archivo_se_usan_los_valores_por_defecto()
    {
        Assert.Equal(ThemeChoice.System, AppSettings.Theme);
        Assert.False(AppSettings.AutoRate);
        Assert.Equal("blue", AppSettings.RateSource);
        Assert.Equal("venta", AppSettings.RatePrice);
        Assert.False(AppSettings.WelcomeSeen);
        Assert.Equal("", AppSettings.TermsAcceptedVersion);
        Assert.Null(AppSettings.ExtraBackupFolder);
    }

    [Fact]
    public void Cambiar_una_preferencia_no_pisa_las_otras()
    {
        AppSettings.Theme = ThemeChoice.Dark;
        AppSettings.RateSource = "oficial";
        AppSettings.ExtraBackupFolder = @"C:\Respaldos";
        AppSettings.WelcomeSeen = true;
        AppSettings.AutoRate = true;

        Assert.Equal(ThemeChoice.Dark, AppSettings.Theme);
        Assert.Equal("oficial", AppSettings.RateSource);
        Assert.Equal(@"C:\Respaldos", AppSettings.ExtraBackupFolder);
        Assert.True(AppSettings.WelcomeSeen);
        Assert.True(AppSettings.AutoRate);
    }

    [Theory]
    [InlineData("esto no es json")]
    [InlineData("")]
    [InlineData("{ \"Theme\": ")]
    public void Un_archivo_dañado_no_impide_abrir_la_app_y_se_usan_los_valores_por_defecto(string content)
    {
        File.WriteAllText(SettingsFile, content);

        Assert.Equal(ThemeChoice.System, AppSettings.Theme);
        Assert.Equal("blue", AppSettings.RateSource);
        AppSettings.Theme = ThemeChoice.Light;    // y se puede volver a guardar
        Assert.Equal(ThemeChoice.Light, AppSettings.Theme);
    }

    [Fact]
    public void Un_valor_que_no_existe_vuelve_al_valor_por_defecto()
    {
        File.WriteAllText(SettingsFile, "{\"Theme\":\"Verde\",\"RateSource\":\"inventado\",\"RatePrice\":\"promedio\"}");

        Assert.Equal(ThemeChoice.System, AppSettings.Theme);
        Assert.Equal("blue", AppSettings.RateSource);
        Assert.Equal("venta", AppSettings.RatePrice);
    }

    [Fact]
    public void Aceptar_los_terminos_guarda_la_version_y_la_fecha_sin_pisar_lo_demas()
    {
        AppSettings.Theme = ThemeChoice.Dark;

        AppSettings.AcceptTerms("2026-10-20");

        Assert.Equal("2026-10-20", AppSettings.TermsAcceptedVersion);
        Assert.Matches(@"""TermsAcceptedAt"":""20\d\d-", File.ReadAllText(SettingsFile));
        Assert.Equal(ThemeChoice.Dark, AppSettings.Theme);
    }

    // ───────────── Términos y recorrido de bienvenida ─────────────

    [Fact]
    public void Los_terminos_se_piden_hasta_aceptarlos_y_de_nuevo_si_cambia_la_version()
    {
        var vm = new MainViewModel();
        Assert.True(vm.NeedsTermsAcceptance());

        vm.AcceptTerms();
        Assert.False(vm.NeedsTermsAcceptance());

        AppSettings.AcceptTerms("1999-01-01");     // aceptó una versión vieja
        Assert.True(vm.NeedsTermsAcceptance());
    }

    [Fact]
    public void El_recorrido_se_muestra_una_vez_y_solo_a_quien_arranca_de_cero()
    {
        var vm = new MainViewModel();
        Assert.True(vm.ShouldShowWelcome());
        Assert.True(vm.ShouldShowWelcome());           // sigue hasta que se marque como visto

        vm.MarkWelcomeSeen();
        Assert.False(vm.ShouldShowWelcome());
    }

    [Fact]
    public void Quien_ya_tiene_movimientos_no_ve_el_recorrido_y_queda_marcado_como_visto()
    {
        AddTransaction(FirstAccountId(), TransactionType.Gasto, 10m, DateTime.Today);

        Assert.False(new MainViewModel().ShouldShowWelcome());
        Assert.True(AppSettings.WelcomeSeen);
    }

    // ───────────── Textos legales ─────────────

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CholinanzasApp.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la carpeta del repositorio.");
    }

    [Fact]
    public void La_version_de_los_terminos_en_el_codigo_coincide_con_la_del_texto()
    {
        // si se edita Legal\Terminos.txt hay que actualizar las dos: con la versión nueva, la app vuelve a pedir la aceptación
        Assert.Contains($"Versión del texto: {LegalTexts.TermsVersion}", LegalTexts.Terms());
    }

    [Fact]
    public void Los_textos_incrustados_son_los_mismos_que_los_archivos_del_repositorio()
    {
        var legal = Path.Combine(RepoRoot(), "Legal");

        Assert.Equal(File.ReadAllText(Path.Combine(legal, "Terminos.txt")).TrimStart('\uFEFF'), LegalTexts.Terms());
        Assert.Equal(File.ReadAllText(Path.Combine(legal, "AVISOS-DE-TERCEROS.txt")).TrimStart('\uFEFF'), LegalTexts.ThirdPartyNotices());
    }

    [Fact]
    public void Los_textos_legales_se_leen_con_sus_acentos_y_sin_la_marca_de_UTF8()
    {
        var terms = LegalTexts.Terms();
        var notices = LegalTexts.ThirdPartyNotices();

        Assert.NotEqual('\uFEFF', terms[0]);
        Assert.NotEqual('\uFEFF', notices[0]);
        Assert.Contains("TÉRMINOS DE USO", terms);
        Assert.DoesNotContain("Ã", terms);
        Assert.Contains("AVISOS DE SOFTWARE DE TERCEROS", notices);
        Assert.DoesNotContain("Ã", notices);
    }

    [Fact]
    public void Cada_libreria_que_usa_la_app_figura_en_los_avisos_de_terceros()
    {
        // si se agrega un paquete y no se vuelve a correr installer\generate-notices.ps1, esta prueba falla
        var csproj = File.ReadAllText(Path.Combine(RepoRoot(), "CholinanzasApp.csproj"));
        var packages = Regex.Matches(csproj, "<PackageReference Include=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        var notices = LegalTexts.ThirdPartyNotices();

        Assert.NotEmpty(packages);
        Assert.All(packages, package => Assert.Contains(package, notices));
        Assert.DoesNotContain("DESCONOCIDA", notices);
    }

    [Fact]
    public void El_instalador_apunta_a_archivos_legales_que_existen()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "installer", "Cholinanzas.iss"));
        var referenced = Regex.Matches(script, @"(?:LicenseFile=|Source: "")\.\.\\(Legal\\[^""\r\n]+)").Select(m => m.Groups[1].Value).ToList();

        Assert.Contains(@"Legal\Terminos.txt", referenced);
        Assert.All(referenced, relative => Assert.True(File.Exists(Path.Combine(RepoRoot(), relative)), relative));
    }
}
