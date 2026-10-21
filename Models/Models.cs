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

    /// <summary>Si es una cuota de una compra en cuotas, el Id del plan y el número de cuota (1 = la primera).</summary>
    public int? InstallmentPlanId { get; set; }
    public int? InstallmentNumber { get; set; }
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

    /// <summary>De dónde salió: "Manual" o, si se trajo de internet, por ejemplo "Blue (venta)". Null en las cargadas antes.</summary>
    public string? Source { get; set; }
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

/// <summary>
/// Compra en cuotas (por ejemplo con tarjeta de crédito). Cada cuota se cobra un mes después de la anterior,
/// a partir de FirstDate, y se registra como un gasto cuando se concreta.
/// </summary>
public class InstallmentPlan
{
    public int Id { get; set; }
    public string Description { get; set; } = "";
    public decimal InstallmentAmount { get; set; }
    public int Count { get; set; }

    /// <summary>Fecha en que se cobra la cuota 1 (en la tarjeta, la del vencimiento del resumen).</summary>
    public DateTime FirstDate { get; set; }

    /// <summary>Cuotas que ya estaban pagadas al cargar el plan (no se registran como movimientos).</summary>
    public int PaidBefore { get; set; }

    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}
