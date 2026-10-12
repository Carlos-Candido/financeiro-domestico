using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Finance.Avalonia;

public partial class MonthView : UserControl
{
    public MonthView()
    {
        InitializeComponent();
    }

    private MonthViewModel Vm => (MonthViewModel)DataContext!;

    private void OnPreviousMonth(object? sender, RoutedEventArgs e) => Vm.PreviousMonthCommand.Execute(null);

    private void OnNextMonth(object? sender, RoutedEventArgs e) => Vm.NextMonthCommand.Execute(null);

    private async void OnAddIncome(object? sender, RoutedEventArgs e) => await Vm.AddEntryAsync(true);

    private async void OnAddExpense(object? sender, RoutedEventArgs e) => await Vm.AddEntryAsync(false);


    private void OnOpenHistory(object? sender, RoutedEventArgs e) => Vm.ShowHistory();

}
