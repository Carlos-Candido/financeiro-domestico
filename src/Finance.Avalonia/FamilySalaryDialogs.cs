using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Finance.Core;
using Finance.Infrastructure;
using System.Globalization;

namespace Finance.Avalonia;

internal sealed class FamilySalaryDefaultDialog : Window
{
    private readonly ComboBox _person = new();
    private readonly TextBox _amount = new();
    private readonly TextBox _dueDay = new();

    public int PersonId => (_person.SelectedItem as Person)?.Id ?? 0;
    public decimal Amount => DialogUi.ParseAmount(_amount.Text ?? "");
    public int DueDay => DialogUi.ParseInt(_dueDay.Text ?? "", "dia habitual de recebimento");

    public FamilySalaryDefaultDialog(IReadOnlyList<Person> people, FamilySalaryRow? existing)
    {
        Title = existing is null ? "Cadastrar salário" : "Atualizar salário padrão";
        var root = DialogUi.FormRoot(Title, out var body);
        _person.ItemsSource = people;
        _person.DisplayMemberBinding = new ReflectionBinding(nameof(Person.Name));
        if (existing is not null)
        {
            _person.SelectedItem = people.FirstOrDefault(person => person.Id == existing.PersonId);
            _person.IsEnabled = false;
            _amount.Text = existing.StandardAmount.ToString("0.00", DialogUi.PtBr);
            _dueDay.Text = existing.DueDay.ToString(CultureInfo.InvariantCulture);
        }
        else if (people.Count == 1)
        {
            _person.SelectedItem = people[0];
        }

        _amount.PlaceholderText = "Exemplo: 3500,00";
        _dueDay.PlaceholderText = "1 a 31";
        DialogUi.AddField(body, "Pessoa", _person);
        DialogUi.AddField(body, "Salário mensal (R$)", _amount);
        DialogUi.AddField(body, "Dia habitual de recebimento", _dueDay);
        body.Children.Add(new TextBlock
        {
            Text = "Este padrão vale para os próximos salários gerados. Meses já gerados não serão alterados.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#647176")),
            FontSize = 14,
            Margin = new Thickness(0, 6, 0, 0),
        });
        body.Children.Add(DialogUi.Buttons(this, () =>
        {
            if (PersonId == 0) throw new InvalidOperationException("Escolha a pessoa.");
            _ = Amount;
            if (DueDay is < 1 or > 31)
                throw new InvalidOperationException("O dia habitual deve estar entre 1 e 31.");
        }));

        Content = root;
        Width = 500;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }
}

internal sealed class FamilySalaryMonthDialog : Window
{
    private readonly TextBox _amount = new();
    private readonly CheckBox _received = new() { Content = "Já recebi este salário", FontSize = 15 };

    public decimal Amount => DialogUi.ParseAmount(_amount.Text ?? "");
    public bool IsReceived => _received.IsChecked == true;

    public FamilySalaryMonthDialog(FamilySalaryRow salary, int year, int month)
    {
        Title = "Salário deste mês";
        var monthName = new DateOnly(year, month, 1).ToDateTime(TimeOnly.MinValue)
            .ToString("MMMM 'de' yyyy", DialogUi.PtBr);
        var root = DialogUi.FormRoot("Editar salário do mês", out var body);
        body.Children.Add(new TextBlock
        {
            Text = $"{salary.PersonName} · {char.ToUpper(monthName[0]) + monthName[1..]}",
            FontSize = 15,
            Foreground = new SolidColorBrush(Color.Parse("#647176")),
            Margin = new Thickness(0, 0, 0, 6),
        });
        _amount.Text = (salary.MonthAmount ?? salary.StandardAmount).ToString("0.00", DialogUi.PtBr);
        _amount.PlaceholderText = "Exemplo: 3500,00";
        DialogUi.AddField(body, "Valor deste mês (R$)", _amount);
        _received.IsChecked = salary.IsReceived;
        body.Children.Add(_received);
        body.Children.Add(new TextBlock
        {
            Text = "A alteração vale somente para este mês. O padrão dos próximos meses não muda.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#647176")),
            FontSize = 14,
            Margin = new Thickness(0, 8, 0, 0),
        });
        body.Children.Add(DialogUi.Buttons(this, () => _ = Amount));

        Content = root;
        Width = 500;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }
}
