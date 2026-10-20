using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace CholinanzasApp.Services;

/// <summary>
/// Cambia entre tema claro y oscuro sin reiniciar: reemplaza el diccionario de colores y el tema de los
/// controles de Windows (Fluent). En "Según Windows" sigue la configuración del sistema, también en vivo.
/// </summary>
public static class ThemeService
{
    public static ThemeChoice Choice { get; private set; } = ThemeChoice.System;
    public static bool IsDark { get; private set; }

    public static void Initialize()
    {
        Apply(AppSettings.Theme);

        // en "Según Windows", si el usuario cambia el tema del sistema con la app abierta, la app lo acompaña
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (Choice != ThemeChoice.System) return;
            if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)) return;
            Application.Current?.Dispatcher.BeginInvoke(() => Apply(ThemeChoice.System));
        };
    }

    public static void Apply(ThemeChoice choice)
    {
        Choice = choice;
        IsDark = choice == ThemeChoice.Dark || (choice == ThemeChoice.System && SystemUsesDarkTheme());

        var app = Application.Current;
        app.ThemeMode = IsDark ? ThemeMode.Dark : ThemeMode.Light;

        // los estilos usan {DynamicResource}: al reemplazar el diccionario de colores se actualiza todo
        // dirección completa (con el nombre del ensamblado): no depende de qué ejecutable arrancó el proceso
        var assembly = typeof(ThemeService).Assembly.GetName().Name;
        var colors = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/{assembly};component/Themes/Colors.{(IsDark ? "Dark" : "Light")}.xaml",
                UriKind.Absolute)
        };
        var merged = app.Resources.MergedDictionaries;
        var index = merged.ToList().FindIndex(d => d.Source?.OriginalString.Contains("Colors.") == true);
        if (index >= 0) merged[index] = colors;
        else merged.Insert(0, colors);
    }

    // Windows guarda 0 en AppsUseLightTheme cuando las aplicaciones usan el tema oscuro.
    private static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false; // si no se puede leer, se asume claro
        }
    }
}
