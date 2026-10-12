using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Finance.Avalonia;

public partial class FamilySalariesView : UserControl
{
    public FamilySalariesView()
    {
        InitializeComponent();
    }

    private MonthViewModel ViewModel => (MonthViewModel)DataContext!;

    private void OnPreviousMonth(object? sender, RoutedEventArgs e) => ViewModel.PreviousMonthCommand.Execute(null);

    private void OnNextMonth(object? sender, RoutedEventArgs e) => ViewModel.NextMonthCommand.Execute(null);

    private async void OnAddSalary(object? sender, RoutedEventArgs e) => await ViewModel.AddFamilySalaryAsync();
}
