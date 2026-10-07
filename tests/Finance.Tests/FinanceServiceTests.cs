using Finance.Core;
using Finance.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Finance.Tests;

public sealed class FinanceServiceTests : IDisposable
{
    private readonly FinanceDbContext _db;
    private readonly FinanceService _service;
    private readonly Person _person;

    public FinanceServiceTests()
    {
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FinanceDbContext(options);
        _service = new FinanceService(_db);
        _person = _service.AddPersonAsync("Ana").GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Categories_ignore_case_and_repeated_whitespace_across_entry_types()
    {
        await _service.AddExpenseAsync("Mercado", 10m, new DateOnly(2026, 1, 4), _person.Id, "  Casa   e mercado ");
        await _service.AddIncomeAsync("Reembolso", 12m, new DateOnly(2026, 1, 5), _person.Id, "cAsA e MERCADO");

        Assert.Equal(1, await _db.Categories.CountAsync());
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
        await _service.AddRecurrenceAsync("Salário", RecurrenceKind.Fixed, 2500m, 31, new DateOnly(2026, 1, 1), null, _person.Id, "Renda");
        await _service.AddRecurrenceAsync("Energia", RecurrenceKind.Variable, null, 10, new DateOnly(2026, 1, 1), null, _person.Id, "Contas");

        Assert.Equal(2, await _service.GenerateRecurrencesAsync(2026, 2));
        Assert.Equal(0, await _service.GenerateRecurrencesAsync(2026, 2));
        var fixedExpense = await _db.Expenses.SingleAsync(x => x.Description == "Salário");
        var variableExpense = await _db.Expenses.SingleAsync(x => x.Description == "Energia");
        Assert.Equal(new DateOnly(2026, 2, 28), fixedExpense.Date);
        Assert.Equal(2500m, fixedExpense.Amount);
        Assert.Null(variableExpense.Amount);
        fixedExpense.Date = new DateOnly(2026, 3, 1);
        await _db.SaveChangesAsync();
        Assert.Equal(0, await _service.GenerateRecurrencesAsync(2026, 2));
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
    public async Task Incomes_and_expenses_can_be_edited_and_deleted()
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

    [Fact]
    public async Task Money_must_be_positive_and_have_at_most_two_decimal_places()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.AddExpenseAsync("Zero", 0m, new DateOnly(2026, 1, 1), _person.Id, "Casa"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.AddIncomeAsync("Frações", 1.234m, new DateOnly(2026, 1, 1), _person.Id, "Renda"));
    }

    public void Dispose() => _db.Dispose();
}
