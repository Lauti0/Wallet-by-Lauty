using CholinanzasApp.Models;

namespace CholinanzasApp.Services;

/// <summary>Cálculos de las compras en cuotas: qué cuota toca cuándo, cuántas se pagaron y cuánto falta.</summary>
public static class InstallmentService
{
    public const int MaxCount = 120;

    /// <summary>Fecha de la cuota <paramref name="number"/> (1 = la primera). Si el mes no tiene ese día, cae el último día.</summary>
    public static DateTime DateOf(InstallmentPlan plan, int number) => plan.FirstDate.Date.AddMonths(number - 1);

    /// <summary>Cuotas pagadas: las que ya estaban pagadas al cargar el plan más las que se registraron como movimiento.</summary>
    public static int Paid(InstallmentPlan plan, IEnumerable<int> registeredNumbers) =>
        Math.Min(plan.Count, plan.PaidBefore + registeredNumbers.Where(n => n > plan.PaidBefore && n <= plan.Count).Distinct().Count());

    public static decimal Remaining(InstallmentPlan plan, IEnumerable<int> registeredNumbers) =>
        (plan.Count - Paid(plan, registeredNumbers)) * plan.InstallmentAmount;

    /// <summary>Números de cuota que todavía no se registraron (las ya pagadas antes de cargar el plan no cuentan).</summary>
    public static IEnumerable<int> Unregistered(InstallmentPlan plan, IEnumerable<int> registeredNumbers)
    {
        var done = registeredNumbers.ToHashSet();
        for (var n = plan.PaidBefore + 1; n <= plan.Count; n++)
            if (!done.Contains(n)) yield return n;
    }

    /// <summary>Texto de avance: "cuota 4 de 12" (la próxima a pagar) o "pagado" si ya terminó.</summary>
    public static string Progress(InstallmentPlan plan, IEnumerable<int> registeredNumbers)
    {
        var paid = Paid(plan, registeredNumbers);
        return paid >= plan.Count ? $"{plan.Count} de {plan.Count} · pagado" : $"{paid} de {plan.Count} pagadas";
    }
}
