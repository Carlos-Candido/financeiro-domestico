using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Finance.Avalonia;

public partial class RecurrencesView : UserControl
{
    public RecurrencesView()
    {
        InitializeComponent();
    }

    private MonthViewModel Vm => (MonthViewModel)DataContext!;

    private async void OnAddRecurrence(object? sender, RoutedEventArgs e) => await Vm.AddRecurrenceAsync();

    private async void OnGenerate(object? sender, RoutedEventArgs e) => await Vm.GenerateRecurrencesAsync();
}
