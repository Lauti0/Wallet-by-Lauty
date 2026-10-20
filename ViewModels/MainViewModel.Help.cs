using System.IO;
using System.Reflection;
using CholinanzasApp.Data;
using CholinanzasApp.Services;

namespace CholinanzasApp.ViewModels;

/// <summary>Datos que muestra la pestaña Ayuda (versión y dónde están los datos).</summary>
public partial class MainViewModel
{
    public string AppVersionText { get; } = "Cholinanzas versión " + (
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "desconocida");

    public string DataFolderText { get; } = Path.GetDirectoryName(AppDbContext.DbPath)!;

    /// <summary>La ventana principal lo escucha para abrir el recorrido de bienvenida (botón de la pestaña Ayuda).</summary>
    public event Action? WelcomeRequested;

    private RelayCommand? _showWelcome;
    public RelayCommand ShowWelcomeCommand => _showWelcome ??= new(_ => WelcomeRequested?.Invoke());

    /// <summary>
    /// El recorrido se muestra una sola vez, y solo a quien arranca de cero: si la base ya tiene movimientos
    /// o transferencias (alguien que venía usando la app), se lo marca como visto sin mostrarlo.
    /// </summary>
    public bool ShouldShowWelcome()
    {
        if (AppSettings.WelcomeSeen) return false;

        using var db = new AppDbContext();
        if (db.Transactions.Any() || db.Transfers.Any())
        {
            AppSettings.WelcomeSeen = true;
            return false;
        }
        return true;
    }

    public void MarkWelcomeSeen() => AppSettings.WelcomeSeen = true;
}
