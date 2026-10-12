namespace Finance.Core;

public enum CategoryKind { Income, Expense }
public enum RecurrenceKind { Fixed, Variable }

public sealed class Person
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public CategoryKind Kind { get; set; }
}

public sealed class Income
{
    public int Id { get; set; }
    public string Description { get; set; } = "";
    public decimal? Amount { get; set; }
    public bool IsConfirmed { get; set; } = true;
    public DateOnly Date { get; set; }
    public int PersonId { get; set; }
    public Person? Person { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public int? RecurrenceId { get; set; }
    public Recurrence? Recurrence { get; set; }
}

public sealed class Expense
{
    public int Id { get; set; }
    public string Description { get; set; } = "";
    public decimal? Amount { get; set; }
    public bool IsConfirmed { get; set; } = true;
    public DateOnly Date { get; set; }
    public int PersonId { get; set; }
    public Person? Person { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public int? RecurrenceId { get; set; }
    public Recurrence? Recurrence { get; set; }
    public int? InstallmentPlanId { get; set; }
    public InstallmentPlan? InstallmentPlan { get; set; }
    public int? InstallmentNumber { get; set; }
}

public sealed class Recurrence
{
    public int Id { get; set; }
    public string Description { get; set; } = "";
    public RecurrenceKind Kind { get; set; }
    public decimal? FixedAmount { get; set; }
    public int DueDay { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int PersonId { get; set; }
    public Person? Person { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public CategoryKind CategoryKind { get; set; } = CategoryKind.Expense;
    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
    public ICollection<Income> Incomes { get; set; } = new List<Income>();
}

public sealed class InstallmentPlan
{
    public int Id { get; set; }
    public string Description { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public int InstallmentCount { get; set; }
    public DateOnly FirstDueDate { get; set; }
    public int PersonId { get; set; }
    public Person? Person { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public ICollection<Expense> Installments { get; set; } = new List<Expense>();
}

public sealed record MonthlyBalance(int Year, int Month, decimal IncomeTotal, decimal ExpenseTotal, decimal Balance, int PendingExpenseCount, int PendingIncomeCount = 0, decimal PredictedIncomeTotal = 0m, decimal PredictedExpenseTotal = 0m);
