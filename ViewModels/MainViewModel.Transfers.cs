using System.Collections.ObjectModel;
using System.Windows;
using CholinanzasApp.Data;
using CholinanzasApp.Models;
using Microsoft.EntityFrameworkCore;

namespace CholinanzasApp.ViewModels;

public record TransferRow(int Id, DateTime Date, string From, string To, Currency FromCurrency,
    Currency ToCurrency, decimal AmountOut, decimal AmountIn, string Description)
{
    public string OutText => Money.Format(AmountOut, FromCurrency);
    public string InText => Money.Format(AmountIn, ToCurrency);

    // Cotización implícita (pesos por dólar) cuando la transferencia cambia de moneda.
    public string RateText => FromCurrency == ToCurrency || AmountOut == 0 || AmountIn == 0
        ? "—"
        : Money.Format(FromCurrency == Currency.USD ? AmountIn / AmountOut : AmountOut / AmountIn, Currency.ARS);
}

public record NetWorthRow(string Label, string ArsText, string UsdText);

public partial class MainViewModel
{
    private AccountRow? _transferFrom, _transferTo;
    private DateTime _transferDate = DateTime.Today;
    private string _transferAmountOut = "", _transferAmountIn = "", _transferDescription = "";
    private string _newRate = "";
    private decimal? _rate;
    private string _rateInfo = "";

    private RelayCommand? _addTransferCommand, _deleteTransferCommand, _saveRateCommand;

    public ObservableCollection<TransferRow> Transfers { get; } = [];
    public ObservableCollection<NetWorthRow> NetWorth { get; } = [];

    public AccountRow? TransferFrom { get => _transferFrom; set => Set(ref _transferFrom, value); }
    public AccountRow? TransferTo { get => _transferTo; set => Set(ref _transferTo, value); }
    public DateTime TransferDate { get => _transferDate; set => Set(ref _transferDate, value); }
    public string TransferAmountOut { get => _transferAmountOut; set => Set(ref _transferAmountOut, value); }
    public string TransferAmountIn { get => _transferAmountIn; set => Set(ref _transferAmountIn, value); }
    public string TransferDescription { get => _transferDescription; set => Set(ref _transferDescription, value); }

    public string NewRate { get => _newRate; set => Set(ref _newRate, value); }
    public string RateInfo { get => _rateInfo; private set => Set(ref _rateInfo, value); }

    public RelayCommand AddTransferCommand => _addTransferCommand ??= new(_ => AddTransfer());
    public RelayCommand DeleteTransferCommand => _deleteTransferCommand ??= new(p => DeleteTransfer(p as TransferRow));
    public RelayCommand SaveRateCommand => _saveRateCommand ??= new(_ => SaveRate());

    private decimal ToArs(Currency currency, decimal amount) =>
        currency == Currency.ARS ? amount : amount * _rate!.Value;

    private void ReloadTransfers(AppDbContext db, List<Transfer> transfers, int? fromId, int? toId)
    {
        // las cuentas se reconstruyen en cada recarga: se vuelve a elegir por Id
        TransferFrom = Accounts.FirstOrDefault(a => a.Id == fromId) ?? Accounts.FirstOrDefault();
        TransferTo = Accounts.FirstOrDefault(a => a.Id == toId)
                     ?? Accounts.FirstOrDefault(a => a.Id != TransferFrom?.Id);

        var start = _month;
        var next = _month.AddMonths(1);
        Transfers.Clear();
        foreach (var t in transfers.Where(t => t.Date >= start && t.Date < next)
                     .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id))
            Transfers.Add(new TransferRow(t.Id, t.Date, t.FromAccount.Name, t.ToAccount.Name,
                t.FromAccount.Currency, t.ToAccount.Currency, t.AmountOut, t.AmountIn, t.Description));

