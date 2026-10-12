using Finance.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Windows;

namespace Finance.Desktop;

public partial class App : Application
{
    public static FinanceDbContext Database { get; private set; } = null!;
    public static FinanceService Finance { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"A operação não foi concluída. Os dados existentes foram preservados.\n\n{args.Exception.Message}", "Finance — erro", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        base.OnStartup(e);
        try
        {
            SqliteProviderConfiguration.Initialize();
            var databasePath = FinanceDataPath.GetDatabasePath();
            var options = new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlite($"Data Source={databasePath};")
                .Options;
            Database = new FinanceDbContext(options);
            Database.Database.EnsureCreated();
            Finance = new FinanceService(Database);
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível abrir os dados financeiros.\n\n{ex.Message}", "Finance — erro de inicialização", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Database?.Dispose();
        base.OnExit(e);
    }
}
