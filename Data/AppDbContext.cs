using System.IO;
using CholinanzasApp.Models;
using Microsoft.EntityFrameworkCore;

namespace CholinanzasApp.Data;

public class AppDbContext : DbContext
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<FinanceTransaction> Transactions => Set<FinanceTransaction>();
    public DbSet<RecurringTransaction> Recurrings => Set<RecurringTransaction>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<ExchangeRate> Rates => Set<ExchangeRate>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<SavingsGoal> Goals => Set<SavingsGoal>();

    // CHOLINANZAS_DB permite apuntar a otro archivo (por ejemplo, para probar con datos de prueba).
    public static string DbPath { get; } =
        Environment.GetEnvironmentVariable("CHOLINANZAS_DB")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CholinanzasApp", "finanzas.db");

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        options.UseSqlite($"Data Source={DbPath}");
    }

    // EnsureCreated no altera una base que ya existe: agrega a mano lo que se sumó después.
    private static void UpgradeExistingDatabase(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "Recurrings" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Recurrings" PRIMARY KEY AUTOINCREMENT,
                "Description" TEXT NOT NULL,
                "Amount" TEXT NOT NULL,
                "Type" INTEGER NOT NULL,
                "DayOfMonth" INTEGER NOT NULL,
                "AccountId" INTEGER NOT NULL,
                "CategoryId" INTEGER NOT NULL,
                CONSTRAINT "FK_Recurrings_Accounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_Recurrings_Categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "Categories" ("Id") ON DELETE CASCADE
            );
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "Budgets" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Budgets" PRIMARY KEY AUTOINCREMENT,
                "Currency" INTEGER NOT NULL,
                "Amount" TEXT NOT NULL,
                "CategoryId" INTEGER NOT NULL,
                CONSTRAINT "FK_Budgets_Categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "Categories" ("Id") ON DELETE CASCADE
            );
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "Goals" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Goals" PRIMARY KEY AUTOINCREMENT,
                "Name" TEXT NOT NULL,
                "Currency" INTEGER NOT NULL,
                "TargetAmount" TEXT NOT NULL,
                "SavedAmount" TEXT NOT NULL,
                "TargetDate" TEXT NOT NULL
            );
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "Transfers" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Transfers" PRIMARY KEY AUTOINCREMENT,
                "Date" TEXT NOT NULL,
                "AmountOut" TEXT NOT NULL,
                "AmountIn" TEXT NOT NULL,
                "Description" TEXT NOT NULL,
                "FromAccountId" INTEGER NOT NULL,
                "ToAccountId" INTEGER NOT NULL,
                CONSTRAINT "FK_Transfers_Accounts_FromAccountId" FOREIGN KEY ("FromAccountId") REFERENCES "Accounts" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_Transfers_Accounts_ToAccountId" FOREIGN KEY ("ToAccountId") REFERENCES "Accounts" ("Id") ON DELETE CASCADE
            );
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "Rates" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Rates" PRIMARY KEY AUTOINCREMENT,
                "Date" TEXT NOT NULL,
                "ArsPerUsd" TEXT NOT NULL
            );
            """);

        var hasColumn = db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM pragma_table_info('Transactions') WHERE name = 'RecurringId'")
            .AsEnumerable().First() > 0;
        if (!hasColumn)
            db.Database.ExecuteSqlRaw("""ALTER TABLE "Transactions" ADD COLUMN "RecurringId" INTEGER NULL""");
    }

    public static void Initialize()
    {
        using var db = new AppDbContext();
        db.Database.EnsureCreated();
        UpgradeExistingDatabase(db);

        if (db.Categories.Any()) return;

        string[] gastos =
        [
            "Alquiler", "Servicios", "Supermercado", "Transporte", "Salidas",
            "Salud", "Educación", "Suscripciones", "Impuestos", "Otros gastos"
        ];
        string[] ingresos = ["Sueldo", "Freelance", "Inversiones", "Otros ingresos"];

        db.Categories.AddRange(gastos.Select(n => new Category { Name = n, Type = TransactionType.Gasto }));
        db.Categories.AddRange(ingresos.Select(n => new Category { Name = n, Type = TransactionType.Ingreso }));
        db.Accounts.Add(new Account { Name = "Efectivo", Currency = Currency.ARS });
        db.SaveChanges();
    }
}
