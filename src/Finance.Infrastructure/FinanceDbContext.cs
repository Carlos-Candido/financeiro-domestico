using Finance.Core;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure;

public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options) : DbContext(options)
{
    public DbSet<Person> People => Set<Person>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Income> Incomes => Set<Income>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Recurrence> Recurrences => Set<Recurrence>();
    public DbSet<InstallmentPlan> InstallmentPlans => Set<InstallmentPlan>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Person>().Property(x => x.Name).HasMaxLength(120).IsRequired();
        modelBuilder.Entity<Category>()
            .Property(x => x.Kind).HasConversion<string>();
        modelBuilder.Entity<Category>()
            .HasIndex(x => new { x.Kind, x.NormalizedName }).IsUnique();
        modelBuilder.Entity<Category>().Property(x => x.Name).HasMaxLength(100).IsRequired();
        modelBuilder.Entity<Category>().Property(x => x.NormalizedName).HasMaxLength(100).IsRequired();
        modelBuilder.Entity<Income>().Property(x => x.Amount).HasPrecision(18, 2);
        modelBuilder.Entity<Expense>().Property(x => x.Amount).HasPrecision(18, 2);
        modelBuilder.Entity<Expense>().HasIndex(x => new { x.RecurrenceId, x.Date }).IsUnique();
        modelBuilder.Entity<Income>().HasIndex(x => new { x.RecurrenceId, x.Date }).IsUnique();
        modelBuilder.Entity<Recurrence>().Property(x => x.Kind).HasConversion<string>();
        modelBuilder.Entity<Recurrence>().Property(x => x.CategoryKind).HasConversion<string>();
        modelBuilder.Entity<Recurrence>().Property(x => x.FixedAmount).HasPrecision(18, 2);
        modelBuilder.Entity<InstallmentPlan>().Property(x => x.TotalAmount).HasPrecision(18, 2);
        modelBuilder.Entity<Expense>().HasOne(x => x.Recurrence).WithMany(x => x.Expenses).HasForeignKey(x => x.RecurrenceId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Income>().HasOne(x => x.Recurrence).WithMany(x => x.Incomes).HasForeignKey(x => x.RecurrenceId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Expense>().HasOne(x => x.InstallmentPlan).WithMany(x => x.Installments).HasForeignKey(x => x.InstallmentPlanId).OnDelete(DeleteBehavior.Cascade);
    }
}
