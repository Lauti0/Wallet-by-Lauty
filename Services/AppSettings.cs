using System.IO;
using System.Text.Json;
using CholinanzasApp.Data;

namespace CholinanzasApp.Services;

public enum ThemeChoice { System, Light, Dark }

/// <summary>
/// Preferencias del usuario, guardadas en settings.json junto a la base de datos (carpeta extra de respaldos
/// y tema). Un archivo dañado o ausente nunca impide abrir la app: se usan los valores por defecto.
/// </summary>
public static class AppSettings
{
    private sealed record Data(string? ExtraBackupFolder = null, string Theme = nameof(ThemeChoice.System));

    private static string DataDir => Path.GetDirectoryName(AppDbContext.DbPath)!;
    private static string SettingsPath => Path.Combine(DataDir, "settings.json");

    private static Data Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<Data>(File.ReadAllText(SettingsPath)) ?? new Data()
                : new Data();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Data();
        }
    }

    // Se guarda el archivo completo: así cambiar una preferencia no borra las otras.
    private static void Save(Data data)
    {
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(data));
    }

    public static string? ExtraBackupFolder
    {
        get => Load().ExtraBackupFolder;
        set => Save(Load() with { ExtraBackupFolder = string.IsNullOrWhiteSpace(value) ? null : value });
    }

    public static ThemeChoice Theme
    {
        get => Enum.TryParse<ThemeChoice>(Load().Theme, out var theme) ? theme : ThemeChoice.System;
        set => Save(Load() with { Theme = value.ToString() });
    }
}
