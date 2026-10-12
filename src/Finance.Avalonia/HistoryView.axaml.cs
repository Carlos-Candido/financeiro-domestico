using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Finance.Avalonia;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
    }

    private MonthViewModel Vm => (MonthViewModel)DataContext!;

    private void OnPreviousMonth(object? sender, RoutedEventArgs e) => Vm.PreviousMonthCommand.Execute(null);

    private void OnNextMonth(object? sender, RoutedEventArgs e) => Vm.NextMonthCommand.Execute(null);

    private async void OnConfirm(object? sender, RoutedEventArgs e) => await Vm.ConfirmSelectedAsync();

    private async void OnEdit(object? sender, RoutedEventArgs e) => await Vm.EditSelectedAsync();

    private async void OnDelete(object? sender, RoutedEventArgs e) => await Vm.DeleteSelectedAsync();
}
