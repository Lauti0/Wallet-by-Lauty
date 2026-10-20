using CholinanzasApp.Services;

namespace CholinanzasApp.ViewModels;

/// <summary>Elección del tema (claro, oscuro o el de Windows). Se aplica al instante y queda guardada.</summary>
public partial class MainViewModel
{
    public bool ThemeSystem
    {
        get => ThemeService.Choice == ThemeChoice.System;
        set { if (value) ChooseTheme(ThemeChoice.System); }
    }

    public bool ThemeLight
    {
        get => ThemeService.Choice == ThemeChoice.Light;
        set { if (value) ChooseTheme(ThemeChoice.Light); }
    }

    public bool ThemeDark
    {
        get => ThemeService.Choice == ThemeChoice.Dark;
        set { if (value) ChooseTheme(ThemeChoice.Dark); }
    }

    private void ChooseTheme(ThemeChoice choice)
    {
        if (ThemeService.Choice == choice) return;

        AppSettings.Theme = choice;
        ThemeService.Apply(choice);

        // los tres botones de opción se actualizan juntos
        Raise(nameof(ThemeSystem));
        Raise(nameof(ThemeLight));
        Raise(nameof(ThemeDark));
    }
}
