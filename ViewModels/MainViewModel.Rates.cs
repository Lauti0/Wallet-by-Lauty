using CholinanzasApp.Data;
using CholinanzasApp.Models;
using CholinanzasApp.Services;
using Microsoft.EntityFrameworkCore;

namespace CholinanzasApp.ViewModels;

public record RateOption(string Key, string Label);

/// <summary>
/// Cotización del dólar traída de internet. Es opcional y la pide el usuario: con el botón "Actualizar ahora"
/// o, si lo activa, sola una vez por día al abrir la app. Si falla, la app sigue con la última cotización cargada.
/// </summary>
public partial class MainViewModel
{
    private bool _autoRate, _fetchingRate;
    private RateOption _selectedSource = null!, _selectedPrice = null!; // se completan en LoadRateSettings
    private string _rateStatus = "";
    private RelayCommand? _updateRateCommand;

    public List<RateOption> RateSources { get; } =
        ExchangeRateService.Sources.Select(s => new RateOption(s.Key, s.Label)).ToList();

    public List<RateOption> RatePrices { get; } = [new("venta", "Venta"), new("compra", "Compra")];

    public bool AutoRateEnabled
    {
        get => _autoRate;
        set
        {
            if (_autoRate == value) return;
            Set(ref _autoRate, value);
            AppSettings.AutoRate = value;
        }
    }

    public RateOption SelectedRateSource
    {
        get => _selectedSource;
        set
        {
            if (value is null || _selectedSource == value) return;
            Set(ref _selectedSource, value);
            AppSettings.RateSource = value.Key;
        }
    }

    public RateOption SelectedRatePrice
    {
        get => _selectedPrice;
        set
        {
            if (value is null || _selectedPrice == value) return;
            Set(ref _selectedPrice, value);
            AppSettings.RatePrice = value.Key;
        }
    }

    public string RateStatus { get => _rateStatus; private set => Set(ref _rateStatus, value); }
    public bool CanUpdateRate => !_fetchingRate;

    public RelayCommand UpdateRateNowCommand => _updateRateCommand ??= new(_ => _ = UpdateRateAsync(manual: true));

    // Lee las preferencias guardadas sin volver a escribirlas.
    private void LoadRateSettings()
    {
        _autoRate = AppSettings.AutoRate;
        _selectedSource = RateSources.First(s => s.Key == AppSettings.RateSource);
        _selectedPrice = RatePrices.First(p => p.Key == AppSettings.RatePrice);
    }

    // Con la opción activada, al abrir la app se pide la cotización; una vez por día alcanza.
    private void StartAutomaticRate()
    {
        if (!AutoRateEnabled) return;

        using (var db = new AppDbContext())
        {
            var latest = db.Rates.AsNoTracking().ToList()
                .OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).FirstOrDefault();
            if (latest is { Source: not null and not "Manual" } && latest.Date.Date == DateTime.Today) return;
        }
        _ = UpdateRateAsync(manual: false);
    }

    /// <summary>Pide la cotización elegida, la guarda y recalcula todo. Devuelve false si no se pudo, sin cambiar nada.</summary>
    public async Task<bool> UpdateRateAsync(bool manual)
    {
        if (_fetchingRate) return false;
        SetFetching(true);
        RateStatus = "Consultando la cotización…";

        try
        {
            var quote = await ExchangeRateService.FetchAsync(SelectedRateSource.Key, SelectedRatePrice.Key);
            var label = $"{SelectedRateSource.Label} ({SelectedRatePrice.Label.ToLowerInvariant()})";
            SaveFetchedRate(quote.Value, label);
            Reload();

            RateStatus = $"Actualizada: {label} = {Money.Format(quote.Value, Currency.ARS)}"
                         + (quote.UpdatedAt is { } at ? $"  ·  dato del servicio: {at:dd/MM HH:mm}" : "");
            return true;
        }
        catch (Exception ex)
        {
            var why = ExchangeRateService.Explain(ex);
            RateStatus = $"No se pudo actualizar: {why} Se sigue usando la última cotización cargada.";
            if (manual)
                InfoNotifier($"No se pudo traer la cotización: {why}\n\nSe sigue usando la última cotización cargada.");
            return false;
        }
        finally
        {
            SetFetching(false);
        }
    }

    private void SetFetching(bool fetching)
    {
        _fetchingRate = fetching;
        Raise(nameof(CanUpdateRate));
    }

    // Una cotización por día y origen: si hoy ya se trajo la misma, se actualiza en vez de sumar otra fila.
    private static void SaveFetchedRate(decimal value, string label)
    {
        using var db = new AppDbContext();
        var latest = db.Rates.ToList().OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).FirstOrDefault();
        if (latest is not null && latest.Date.Date == DateTime.Today && latest.Source == label)
            latest.ArsPerUsd = value;
        else
            db.Rates.Add(new ExchangeRate { Date = DateTime.Today, ArsPerUsd = value, Source = label });
        db.SaveChanges();
    }
}
