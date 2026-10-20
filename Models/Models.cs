namespace CholinanzasApp.Models;

public enum Currency { ARS, USD }

public enum TransactionType { Gasto, Ingreso }

public class Account
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public Currency Currency { get; set; }
    public decimal InitialBalance { get; set; }
}

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public TransactionType Type { get; set; }
}

/// <summary>Si la descripción de un movimiento contiene Pattern, se le asigna esa categoría.</summary>
public class CategoryRule
{
    public int Id { get; set; }
    public string Pattern { get; set; } = "";

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}

public class FinanceTransaction
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
    public string Description { get; set; } = "";

    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    /// <summary>Si viene de un movimiento recurrente, su Id (para no proyectarlo dos veces).</summary>
    public int? RecurringId { get; set; }
}

/// <summary>Movimiento de plata entre dos cuentas propias; no cuenta como ingreso ni gasto.</summary>
public class Transfer
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public decimal AmountOut { get; set; }
    public decimal AmountIn { get; set; }
    public string Description { get; set; } = "";

    public int FromAccountId { get; set; }
    public Account FromAccount { get; set; } = null!;

    public int ToAccountId { get; set; }
    public Account ToAccount { get; set; } = null!;
}

/// <summary>Cotización cargada a mano: pesos por 1 USD. Se usa la más reciente.</summary>
public class ExchangeRate
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public decimal ArsPerUsd { get; set; }
}

/// <summary>Límite mensual de gasto para una categoría en una moneda.</summary>
public class Budget
{
    public int Id { get; set; }
    public Currency Currency { get; set; }
    public decimal Amount { get; set; }

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}

public class SavingsGoal
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public Currency Currency { get; set; }
    public decimal TargetAmount { get; set; }
    public decimal SavedAmount { get; set; }
    public DateTime TargetDate { get; set; }
}

public class RecurringTransaction
{
    public int Id { get; set; }
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
    public int DayOfMonth { get; set; }

    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}
