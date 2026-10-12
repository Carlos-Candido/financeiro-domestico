using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Finance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Avalonia;

public partial class App : Application
{
    public static FinanceDbContext Database { get; private set; } = null!;
    public static FinanceService Finance { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
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
            }
            catch (Exception ex)
            {
                // A janela principal ainda não existe e ShutdownMode=OnMainWindowClose
                // encerraria o app sem mostrar nada. Usa modo explícito, mostra o erro
                // por alguns segundos e encerra com código 1.
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                base.OnFrameworkInitializationCompleted();
                MessageBoxScreen.ShowWarning(
                    $"Não foi possível abrir os dados financeiros.\n\n{ex.Message}", "Finance — erro de inicialização");
                return;
            }

            desktop.Exit += (_, _) => Database?.Dispose();
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Exibe um erro legível e mantém a aplicação utilizável.</summary>
    public static void ShowError(Window? owner, string message, Exception ex)
        => MessageBoxScreen.ShowError(owner, $"{message}.\n\n{ex.Message}", "Finance — erro");
}
