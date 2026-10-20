using System.Net.Http;
using System.Text.Json;

namespace CholinanzasApp.Services;

/// <summary>Una cotización ya leída: pesos por 1 USD, cuándo la actualizó el servicio y de qué dólar y valor es.</summary>
public record RateQuote(decimal Value, DateTime? UpdatedAt, string Name, string Price);

/// <summary>
/// Trae la cotización pública del dólar de dolarapi.com. Es la única parte de la app que usa internet, y solo
/// cuando el usuario lo pide. No se envía ningún dato del usuario: es un GET a una dirección pública.
/// El servicio es de un tercero y puede fallar: el que llama tiene que seguir funcionando sin él.
/// </summary>
public static class ExchangeRateService
{
    public const string DefaultUrl = "https://dolarapi.com/v1/dolares";

    /// <summary>Solo para pruebas: reemplaza la dirección, y únicamente si apunta a esta misma computadora.</summary>
    public const string UrlOverrideVariable = "CHOLINANZAS_RATE_URL";

    private const int MaxResponseBytes = 1_000_000;
    private const decimal MaxPlausibleRate = 10_000_000m;

    public static readonly IReadOnlyList<(string Key, string Label)> Sources =
    [
        ("oficial", "Oficial"),
        ("blue", "Blue"),
        ("bolsa", "MEP"),
        ("contadoconliqui", "Contado con liquidación"),
        ("tarjeta", "Tarjeta")
    ];

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { MaxAutomaticRedirections = 3 })
        {
            Timeout = TimeSpan.FromSeconds(10),
            MaxResponseContentBufferSize = MaxResponseBytes // una respuesta más grande se corta: no debería pasar con este servicio
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Cholinanzas/1.0");
        return client;
    }

    /// <param name="casa">oficial, blue, bolsa (MEP), contadoconliqui o tarjeta.</param>
    /// <param name="price">"venta" o "compra".</param>
    public static async Task<RateQuote> FetchAsync(string casa, string price, CancellationToken cancellation = default)
    {
        var url = ResolveUrl();
        using var response = await Http.GetAsync(url, cancellation);
        response.EnsureSuccessStatusCode();

        // si hubo redirecciones, la dirección final también tiene que ser segura
        var finalUrl = response.RequestMessage?.RequestUri ?? url;
        if (finalUrl.Scheme != Uri.UriSchemeHttps && !finalUrl.IsLoopback)
            throw new HttpRequestException("El servicio respondió desde una dirección no segura (sin HTTPS).");

        var json = await response.Content.ReadAsStringAsync(cancellation);
        return Parse(json, casa, price);
    }

    private static Uri ResolveUrl()
    {
        var overridden = Environment.GetEnvironmentVariable(UrlOverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridden) && Uri.TryCreate(overridden, UriKind.Absolute, out var local) && local.IsLoopback)
            return local;
        return new Uri(DefaultUrl);
    }

    /// <summary>Busca el dólar pedido en la respuesta y valida el valor. Lanza FormatException o JsonException si no sirve.</summary>
    public static RateQuote Parse(string json, string casa, string price)
    {
        if (price is not ("venta" or "compra"))
            throw new ArgumentException("El valor tiene que ser \"venta\" o \"compra\".", nameof(price));

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new FormatException("La respuesta del servicio no tiene el formato esperado.");

        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty("casa", out var house) || house.ValueKind != JsonValueKind.String) continue;
            if (!string.Equals(house.GetString(), casa, StringComparison.OrdinalIgnoreCase)) continue;
            if (item.TryGetProperty("moneda", out var currency) && currency.ValueKind == JsonValueKind.String
                && !string.Equals(currency.GetString(), "USD", StringComparison.OrdinalIgnoreCase)) continue;

            if (!item.TryGetProperty(price, out var number) || number.ValueKind != JsonValueKind.Number || !number.TryGetDecimal(out var value))
                throw new FormatException($"El servicio no trae el valor de {price} para esa cotización.");
            if (value <= 0 || value > MaxPlausibleRate)
                throw new FormatException("La cotización recibida no es válida.");

            DateTime? updated = item.TryGetProperty("fechaActualizacion", out var date)
                                && date.ValueKind == JsonValueKind.String && date.TryGetDateTime(out var parsed)
                ? parsed.ToLocalTime()
                : null;
            var name = item.TryGetProperty("nombre", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? casa : casa;
            return new RateQuote(Math.Round(value, 2), updated, name, price);
        }

        throw new FormatException("El servicio no devolvió esa cotización.");
    }

    /// <summary>Explica en criollo por qué falló, sin mostrar mensajes técnicos.</summary>
    public static string Explain(Exception ex) => ex switch
    {
        TaskCanceledException => "el servicio tardó demasiado en responder.",
        HttpRequestException { StatusCode: { } code } => $"el servicio respondió con un error ({(int)code}).",
        HttpRequestException => "no se pudo conectar. ¿Hay internet?",
        JsonException or FormatException => "el servicio devolvió algo que no se pudo entender.",
        _ => "ocurrió un problema inesperado."
    };
}
