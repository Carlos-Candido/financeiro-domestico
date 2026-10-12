using Finance.Core;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure;

public sealed class FinanceService(FinanceDbContext db)
{
    public const string FamilySalaryDescription = "Salário mensal";
    public const string FamilySalaryCategoryName = "Salários da família";
    private const string FamilySalaryCategoryKey = "SALÁRIOS DA FAMÍLIA";
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
        var categoryEntity = await GetOrCreateCategoryAsync(category, CategoryKind.Income, cancellationToken);
        var income = new Income { Description = RequiredText(description, nameof(description)), Amount = amount, Date = date, PersonId = personId, CategoryId = categoryEntity.Id };
        db.Incomes.Add(income);
        await db.SaveChangesAsync(cancellationToken);
        return income;
    }

    public async Task<Income> AddPendingIncomeAsync(string description, DateOnly date, int personId, string category, CancellationToken cancellationToken = default)
    {
        var categoryEntity = await GetOrCreateCategoryAsync(category, CategoryKind.Income, cancellationToken);
        var income = new Income { Description = RequiredText(description, nameof(description)), Amount = null, IsConfirmed = false, Date = date, PersonId = personId, CategoryId = categoryEntity.Id };
        db.Incomes.Add(income);
        await db.SaveChangesAsync(cancellationToken);
        return income;
    }
    public async Task<Expense> AddExpenseAsync(string description, decimal amount, DateOnly date, int personId, string category, CancellationToken cancellationToken = default)
    {
        ValidateMoney(amount);
        var categoryEntity = await GetOrCreateCategoryAsync(category, CategoryKind.Expense, cancellationToken);
        var expense = new Expense { Description = RequiredText(description, nameof(description)), Amount = amount, Date = date, PersonId = personId, CategoryId = categoryEntity.Id };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync(cancellationToken);
        return expense;
    }

    public async Task<Expense> AddPendingExpenseAsync(string description, DateOnly date, int personId, string category, CancellationToken cancellationToken = default)
    {
        var categoryEntity = await GetOrCreateCategoryAsync(category, CategoryKind.Expense, cancellationToken);
        var expense = new Expense { Description = RequiredText(description, nameof(description)), Amount = null, IsConfirmed = false, Date = date, PersonId = personId, CategoryId = categoryEntity.Id };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync(cancellationToken);
        return expense;
    }

    public async Task ConfirmIncomeAsync(int id, CancellationToken cancellationToken = default)
    {
        var income = await db.Incomes.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException($"Income {id} not found.");
        if (income.Amount is null) throw new InvalidOperationException("Informe um valor antes de confirmar esta receita.");
        income.IsConfirmed = true;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ConfirmExpenseAsync(int id, CancellationToken cancellationToken = default)
    {
        var expense = await db.Expenses.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException($"Expense {id} not found.");
        if (expense.Amount is null) throw new InvalidOperationException("Informe um valor antes de confirmar esta despesa.");
        expense.IsConfirmed = true;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateIncomeAsync(int id, string description, decimal? amount, DateOnly date, int personId, string category, CancellationToken cancellationToken = default)
    {
        if (amount is not null) ValidateMoney(amount.Value);
        var income = await db.Incomes.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException($"Income {id} not found.");
        var categoryEntity = await GetOrCreateCategoryAsync(category, CategoryKind.Income, cancellationToken);
        income.Description = RequiredText(description, nameof(description));
        income.Amount = amount; income.IsConfirmed = amount is not null; income.Date = date; income.PersonId = personId; income.CategoryId = categoryEntity.Id;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateIncomeAmountAsync(int id, decimal amount, bool isConfirmed, CancellationToken cancellationToken = default)
    {
        ValidateMoney(amount);
        var income = await db.Incomes.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException($"Income {id} not found.");
        income.Amount = amount;
        income.IsConfirmed = isConfirmed;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateExpenseAsync(int id, string description, decimal? amount, DateOnly date, int personId, string category, CancellationToken cancellationToken = default)
    {
        if (amount is not null) ValidateMoney(amount.Value);
        var expense = await db.Expenses.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException($"Expense {id} not found.");
        var categoryEntity = await GetOrCreateCategoryAsync(category, CategoryKind.Expense, cancellationToken);
        expense.Description = RequiredText(description, nameof(description));
        expense.Amount = amount; expense.IsConfirmed = amount is not null; expense.Date = date; expense.PersonId = personId; expense.CategoryId = categoryEntity.Id;
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
        var incomes = await db.Incomes.Where(x => x.Date >= firstDay && x.Date <= lastDay).ToListAsync(cancellationToken);
        var expenses = await db.Expenses.Where(x => x.Date >= firstDay && x.Date <= lastDay).ToListAsync(cancellationToken);
        var incomeTotal = incomes.Where(x => x.IsConfirmed).Sum(x => x.Amount ?? 0m);
        var expenseTotal = expenses.Where(x => x.IsConfirmed).Sum(x => x.Amount ?? 0m);
        var predictedIncomeTotal = incomes.Where(x => !x.IsConfirmed && x.Amount is not null).Sum(x => x.Amount ?? 0m);
        var predictedExpenseTotal = expenses.Where(x => !x.IsConfirmed && x.Amount is not null).Sum(x => x.Amount ?? 0m);
        return new(year, month, incomeTotal, expenseTotal, incomeTotal - expenseTotal,
            expenses.Count(x => x.Amount is null), incomes.Count(x => x.Amount is null), predictedIncomeTotal, predictedExpenseTotal);
    }

    public Task<Recurrence> AddRecurrenceAsync(string description, RecurrenceKind kind, decimal? fixedAmount, int dueDay, DateOnly startDate, DateOnly? endDate, int personId, string category, CancellationToken cancellationToken = default)
        => AddRecurrenceAsync(description, kind, fixedAmount, dueDay, startDate, endDate, personId, category, CategoryKind.Expense, cancellationToken);

    public Task<Recurrence> AddIncomeRecurrenceAsync(string description, RecurrenceKind kind, decimal? fixedAmount, int dueDay, DateOnly startDate, DateOnly? endDate, int personId, string category, CancellationToken cancellationToken = default)
        => AddRecurrenceAsync(description, kind, fixedAmount, dueDay, startDate, endDate, personId, category, CategoryKind.Income, cancellationToken);

    private async Task<Recurrence> AddRecurrenceAsync(string description, RecurrenceKind kind, decimal? fixedAmount, int dueDay, DateOnly startDate, DateOnly? endDate, int personId, string category, CategoryKind categoryKind, CancellationToken cancellationToken)
    {
        if (dueDay is < 1 or > 31) throw new ArgumentOutOfRangeException(nameof(dueDay));
        if (endDate < startDate) throw new ArgumentException("End date must not precede start date.", nameof(endDate));
        if (kind == RecurrenceKind.Fixed)
        {
            if (fixedAmount is null) throw new ArgumentException("Fixed recurrence requires an amount.", nameof(fixedAmount));
            ValidateMoney(fixedAmount.Value);
        }
        else if (fixedAmount is not null)
            throw new ArgumentException("Variable recurrence must not have a fixed amount.", nameof(fixedAmount));

        var categoryEntity = await GetOrCreateCategoryAsync(category, categoryKind, cancellationToken);
        var recurrence = new Recurrence { Description = RequiredText(description, nameof(description)), Kind = kind, FixedAmount = fixedAmount, DueDay = dueDay, StartDate = startDate, EndDate = endDate, PersonId = personId, CategoryId = categoryEntity.Id, CategoryKind = categoryKind };
        db.Recurrences.Add(recurrence);
        await db.SaveChangesAsync(cancellationToken);
        return recurrence;
    }

    public async Task UpdateRecurrenceAsync(
        int id,
        string description,
        RecurrenceKind kind,
        decimal? fixedAmount,
        int dueDay,
        DateOnly? endDate,
        int personId,
        string category,
        CancellationToken cancellationToken = default)
    {
        var recurrence = await db.Recurrences.FindAsync([id], cancellationToken)
            ?? throw new KeyNotFoundException($"Recurrence {id} not found.");
        if (dueDay is < 1 or > 31) throw new ArgumentOutOfRangeException(nameof(dueDay));
        if (endDate < recurrence.StartDate) throw new ArgumentException("End date must not precede start date.", nameof(endDate));
        if (kind == RecurrenceKind.Fixed)
        {
            if (fixedAmount is null) throw new ArgumentException("Fixed recurrence requires an amount.", nameof(fixedAmount));
            ValidateMoney(fixedAmount.Value);
        }
        else if (fixedAmount is not null)
            throw new ArgumentException("Variable recurrence must not have a fixed amount.", nameof(fixedAmount));

        var categoryEntity = await GetOrCreateCategoryAsync(category, recurrence.CategoryKind, cancellationToken);
        recurrence.Description = RequiredText(description, nameof(description));
        recurrence.Kind = kind;
        recurrence.FixedAmount = fixedAmount;
        recurrence.DueDay = dueDay;
        recurrence.EndDate = endDate;
        recurrence.PersonId = personId;
        recurrence.CategoryId = categoryEntity.Id;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task EndRecurrenceAsync(int id, DateOnly endDate, CancellationToken cancellationToken = default)
    {
        var recurrence = await db.Recurrences.FindAsync([id], cancellationToken)
            ?? throw new KeyNotFoundException($"Recurrence {id} not found.");
        if (endDate < recurrence.StartDate)
            throw new ArgumentException("End date must not precede start date.", nameof(endDate));
        recurrence.EndDate = endDate;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Recurrence> SaveFamilySalaryDefaultAsync(int personId, decimal amount, int dueDay, DateOnly startsOn, CancellationToken cancellationToken = default)
    {
        ValidateMoney(amount);
        if (dueDay is < 1 or > 31) throw new ArgumentOutOfRangeException(nameof(dueDay));
        if (!await db.People.AnyAsync(x => x.Id == personId, cancellationToken))
            throw new KeyNotFoundException($"Person {personId} not found.");

        var matches = await FamilySalaryRecurrences().Where(x => x.PersonId == personId).ToListAsync(cancellationToken);
        if (matches.Count > 1)
            throw new InvalidOperationException("Há mais de um salário padrão cadastrado para esta pessoa.");

        if (matches.Count == 1)
        {
            var existing = matches[0];
            existing.Kind = RecurrenceKind.Fixed;
            existing.FixedAmount = amount;
            existing.DueDay = dueDay;
            await db.SaveChangesAsync(cancellationToken);
            return existing;
        }

        return await AddIncomeRecurrenceAsync(
            FamilySalaryDescription,
            RecurrenceKind.Fixed,
            amount,
            dueDay,
            startsOn,
            null,
            personId,
            FamilySalaryCategoryName,
            cancellationToken);
    }

    public async Task<FamilySalaryOverview> GetFamilySalaryOverviewAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var firstDay = new DateOnly(year, month, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);
        var recurrences = await FamilySalaryRecurrences()
            .Include(x => x.Person)
            .OrderBy(x => x.Person!.Name)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var recurrenceIds = recurrences.Select(x => x.Id).ToArray();
        var incomes = await db.Incomes
            .Where(x => x.RecurrenceId != null && recurrenceIds.Contains(x.RecurrenceId.Value)
                && x.Date >= firstDay && x.Date <= lastDay)
            .ToDictionaryAsync(x => x.RecurrenceId!.Value, cancellationToken);

        var rows = new List<FamilySalaryRow>(recurrences.Count);
        foreach (var recurrence in recurrences)
        {
            var dueDate = new DateOnly(year, month, Math.Min(recurrence.DueDay, lastDay.Day));
            var isScheduled = dueDate >= recurrence.StartDate
                && (recurrence.EndDate is null || dueDate <= recurrence.EndDate.Value);
            incomes.TryGetValue(recurrence.Id, out var income);
            var monthlyAmount = isScheduled
                ? income is null ? recurrence.FixedAmount : income.Amount
                : null;
            rows.Add(new FamilySalaryRow(
                recurrence.PersonId,
                recurrence.Person!.Name,
                recurrence.Id,
                recurrence.FixedAmount!.Value,
                recurrence.DueDay,
                isScheduled ? dueDate : null,
                income?.Id,
                monthlyAmount,
                income is { IsConfirmed: true, Amount: not null }));
        }

        return new FamilySalaryOverview(
            rows,
            rows.Sum(x => x.MonthAmount ?? 0m),
            rows.Where(x => x.IsReceived).Sum(x => x.MonthAmount ?? 0m));
    }

    private IQueryable<Recurrence> FamilySalaryRecurrences()
        => db.Recurrences.Where(x =>
            x.CategoryKind == CategoryKind.Income
            && x.Description == FamilySalaryDescription
            && db.Categories.Any(category =>
                category.Id == x.CategoryId
                && category.Kind == CategoryKind.Income
                && category.NormalizedName == FamilySalaryCategoryKey));

    public async Task<int> GenerateRecurrencesAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var firstDay = new DateOnly(year, month, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);
        var recurrences = await db.Recurrences
            .Where(x => x.StartDate <= lastDay && (x.EndDate == null || x.EndDate >= firstDay))
            .ToListAsync(cancellationToken);
        var ids = recurrences.Select(x => x.Id).ToArray();
        var existingExpenseIds = (await db.Expenses.Where(x => x.RecurrenceId != null && ids.Contains(x.RecurrenceId.Value) && x.Date >= firstDay && x.Date <= lastDay)
            .Select(x => x.RecurrenceId!.Value).ToListAsync(cancellationToken)).ToHashSet();
        var existingIncomeIds = (await db.Incomes.Where(x => x.RecurrenceId != null && ids.Contains(x.RecurrenceId.Value) && x.Date >= firstDay && x.Date <= lastDay)
            .Select(x => x.RecurrenceId!.Value).ToListAsync(cancellationToken)).ToHashSet();
        var created = 0;
        foreach (var recurrence in recurrences)
        {
            var dueDate = new DateOnly(year, month, Math.Min(recurrence.DueDay, lastDay.Day));
            if (dueDate < recurrence.StartDate || (recurrence.EndDate is { } endDate && dueDate > endDate)) continue;
            if (recurrence.CategoryKind == CategoryKind.Income)
            {
                if (existingIncomeIds.Contains(recurrence.Id)) continue;
                db.Incomes.Add(new Income { Description = recurrence.Description, Amount = recurrence.Kind == RecurrenceKind.Fixed ? recurrence.FixedAmount : null, IsConfirmed = false, Date = dueDate, PersonId = recurrence.PersonId, CategoryId = recurrence.CategoryId, RecurrenceId = recurrence.Id });
            }
            else
            {
                if (existingExpenseIds.Contains(recurrence.Id)) continue;
                db.Expenses.Add(new Expense { Description = recurrence.Description, Amount = recurrence.Kind == RecurrenceKind.Fixed ? recurrence.FixedAmount : null, IsConfirmed = false, Date = dueDate, PersonId = recurrence.PersonId, CategoryId = recurrence.CategoryId, RecurrenceId = recurrence.Id });
            }
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
        var categoryEntity = await GetOrCreateCategoryAsync(category, CategoryKind.Expense, cancellationToken);
        var plan = new InstallmentPlan { Description = RequiredText(description, nameof(description)), TotalAmount = totalAmount, InstallmentCount = installmentCount, FirstDueDate = firstDueDate, PersonId = personId, CategoryId = categoryEntity.Id };
        db.InstallmentPlans.Add(plan);
        var baseCents = decimal.Truncate(totalCents / installmentCount);
        var remainderCents = decimal.ToInt32(totalCents - (baseCents * installmentCount));
        for (var index = 0; index < installmentCount; index++)
        {
            var installmentAmount = (baseCents + (index < remainderCents ? 1 : 0)) / 100m;
            var dueDate = AddMonthsClamped(firstDueDate, index);
            plan.Installments.Add(new Expense { Description = plan.Description, Amount = installmentAmount, Date = dueDate, PersonId = personId, CategoryId = categoryEntity.Id, InstallmentNumber = index + 1 });
        }
        await db.SaveChangesAsync(cancellationToken);
        return plan;
    }

    private async Task<Category> GetOrCreateCategoryAsync(string name, CategoryKind kind, CancellationToken cancellationToken)
    {
        var displayName = NormalizeSpaces(name);
        if (displayName.Length == 0) throw new ArgumentException("Category is required.", nameof(name));
        var key = displayName.ToUpperInvariant();
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Kind == kind && x.NormalizedName == key, cancellationToken);
        if (category is not null) return category;
        category = new Category { Name = displayName, NormalizedName = key, Kind = kind };
        db.Categories.Add(category);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return category;
        }
        catch (DbUpdateException)
        {
            db.Entry(category).State = EntityState.Detached;
            return await db.Categories.SingleAsync(x => x.Kind == kind && x.NormalizedName == key, cancellationToken);
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
        if (amount != decimal.Round(amount, 2)) throw new ArgumentException("Money values may have at most two decimal places.", nameof(amount));
    }

    private static DateOnly AddMonthsClamped(DateOnly date, int months)
    {
        var month = new DateOnly(date.Year, date.Month, 1).AddMonths(months);
        return new DateOnly(month.Year, month.Month, Math.Min(date.Day, DateTime.DaysInMonth(month.Year, month.Month)));
    }
}

public sealed record FamilySalaryRow(
    int PersonId,
    string PersonName,
    int RecurrenceId,
    decimal StandardAmount,
    int DueDay,
    DateOnly? DueDate,
    int? IncomeId,
    decimal? MonthAmount,
    bool IsReceived);

public sealed record FamilySalaryOverview(
    IReadOnlyList<FamilySalaryRow> Salaries,
    decimal ExpectedTotal,
    decimal ReceivedTotal);
