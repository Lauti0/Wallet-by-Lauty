using System.IO;
using System.Text.RegularExpressions;
using CholinanzasApp.Data;
using Microsoft.Data.Sqlite;

namespace CholinanzasApp.Services;

public record BackupInfo(string Path, DateTime Date, long Size);

/// <summary>
/// Respaldos de la base de datos: uno automático por día al abrir la app (antes de tocar nada),
/// más los manuales. Usa la copia nativa de SQLite, que saca una foto consistente aunque la base esté en uso.
/// </summary>
public static class BackupService
{
    public const int KeepCount = 30;
    private const string Pattern = "finanzas-*.db";
    private static readonly Regex OwnName = new(@"^finanzas-\d{8}-\d{6}\.db$", RegexOptions.CultureInvariant);

    private static string DataDir => Path.GetDirectoryName(AppDbContext.DbPath)!;
    public static string BackupDir => Path.Combine(DataDir, "Backups");

    /// <summary>Último problema ocurrido al respaldar, para mostrarlo en pantalla (null si todo salió bien).</summary>
    public static string? LastError { get; private set; }

    // ───────────── Carpeta extra ─────────────

    // La preferencia vive en AppSettings (junto con el tema); un settings.json dañado no impide abrir la app.
    public static string? ExtraFolder => AppSettings.ExtraBackupFolder;

    public static void SetExtraFolder(string? folder) => AppSettings.ExtraBackupFolder = folder;

    // ───────────── Respaldar ─────────────

    /// <summary>Se llama al abrir la app: si todavía no hay un respaldo de hoy, lo hace. Nunca lanza excepciones.</summary>
    public static void RunAutomatic()
    {
        try
        {
            if (!File.Exists(AppDbContext.DbPath)) return; // primera vez: no hay nada que respaldar
            if (List().Any(b => b.Date.Date == DateTime.Today)) return;
            CreateBackup();
        }
        catch (Exception ex)
        {
            LastError = $"No se pudo hacer el respaldo automático: {ex.Message}";
        }
    }

    /// <summary>Hace un respaldo ahora. Si falla el respaldo principal, lanza la excepción.</summary>
    public static BackupInfo CreateBackup()
    {
        Directory.CreateDirectory(BackupDir);
        var name = $"finanzas-{DateTime.Now:yyyyMMdd-HHmmss}.db";
        var target = Path.Combine(BackupDir, name);
        if (File.Exists(target)) return Describe(target); // dos pedidos en el mismo segundo

        CopyDatabase(AppDbContext.DbPath, target);
        VerifyDatabase(target);
        LastError = null;

        CopyToExtraFolder(target, name);
        Prune(BackupDir);
        return Describe(target);
    }

    // La carpeta extra puede ser un pendrive o una nube sin conexión: si falla, avisa pero no pierde el respaldo local.
    private static void CopyToExtraFolder(string source, string name)
    {
        var folder = ExtraFolder;
        if (string.IsNullOrWhiteSpace(folder)) return;

        try
        {
            Directory.CreateDirectory(folder);
            File.Copy(source, Path.Combine(folder, name), overwrite: true);
            Prune(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = $"El respaldo se guardó acá, pero no se pudo copiar a la carpeta extra ({folder}): {ex.Message}";
        }
    }

    // ───────────── Listar y limpiar ─────────────

    public static List<BackupInfo> List() =>
        !Directory.Exists(BackupDir)
            ? []
            : OwnBackups(BackupDir)
                .Select(f => new BackupInfo(f.FullName, f.LastWriteTime, f.Length))
                .ToList();

    // Solo los archivos con el nombre exacto que genera la app (finanzas-AAAAMMDD-HHMMSS.db), del más nuevo al más viejo.
    // Con un patrón más amplio, listar o limpiar una carpeta elegida por el usuario podía tocar archivos suyos
    // que se llamaran, por ejemplo, "finanzas-familia.db".
    private static IEnumerable<FileInfo> OwnBackups(string folder) =>
        new DirectoryInfo(folder).GetFiles(Pattern)
            .Where(f => OwnName.IsMatch(f.Name))
            .OrderByDescending(f => f.LastWriteTime);

    private static BackupInfo Describe(string path)
    {
        var file = new FileInfo(path);
        return new BackupInfo(file.FullName, file.LastWriteTime, file.Length);
    }

    // Conserva los KeepCount más nuevos de la carpeta; borra solo archivos con el nombre de nuestros respaldos.
    private static void Prune(string folder)
    {
        try
        {
            foreach (var old in OwnBackups(folder).Skip(KeepCount))
                old.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // limpiar es opcional: si falla, quedan respaldos de más
        }
    }

    // ───────────── Restaurar ─────────────

    /// <summary>
    /// Reemplaza los datos actuales por los del respaldo. Antes guarda un respaldo de seguridad de lo actual
    /// y después actualiza el esquema por si el respaldo es de una versión anterior.
    /// </summary>
    public static void Restore(string backupPath)
    {
        VerifyDatabase(backupPath);   // no tocar nada si el respaldo está dañado
        CreateBackup();               // red de seguridad: lo actual queda respaldado

        CopyDatabase(backupPath, AppDbContext.DbPath);
        SqliteConnection.ClearAllPools();
        AppDbContext.Initialize();
    }

    // ───────────── SQLite ─────────────

    private static string ConnectionString(string path, bool readOnly = false) => new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Pooling = false,
        Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate
    }.ToString();

    private static void CopyDatabase(string from, string to)
    {
        // Pooling=False para que el archivo no quede bloqueado cuando terminamos.
        // La ruta se arma con el "builder" y no pegada en el texto: una ruta con ";" o "=" no puede alterar la conexión.
        using var source = new SqliteConnection(ConnectionString(from));
        using var destination = new SqliteConnection(ConnectionString(to));
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    private static void VerifyDatabase(string path)
    {
        string? result;
        using (var connection = new SqliteConnection(ConnectionString(path, readOnly: true)))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check";
            result = command.ExecuteScalar() as string;
        }

        if (result != "ok")
            throw new InvalidDataException($"El archivo de respaldo no pasó la verificación ({result}).");
    }
}
