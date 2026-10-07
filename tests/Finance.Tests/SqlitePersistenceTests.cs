using Finance.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Finance.Tests;

public sealed class SqlitePersistenceTests
{
    [Fact]
    public async Task Sqlite_stores_transactions_and_computes_monthly_balance()
    {
        SqliteProviderConfiguration.Initialize();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FinanceDbContext>().UseSqlite(connection).Options;
        await using var db = new FinanceDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var service = new FinanceService(db);
        var person = await service.AddPersonAsync("Ana");
        await service.AddIncomeAsync("Salário", 1000.25m, new DateOnly(2026, 5, 1), person.Id, "Renda");
        await service.AddExpenseAsync("Aluguel", 1100.10m, new DateOnly(2026, 5, 2), person.Id, "Moradia");

        var balance = await service.GetMonthlyBalanceAsync(2026, 5);

        Assert.Equal(1000.25m, balance.IncomeTotal);
        Assert.Equal(1100.10m, balance.ExpenseTotal);
        Assert.Equal(-99.85m, balance.Balance);
    }
}
