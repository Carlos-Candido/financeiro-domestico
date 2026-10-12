using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Finance.Avalonia;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MonthViewModel(this);
        ViewModel.AttachViews(
            (MonthView)DashboardPanel,
            (HistoryView)HistoryPanel,
            (RecurrencesView)RecurrencesPanel,
            (SettingsView)SettingsPanel,
            (FamilySalariesView)FamilySalariesPanel);
        Loaded += async (_, _) => await ViewModel.RefreshAsync();
    }

    private MonthViewModel ViewModel => (MonthViewModel)DataContext!;

    private void OnShowDashboard(object? sender, RoutedEventArgs e) => ViewModel.ShowDashboard();

    private void OnShowHistory(object? sender, RoutedEventArgs e) => ViewModel.ShowHistory();

    private void OnShowRecurrences(object? sender, RoutedEventArgs e) => ViewModel.ShowRecurrences();

    private void OnShowSettings(object? sender, RoutedEventArgs e) => ViewModel.ShowSettings();
    private void OnShowFamilySalaries(object? sender, RoutedEventArgs e) => ViewModel.ShowFamilySalaries();
}