        var latest = db.Rates.AsNoTracking().ToList()
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).FirstOrDefault();
        _rate = latest?.ArsPerUsd;
        RateInfo = latest is null
            ? "Todavía no cargaste la cotización."
            : $"Cotización actual: 1 USD = {Money.Format(latest.ArsPerUsd, Currency.ARS)} (cargada el {latest.Date:dd/MM/yyyy})";

        NetWorth.Clear();
        if (_rate is null || _rate == 0)
        {
            if (Accounts.Any(a => a.Currency == Currency.USD))
            {
                NetWorth.Add(new NetWorthRow("Cargá la cotización del dólar (pestaña Cuentas) para ver el total", "—", "—"));
            }
            else
            {
                // sin dólares no hace falta cotización: el total es la suma en pesos
                NetWorth.Add(new NetWorthRow("Hoy", Money.Format(Accounts.Sum(a => a.Balance), Currency.ARS), ""));
                if (Projection.Count > 0)
                    NetWorth.Add(new NetWorthRow($"Proyectado a fin de {MonthTitle}",
                        Money.Format(Projection.Sum(p => p.Projected), Currency.ARS), ""));
            }
            return;
        }

        NetWorth.Add(BuildNetWorthRow("Hoy", Accounts.Select(a => ToArs(a.Currency, a.Balance)).Sum()));
        if (Projection.Count > 0)
            NetWorth.Add(BuildNetWorthRow($"Proyectado a fin de {MonthTitle}",
                Projection.Select(p => ToArs(p.Currency, p.Projected)).Sum()));
    }

    private NetWorthRow BuildNetWorthRow(string label, decimal totalArs) =>
        new(label, Money.Format(totalArs, Currency.ARS), "≈ " + Money.Format(totalArs / _rate!.Value, Currency.USD));

    private void SaveRate()
    {
        if (!TryParseAmount(NewRate, out var rate) || rate <= 0)
        {
            MessageBox.Show("Ingresá una cotización válida (pesos por 1 USD).", "Cotización");
            return;
        }

        using (var db = new AppDbContext())
        {
            db.Rates.Add(new ExchangeRate { Date = DateTime.Today, ArsPerUsd = rate });
            db.SaveChanges();
        }

        NewRate = "";
        Reload();
    }

    private void AddTransfer()
    {
        if (TransferFrom is null || TransferTo is null)
        {
            MessageBox.Show("Elegí la cuenta de origen y la de destino.", "Transferencia");
            return;
        }
        if (TransferFrom.Id == TransferTo.Id)
        {
            MessageBox.Show("El origen y el destino tienen que ser cuentas distintas.", "Transferencia");
            return;
        }
        if (!TryParseAmount(TransferAmountOut, out var amountOut) || amountOut <= 0)
        {
            MessageBox.Show("Ingresá un monto de salida válido mayor a 0.", "Transferencia");
            return;
        }

        decimal amountIn;
        if (TransferFrom.Currency == TransferTo.Currency)
        {
            amountIn = amountOut;
        }
        else if (!string.IsNullOrWhiteSpace(TransferAmountIn))
        {
            if (!TryParseAmount(TransferAmountIn, out amountIn) || amountIn <= 0)
            {
                MessageBox.Show("El monto que entra no es válido.", "Transferencia");
                return;
            }
        }
        else if (_rate is { } rate && rate > 0)
        {
            // sin monto de entrada: se convierte con la cotización cargada
            amountIn = TransferFrom.Currency == Currency.ARS ? amountOut / rate : amountOut * rate;
        }
        else
        {
            MessageBox.Show("Las cuentas son de distinta moneda: indicá cuánto entra o cargá la cotización.",
                "Transferencia");
            return;
        }

        using (var db = new AppDbContext())
        {
            if (_editingTransferId is { } id)
            {
                var existing = db.Transfers.Find(id);
                if (existing is null)
                {
                    // se borró mientras se editaba
                    InfoNotifier("Esa transferencia ya no existe.");
                    ClearTransferForm();
                    return;
                }
                existing.Date = TransferDate.Date;
                existing.AmountOut = amountOut;
                existing.AmountIn = Math.Round(amountIn, 2);
                existing.Description = TransferDescription.Trim();
                existing.FromAccountId = TransferFrom.Id;
                existing.ToAccountId = TransferTo.Id;
            }
            else
            {
                db.Transfers.Add(new Transfer
                {
                    Date = TransferDate.Date,
                    AmountOut = amountOut,
                    AmountIn = Math.Round(amountIn, 2),
                    Description = TransferDescription.Trim(),
                    FromAccountId = TransferFrom.Id,
                    ToAccountId = TransferTo.Id
                });
            }
            db.SaveChanges();
        }

        ShowMonthOf(TransferDate);
        ClearTransferForm();
    }

    private void DeleteTransfer(TransferRow? row)
    {
        if (row is null) return;
        if (MessageBox.Show($"¿Eliminar la transferencia {row.From} → {row.To} ({row.OutText})?", "Eliminar",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

        using (var db = new AppDbContext())
        {
            db.Transfers.Where(t => t.Id == row.Id).ExecuteDelete();
        }
        if (_editingTransferId == row.Id) SetEditingTransfer(null);
        Reload();
    }
}
