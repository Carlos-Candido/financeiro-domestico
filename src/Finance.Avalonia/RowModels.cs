using System.Globalization;

namespace Finance.Avalonia;

/// <summary>Linha exibível de um lançamento (receita ou despesa) do mês.</summary>
internal sealed record EntryRow(int Id, bool IsIncome, int PersonId, DateOnly Date, string Description,
    string DisplayDescription, string Category, string Person, decimal? Amount, string Status)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public string DateText => Date.ToString("dd/MM/yyyy", PtBr);
    public string AmountText => Amount?.ToString("C", PtBr) ?? "A informar";
}

/// <summary>Linha exibível de uma recorrência cadastrada.</summary>
internal sealed record RecurrenceRow(string Description, string CategoryKind, string Kind, decimal? Amount,
    int DueDay, string Person, string Category, DateOnly StartDate, DateOnly? EndDate)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public string ValueText => Amount?.ToString("C", PtBr) ?? "Variável";
    public string DueDayText => $"Dia {DueDay}";
    public string PeriodText => EndDate is { } end ? $"{StartDate:dd/MM/yyyy} — {end:dd/MM/yyyy}" : $"{StartDate:dd/MM/yyyy} — em aberto";
}
