using Microsoft.Data.Sqlite;
using Finance.Core;
using Finance.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Finance.Tests;

public sealed class FinanceServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FinanceDbContext _db;
    private readonly FinanceService _service;
    private readonly Person _person;

    public FinanceServiceTests()
    {
        SqliteProviderConfiguration.Initialize();
        _connection.Open();
        _db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _service = new FinanceService(_db);
        _person = _service.AddPersonAsync("Ana").GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Categories_ignore_case_and_repeated_whitespace()
    {
        await _service.AddExpenseAsync("Mercado", 10m, new DateOnly(2026, 1, 4), _person.Id, "  Casa   e mercado ");
        await _service.AddExpenseAsync("Feira", 12m, new DateOnly(2026, 1, 5), _person.Id, "cAsA e MERCADO");

        Assert.Equal(1, await _db.Categories.CountAsync(x => x.Kind == CategoryKind.Expense));
    }

    [Fact]
    public async Task Monthly_balance_can_be_negative_and_reports_pending_expenses()
    {
        await _service.AddIncomeAsync("Salário", 100m, new DateOnly(2026, 3, 1), _person.Id, "Salário");
        await _service.AddExpenseAsync("Aluguel", 150m, new DateOnly(2026, 3, 2), _person.Id, "Moradia");
        await _service.AddPendingExpenseAsync("Conta de luz", new DateOnly(2026, 3, 18), _person.Id, "Energia");
        await _service.AddExpenseAsync("Outro mês", 200m, new DateOnly(2026, 4, 1), _person.Id, "Moradia");

        var balance = await _service.GetMonthlyBalanceAsync(2026, 3);

        Assert.Equal(100m, balance.IncomeTotal);
        Assert.Equal(150m, balance.ExpenseTotal);
        Assert.Equal(-50m, balance.Balance);
        Assert.Equal(1, balance.PendingExpenseCount);
    }

    [Fact]
    public async Task Recurrences_generate_fixed_and_pending_expenses_once_per_month()
    {
        await _service.AddRecurrenceAsync("Aluguel", RecurrenceKind.Fixed, 900m, 31, new DateOnly(2026, 1, 1), null, _person.Id, "Moradia");
        await _service.AddRecurrenceAsync("Energia", RecurrenceKind.Variable, null, 10, new DateOnly(2026, 1, 1), null, _person.Id, "Contas");

        Assert.Equal(2, await _service.GenerateRecurrencesAsync(2026, 2));
        Assert.Equal(0, await _service.GenerateRecurrencesAsync(2026, 2));
        var expenses = await _db.Expenses.ToListAsync();
        var fixedExpense = expenses.Single(x => x.Amount is not null);
        var pendingExpense = expenses.Single(x => x.Amount is null);
        Assert.Equal(new DateOnly(2026, 2, 28), fixedExpense.Date);
        Assert.Equal(900m, fixedExpense.Amount);
        Assert.False(fixedExpense.IsConfirmed);
        Assert.False(pendingExpense.IsConfirmed);

        var balance = await _service.GetMonthlyBalanceAsync(2026, 2);
        Assert.Equal(0m, balance.ExpenseTotal);
        Assert.Equal(900m, balance.PredictedExpenseTotal);
        Assert.Equal(1, balance.PendingExpenseCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ConfirmExpenseAsync(pendingExpense.Id));
        await _service.ConfirmExpenseAsync(fixedExpense.Id);
        balance = await _service.GetMonthlyBalanceAsync(2026, 2);
        Assert.Equal(900m, balance.ExpenseTotal);
        Assert.Equal(0m, balance.PredictedExpenseTotal);
    }

    [Fact]
    public async Task Income_recurrences_are_predicted_or_pending_and_confirm_only_one_month()
    {
        await _service.AddIncomeRecurrenceAsync("Salário", RecurrenceKind.Fixed, 2500m, 5, new DateOnly(2026, 1, 1), null, _person.Id, "Renda");
        await _service.AddIncomeRecurrenceAsync("Comissão", RecurrenceKind.Variable, null, 15, new DateOnly(2026, 1, 1), null, _person.Id, "Renda variável");

        Assert.Equal(2, await _service.GenerateRecurrencesAsync(2026, 2));
        Assert.Equal(0, await _service.GenerateRecurrencesAsync(2026, 2));
        Assert.Equal(2, await _service.GenerateRecurrencesAsync(2026, 3));
        var february = await _db.Incomes.Where(x => x.Date.Month == 2).OrderBy(x => x.Date).ToListAsync();
        var balance = await _service.GetMonthlyBalanceAsync(2026, 2);
        Assert.Equal(0m, balance.IncomeTotal);
        Assert.Equal(2500m, balance.PredictedIncomeTotal);
        Assert.Equal(1, balance.PendingIncomeCount);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ConfirmIncomeAsync(february[1].Id));
        await _service.ConfirmIncomeAsync(february[0].Id);
        balance = await _service.GetMonthlyBalanceAsync(2026, 2);
        Assert.Equal(2500m, balance.IncomeTotal);
        Assert.Equal(0m, balance.PredictedIncomeTotal);
        Assert.Equal(1, balance.PendingIncomeCount);
        var marchBalance = await _service.GetMonthlyBalanceAsync(2026, 3);
        Assert.Equal(0m, marchBalance.IncomeTotal);
        Assert.Equal(2500m, marchBalance.PredictedIncomeTotal);
        Assert.Equal(1, marchBalance.PendingIncomeCount);

        await _service.UpdateIncomeAsync(february[1].Id, february[1].Description, 175m, february[1].Date, _person.Id, "Renda variável");
        balance = await _service.GetMonthlyBalanceAsync(2026, 2);
        Assert.Equal(2675m, balance.IncomeTotal);
        Assert.Equal(0, balance.PendingIncomeCount);
        marchBalance = await _service.GetMonthlyBalanceAsync(2026, 3);
        Assert.Equal(0m, marchBalance.IncomeTotal);
        Assert.Equal(2500m, marchBalance.PredictedIncomeTotal);
        Assert.Equal(1, marchBalance.PendingIncomeCount);
    }
    [Fact]
    public async Task Recurrence_edits_change_only_ungenerated_occurrences_and_ending_preserves_existing_rows()
    {
        var recurrence = await _service.AddRecurrenceAsync(
            "Internet",
            RecurrenceKind.Fixed,
            100m,
            5,
            new DateOnly(2026, 1, 1),
            null,
            _person.Id,
            "Contas");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.EndRecurrenceAsync(recurrence.Id, new DateOnly(2025, 12, 31)));
        Assert.Null((await _db.Recurrences.FindAsync(recurrence.Id))!.EndDate);

        Assert.Equal(1, await _service.GenerateRecurrencesAsync(2026, 2));
        Assert.Equal(1, await _service.GenerateRecurrencesAsync(2026, 3));
        await _service.UpdateRecurrenceAsync(
            recurrence.Id,
            "Internet",
            RecurrenceKind.Fixed,
            125m,
            10,
            new DateOnly(2026, 4, 10),
            _person.Id,
            "Contas da casa");

        var february = await _db.Expenses.SingleAsync(x => x.Date.Month == 2);
        var march = await _db.Expenses.SingleAsync(x => x.Date.Month == 3);
        Assert.Equal(100m, february.Amount);
        Assert.Equal(new DateOnly(2026, 2, 5), february.Date);
        Assert.Equal(100m, march.Amount);
        Assert.Equal(new DateOnly(2026, 3, 5), march.Date);

        Assert.Equal(1, await _service.GenerateRecurrencesAsync(2026, 4));
        Assert.Equal(0, await _service.GenerateRecurrencesAsync(2026, 4));
        var april = await _db.Expenses.SingleAsync(x => x.Date.Month == 4);
        Assert.Equal(125m, april.Amount);
        Assert.Equal(new DateOnly(2026, 4, 10), april.Date);
        Assert.Equal("Contas da casa", (await _db.Categories.FindAsync(april.CategoryId))!.Name);
        Assert.Equal(CategoryKind.Expense, (await _db.Recurrences.FindAsync(recurrence.Id))!.CategoryKind);

        await _service.EndRecurrenceAsync(recurrence.Id, new DateOnly(2026, 3, 31));
        Assert.Equal(0, await _service.GenerateRecurrencesAsync(2026, 5));
        Assert.Equal(3, await _db.Expenses.CountAsync());
        Assert.Equal(new DateOnly(2026, 3, 31), (await _db.Recurrences.FindAsync(recurrence.Id))!.EndDate);
    }



    [Fact]
    public async Task Family_salaries_are_idempotent_and_month_edits_do_not_change_future_defaults()
    {
        var mother = await _service.AddPersonAsync("Mãe");
        await _service.SaveFamilySalaryDefaultAsync(_person.Id, 3000m, 5, new DateOnly(2026, 1, 1));
        await _service.SaveFamilySalaryDefaultAsync(mother.Id, 2400m, 10, new DateOnly(2026, 1, 1));
        await _service.SaveFamilySalaryDefaultAsync(_person.Id, 3100m, 6, new DateOnly(2026, 2, 1));
        Assert.Equal(2, await _db.Recurrences.CountAsync(x => x.CategoryKind == CategoryKind.Income));

        Assert.Equal(2, await _service.GenerateRecurrencesAsync(2026, 2));
        Assert.Equal(0, await _service.GenerateRecurrencesAsync(2026, 2));
        var february = await _service.GetFamilySalaryOverviewAsync(2026, 2);
        Assert.Equal(5500m, february.ExpectedTotal);
        Assert.Equal(0m, february.ReceivedTotal);
        var fatherFebruary = february.Salaries.Single(x => x.PersonId == _person.Id);
        var motherFebruary = february.Salaries.Single(x => x.PersonId == mother.Id);
        Assert.Equal(3100m, fatherFebruary.MonthAmount);
        Assert.Equal(6, fatherFebruary.DueDate!.Value.Day);
        Assert.Equal(2400m, motherFebruary.MonthAmount);
        Assert.Equal(10, motherFebruary.DueDate!.Value.Day);

        await _service.UpdateIncomeAmountAsync(fatherFebruary.IncomeId!.Value, 3250m, isConfirmed: false);
        february = await _service.GetFamilySalaryOverviewAsync(2026, 2);
        Assert.Equal(5650m, february.ExpectedTotal);
        Assert.Equal(0m, february.ReceivedTotal);
        var balance = await _service.GetMonthlyBalanceAsync(2026, 2);
        Assert.Equal(0m, balance.IncomeTotal);
        Assert.Equal(5650m, balance.PredictedIncomeTotal);

        await _service.ConfirmIncomeAsync(fatherFebruary.IncomeId.Value);
        february = await _service.GetFamilySalaryOverviewAsync(2026, 2);
        Assert.Equal(5650m, february.ExpectedTotal);
        Assert.Equal(3250m, february.ReceivedTotal);
        balance = await _service.GetMonthlyBalanceAsync(2026, 2);
        Assert.Equal(3250m, balance.IncomeTotal);
        Assert.Equal(2400m, balance.PredictedIncomeTotal);

        await _service.SaveFamilySalaryDefaultAsync(_person.Id, 3500m, 7, new DateOnly(2026, 3, 1));
        Assert.Equal(2, await _db.Recurrences.CountAsync(x => x.CategoryKind == CategoryKind.Income));
        Assert.Equal(2, await _service.GenerateRecurrencesAsync(2026, 3));
        Assert.Equal(0, await _service.GenerateRecurrencesAsync(2026, 3));
        var march = await _service.GetFamilySalaryOverviewAsync(2026, 3);
        Assert.Equal(5900m, march.ExpectedTotal);
        Assert.Equal(0m, march.ReceivedTotal);
        var fatherMarch = march.Salaries.Single(x => x.PersonId == _person.Id);
        Assert.Equal(3500m, fatherMarch.StandardAmount);
        Assert.Equal(3500m, fatherMarch.MonthAmount);
        Assert.Equal(7, fatherMarch.DueDate!.Value.Day);

        february = await _service.GetFamilySalaryOverviewAsync(2026, 2);
        Assert.Equal(3500m, february.Salaries.Single(x => x.PersonId == _person.Id).StandardAmount);
        Assert.Equal(3250m, february.Salaries.Single(x => x.PersonId == _person.Id).MonthAmount);
        Assert.Equal(3250m, february.ReceivedTotal);
    }

    [Fact]
    public async Task Installments_distribute_remaining_cents_and_clamp_month_end()
    {
        var plan = await _service.CreateInstallmentPlanAsync("Compra", 100m, 3, new DateOnly(2026, 1, 31), _person.Id, "Compras");
        var installments = await _db.Expenses.Where(x => x.InstallmentPlanId == plan.Id).OrderBy(x => x.InstallmentNumber).ToListAsync();

        Assert.Equal(new[] { 33.34m, 33.33m, 33.33m }, installments.Select(x => x.Amount!.Value).ToArray());
        Assert.Equal(100m, installments.Sum(x => x.Amount ?? 0m));
        Assert.All(installments, item => Assert.Equal(plan.Id, item.InstallmentPlanId));
        Assert.Equal(new DateOnly(2026, 2, 28), installments[1].Date);
    }

    [Fact]
    public async Task Income_and_expense_can_be_edited_and_deleted()
    {
        var income = await _service.AddIncomeAsync("Extra", 20m, new DateOnly(2026, 1, 1), _person.Id, "Extra");
        var expense = await _service.AddExpenseAsync("Lanche", 5m, new DateOnly(2026, 1, 2), _person.Id, "Comida");
        await _service.UpdateIncomeAsync(income.Id, "Bônus", 30m, new DateOnly(2026, 2, 1), _person.Id, "Trabalho");
        await _service.UpdateExpenseAsync(expense.Id, "Conta", 7m, new DateOnly(2026, 2, 2), _person.Id, "Casa");
        Assert.Equal(23m, (await _service.GetMonthlyBalanceAsync(2026, 2)).Balance);

        await _service.DeleteIncomeAsync(income.Id);
        await _service.DeleteExpenseAsync(expense.Id);
        Assert.Equal(0m, (await _service.GetMonthlyBalanceAsync(2026, 2)).Balance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
