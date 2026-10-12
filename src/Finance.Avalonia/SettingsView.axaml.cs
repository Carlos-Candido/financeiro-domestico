using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Finance.Avalonia;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private MonthViewModel ViewModel => (MonthViewModel)DataContext!;

    private async void OnAddPerson(object? sender, RoutedEventArgs e) => await ViewModel.AddPersonAsync();

    private async void OnAddInstallment(object? sender, RoutedEventArgs e) => await ViewModel.AddInstallmentAsync();
    private void OnShowFamilySalaries(object? sender, RoutedEventArgs e) => ViewModel.ShowFamilySalaries();
}
