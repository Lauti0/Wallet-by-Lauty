using System.IO;
using System.Runtime.CompilerServices;
using CholinanzasApp.Data;
using CholinanzasApp.Models;
using Microsoft.Data.Sqlite;

// Las pruebas comparten UNA base de datos de prueba (una carpeta temporal por ejecución) y se vacía entre una y otra, así
// que no pueden correr en paralelo.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CholinanzasApp.Tests;

/// <summary>
/// Apunta la aplicación a una carpeta temporal ANTES de que se use cualquier clase de la app (la ruta de la base se lee
/// una sola vez, de la variable CHOLINANZAS_DB). Así las pruebas nunca tocan los datos reales del usuario.
/// </summary>
internal static class TestEnvironment
{
    public static string Dir { get; } = Path.Combine(Path.GetTempPath(), "cholinanzas-tests-" + Guid.NewGuid().ToString("N")[..8]);

    [ModuleInitializer]
    internal static void Init()
    {
        Directory.CreateDirectory(Dir);
        Environment.SetEnvironmentVariable("CHOLINANZAS_DB", Path.Combine(Dir, "finanzas.db"));

        // al terminar la ejecución se borra la carpeta temporal (si algo la tiene abierta, queda: es solo basura de prueba)
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        };
    }
}

/// <summary>Base de las pruebas que usan la base de datos: cada una arranca con una base nueva, con las categorías de fábrica.</summary>
public abstract class DatabaseTest : IDisposable
{
    protected DatabaseTest() => Reset();

    public void Dispose() => GC.SuppressFinalize(this);

    /// <summary>Carpeta donde viven la base, los respaldos y settings.json de la prueba.</summary>
    protected static string DataDir => Path.GetDirectoryName(AppDbContext.DbPath)!;

    protected static void Reset()
    {
        SqliteConnection.ClearAllPools(); // que no quede el archivo bloqueado
        if (Directory.Exists(DataDir)) Directory.Delete(DataDir, recursive: true);
        Directory.CreateDirectory(DataDir);
        AppDbContext.Initialize();
    }

    // ───────────── Atajos para armar datos ─────────────

    protected static int FirstAccountId()
    {
        using var db = new AppDbContext();
        return db.Accounts.First().Id;
    }

    protected static int AddAccount(string name, Currency currency = Currency.ARS, decimal initial = 0)
    {
        using var db = new AppDbContext();
        var account = new Account { Name = name, Currency = currency, InitialBalance = initial };
        db.Accounts.Add(account);
        db.SaveChanges();
        return account.Id;
    }

    protected static int CategoryId(TransactionType type, string? name = null)
    {
        using var db = new AppDbContext();
        return db.Categories.First(c => c.Type == type && (name == null || c.Name == name)).Id;
    }

    protected static int AddTransaction(int accountId, TransactionType type, decimal amount, DateTime date,
        string description = "movimiento", int? categoryId = null, int? recurringId = null)
    {
        using var db = new AppDbContext();
        var transaction = new FinanceTransaction
        {
            AccountId = accountId, Type = type, Amount = amount, Date = date.Date, Description = description,
            CategoryId = categoryId ?? db.Categories.First(c => c.Type == type).Id, RecurringId = recurringId
        };
        db.Transactions.Add(transaction);
        db.SaveChanges();
        return transaction.Id;
    }

    protected static string WriteFile(string name, string content)
    {
        var path = Path.Combine(DataDir, name);
        File.WriteAllText(path, content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }
}
