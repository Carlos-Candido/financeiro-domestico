using Finance.Core;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure;

public sealed class FinanceService(FinanceDbContext db)
{
    public async Task<Person> AddPersonAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var person = new Person { Name = name.Trim() };
        db.People.Add(person);
        await db.SaveChangesAsync(cancellationToken);
        return person;
    }

    public async Task<Income> AddIncomeAsync(string description, decimal amount, DateOnly date, int personId, string category, CancellationToken cancellationToken = default)
    {
        ValidateMoney(amount);
        var categoryEntity = await GetOrCreateCategoryAsync(category, cancellationToken);
        var income = new Income { Description = RequiredText(description, nameof(description)), Amount = amount, Date = date, PersonId = personId, CategoryId = categoryEntity.Id };
        db.Incomes.Add(income);
        await db.SaveChangesAsync(cancellationToken);
        return income;
    }

    public async Task<Expense> AddExpenseAsync(string description, decimal amount, DateOnly date, int personId, string category, CancellationToken cancellationToken = default)
    {
        ValidateMoney(amount);
        var categoryEntity = await GetOrCreateCategoryAsync(category, cancellationToken);
        var expense = new Expense { Description = RequiredText(description, nameof(description)), Amount = amount, Date = date, PersonId = personId, CategoryId = categoryEntity.Id };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync(cancellationToken);
        return expense;
    }

    public async Task<Expense> AddPendingExpenseAsync(string description, DateOnly date, int personId, string category, CancellationToken cancellationToken = default)
    {
        var categoryEntity = await GetOrCreateCategoryAsync(category, cancellationToken);
        var expense = new Expense { Description = RequiredText(description, nameof(description)), Amount = null, Date = date, PersonId = personId, CategoryId = categoryEntity.Id };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync(cancellationToken);
        return expense;
    }

    public async Task UpdateIncomeAsync(int id, string description, decimal amount, DateOnly date, int personId, string category, CancellationToken cancellationToken = default)
    {
        ValidateMoney(amount);
        var income = await db.Incomes.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException($"Income {id} not found.");
        var categoryEntity = await GetOrCreateCategoryAsync(category, cancellationToken);
        income.Description = RequiredText(description, nameof(description));
        income.Amount = amount;
        income.Date = date;
        income.PersonId = personId;
        income.CategoryId = categoryEntity.Id;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateExpenseAsync(int id, string description, decimal? amount, DateOnly date, int personId, string category, CancellationToken cancellationToken = default)
    {
        if (amount is not null) ValidateMoney(amount.Value);
        var expense = await db.Expenses.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException($"Expense {id} not found.");
        var categoryEntity = await GetOrCreateCategoryAsync(category, cancellationToken);
        expense.Description = RequiredText(description, nameof(description));
        expense.Amount = amount;
        expense.Date = date;
        expense.PersonId = personId;
        expense.CategoryId = categoryEntity.Id;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteIncomeAsync(int id, CancellationToken cancellationToken = default)
    {
        var income = await db.Incomes.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException($"Income {id} not found.");
        db.Incomes.Remove(income);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteExpenseAsync(int id, CancellationToken cancellationToken = default)
    {
        var expense = await db.Expenses.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException($"Expense {id} not found.");
        db.Expenses.Remove(expense);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<MonthlyBalance> GetMonthlyBalanceAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var firstDay = new DateOnly(year, month, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);
        var incomes = await db.Incomes.Where(x => x.Date >= firstDay && x.Date <= lastDay).Select(x => x.Amount).ToListAsync(cancellationToken);
        var expenses = await db.Expenses.Where(x => x.Date >= firstDay && x.Date <= lastDay).Select(x => x.Amount).ToListAsync(cancellationToken);
        var incomeTotal = incomes.Sum();
        var expenseTotal = expenses.Sum(x => x ?? 0m);
        return new(year, month, incomeTotal, expenseTotal, incomeTotal - expenseTotal, expenses.Count(x => x is null));
    }

    public async Task<Recurrence> AddRecurrenceAsync(string description, RecurrenceKind kind, decimal? fixedAmount, int dueDay, DateOnly startDate, DateOnly? endDate, int personId, string category, CancellationToken cancellationToken = default)
    {
        if (dueDay is < 1 or > 31) throw new ArgumentOutOfRangeException(nameof(dueDay));
        if (endDate is { } lastDate && lastDate < startDate) throw new ArgumentException("End date must not precede start date.", nameof(endDate));
        if (kind == RecurrenceKind.Fixed)
        {
            if (fixedAmount is null) throw new ArgumentException("Fixed recurrence requires an amount.", nameof(fixedAmount));
            ValidateMoney(fixedAmount.Value);
        }
        else if (fixedAmount is not null)
        {
            throw new ArgumentException("Variable recurrence must not have a fixed amount.", nameof(fixedAmount));
        }

        var categoryEntity = await GetOrCreateCategoryAsync(category, cancellationToken);
        var recurrence = new Recurrence { Description = RequiredText(description, nameof(description)), Kind = kind, FixedAmount = fixedAmount, DueDay = dueDay, StartDate = startDate, EndDate = endDate, PersonId = personId, CategoryId = categoryEntity.Id };
        db.Recurrences.Add(recurrence);
        await db.SaveChangesAsync(cancellationToken);
        return recurrence;
    }

    public async Task<int> GenerateRecurrencesAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var firstDay = new DateOnly(year, month, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);
        var recurrences = await db.Recurrences
            .Where(x => x.StartDate <= lastDay && (x.EndDate == null || x.EndDate >= firstDay))
            .ToListAsync(cancellationToken);
        var ids = recurrences.Select(x => x.Id).ToArray();
        var existingIds = await db.Expenses
            .Where(x => x.RecurrenceId != null && ids.Contains(x.RecurrenceId.Value) && x.RecurrenceMonth == firstDay)
            .Select(x => x.RecurrenceId!.Value)
            .ToListAsync(cancellationToken);
        var existing = existingIds.ToHashSet();
        var created = 0;
        foreach (var recurrence in recurrences)
        {
            var dueDate = new DateOnly(year, month, Math.Min(recurrence.DueDay, lastDay.Day));
            if (existing.Contains(recurrence.Id) || dueDate < recurrence.StartDate || (recurrence.EndDate is { } endDate && dueDate > endDate)) continue;
            db.Expenses.Add(new Expense { Description = recurrence.Description, Amount = recurrence.Kind == RecurrenceKind.Fixed ? recurrence.FixedAmount : null, Date = dueDate, PersonId = recurrence.PersonId, CategoryId = recurrence.CategoryId, RecurrenceId = recurrence.Id, RecurrenceMonth = firstDay });
            created++;
        }
        await db.SaveChangesAsync(cancellationToken);
        return created;
    }

    public async Task<InstallmentPlan> CreateInstallmentPlanAsync(string description, decimal totalAmount, int installmentCount, DateOnly firstDueDate, int personId, string category, CancellationToken cancellationToken = default)
    {
        ValidateMoney(totalAmount);
        if (installmentCount < 1) throw new ArgumentOutOfRangeException(nameof(installmentCount));
        var totalCents = totalAmount * 100m;
        if (totalCents < installmentCount) throw new ArgumentException("Every installment must be at least one cent.", nameof(installmentCount));
        var categoryEntity = await GetOrCreateCategoryAsync(category, cancellationToken);
        var plan = new InstallmentPlan { Description = RequiredText(description, nameof(description)), TotalAmount = totalAmount, InstallmentCount = installmentCount, FirstDueDate = firstDueDate, PersonId = personId, CategoryId = categoryEntity.Id };
        db.InstallmentPlans.Add(plan);
        var baseCents = decimal.Truncate(totalCents / installmentCount);
        var remainderCents = decimal.ToInt32(totalCents - baseCents * installmentCount);
        for (var index = 0; index < installmentCount; index++)
        {
            var installmentAmount = (baseCents + (index < remainderCents ? 1 : 0)) / 100m;
            plan.Installments.Add(new Expense { Description = plan.Description, Amount = installmentAmount, Date = AddMonthsClamped(firstDueDate, index), PersonId = personId, CategoryId = categoryEntity.Id, InstallmentNumber = index + 1 });
        }
        await db.SaveChangesAsync(cancellationToken);
        return plan;
    }

    private async Task<Category> GetOrCreateCategoryAsync(string name, CancellationToken cancellationToken)
    {
        var displayName = NormalizeSpaces(name);
        if (displayName.Length == 0) throw new ArgumentException("Category is required.", nameof(name));
        var key = displayName.ToUpperInvariant();
        var category = await db.Categories.SingleOrDefaultAsync(x => x.NormalizedName == key, cancellationToken);
        if (category is not null) return category;
        category = new Category { Name = displayName, NormalizedName = key };
        db.Categories.Add(category);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return category;
        }
        catch (DbUpdateException)
        {
            db.Entry(category).State = EntityState.Detached;
            return await db.Categories.SingleAsync(x => x.NormalizedName == key, cancellationToken);
        }
    }

    private static string NormalizeSpaces(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string RequiredText(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        return value.Trim();
    }

    private static void ValidateMoney(decimal amount)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive.");
        if (amount > 9999999999999999.99m) throw new ArgumentOutOfRangeException(nameof(amount), "Amount exceeds the supported precision.");
        if (amount != decimal.Round(amount, 2)) throw new ArgumentException("Money values may have at most two decimal places.", nameof(amount));
    }

    private static DateOnly AddMonthsClamped(DateOnly date, int months)
    {
        var month = new DateOnly(date.Year, date.Month, 1).AddMonths(months);
        return new DateOnly(month.Year, month.Month, Math.Min(date.Day, DateTime.DaysInMonth(month.Year, month.Month)));
    }
}
