using System.IO;
using System.Reflection;
using CholinanzasApp.Data;

namespace CholinanzasApp.ViewModels;

/// <summary>Datos que muestra la pestaña Ayuda (versión y dónde están los datos).</summary>
public partial class MainViewModel
{
    public string AppVersionText { get; } = "Cholinanzas versión " + (
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "desconocida");

    public string DataFolderText { get; } = Path.GetDirectoryName(AppDbContext.DbPath)!;
}
