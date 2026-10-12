using Finance.Core;
using Finance.Infrastructure;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Finance.Desktop;

internal sealed class EntryDialog : Window
{
    private readonly TextBox _description = new();
    private readonly TextBox _amount = new();
    private readonly ComboBox _category = new() { IsEditable = true };
    private readonly DatePicker _date = new();
    private readonly ComboBox _person = new() { DisplayMemberPath = "Name" };
    private readonly CheckBox _pending = new() { Content = "Deixar valor pendente para informar depois", FontSize = 16, Margin = new Thickness(0, 4, 0, 12) };
    public string Description => _description.Text.Trim();
    public decimal? Amount => _pending.IsChecked == true ? null : DialogUi.ParseAmount(_amount.Text);
    public bool IsPending => _pending.IsChecked == true;
    public string Category => _category.Text.Trim();
    public DateOnly Date => DateOnly.FromDateTime(_date.SelectedDate ?? DateTime.Today);
    public int PersonId => ((Person)_person.SelectedItem).Id;

    public EntryDialog(bool income, IReadOnlyList<Person> people, IReadOnlyList<string> categories, EntryRow? existing, DateOnly defaultDate)
    {
        _date.SelectedDate = defaultDate.ToDateTime(TimeOnly.MinValue);
        Title = income ? "Adicionar dinheiro" : "Registrar gasto";
        Build(income, people, categories);
        if (existing is not null)
        {
            _description.Text = existing.Description;
            _amount.Text = existing.Amount?.ToString("0.00", CultureInfo.InvariantCulture) ?? "";
            _category.Text = existing.Category;
            _date.SelectedDate = existing.Date.ToDateTime(TimeOnly.MinValue);
            _person.SelectedItem = people.FirstOrDefault(p => p.Name == existing.Person) ?? people.FirstOrDefault();
            _pending.IsChecked = existing.Amount is null;
        }
    }

    private void Build(bool income, IReadOnlyList<Person> people, IReadOnlyList<string> categories)
    {
        var body = DialogUi.FormRoot(income ? "Receita" : "Despesa");
        DialogUi.AddField(body, "Descrição", _description);
        DialogUi.AddField(body, "Valor (R$)", _amount);
        _amount.ToolTip = "Exemplo: 125,50";
        DialogUi.AddField(body, "Data", _date);
        _category.ItemsSource = categories;
        DialogUi.AddField(body, "Categoria (se nova, será cadastrada)", _category);
        DialogUi.AddField(body, "Pessoa responsável", _person);
        _person.ItemsSource = people;
        body.Children.Add(_pending);
        AddButtons(body);
        Content = body;
        SizeToContent = SizeToContent.Height;
        Width = 460;
        MinHeight = 470;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    private void AddButtons(Panel body) => body.Children.Add(DialogUi.Buttons(() =>
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Description) || string.IsNullOrWhiteSpace(Category) || _person.SelectedItem is null)
                throw new InvalidOperationException("Preencha descrição, categoria e pessoa responsável.");
            if (!IsPending) _ = DialogUi.ParseAmount(_amount.Text);
            if (_date.SelectedDate is null) throw new InvalidOperationException("Informe a data.");
            DialogResult = true;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Verifique os dados", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }));
}

internal sealed class RecurrenceDialog : Window
{
    private readonly TextBox _description = new();
    private readonly TextBox _amount = new();
    private readonly TextBox _day = new();
    private readonly ComboBox _kind = new() { ItemsSource = new[] { "Fixa", "Variável" }, SelectedIndex = 0 };
    private readonly ComboBox _categoryKind = new() { ItemsSource = new[] { "Despesa", "Receita" }, SelectedIndex = 0 };
    private readonly ComboBox _person = new() { DisplayMemberPath = "Name" };
    private readonly DatePicker _start = new() { SelectedDate = DateTime.Today };
    private readonly DatePicker _end = new();
    private readonly ComboBox _categoryChoices;

    public string Description => _description.Text.Trim();
    public decimal? Amount => _kind.SelectedIndex == 0
        ? decimal.Parse(_amount.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("pt-BR"))
        : null;
    public RecurrenceKind Kind => _kind.SelectedIndex == 0 ? RecurrenceKind.Fixed : RecurrenceKind.Variable;
    public CategoryKind CategoryKind => _categoryKind.SelectedIndex == 1 ? CategoryKind.Income : CategoryKind.Expense;
    public int DueDay => int.Parse(_day.Text, CultureInfo.InvariantCulture);
    public DateOnly StartDate => DateOnly.FromDateTime(_start.SelectedDate!.Value);
    public DateOnly? EndDate => _end.SelectedDate is { } date ? DateOnly.FromDateTime(date) : null;
    public int PersonId => ((Person)_person.SelectedItem).Id;
    public string Category => _categoryChoices.Text.Trim();

    public RecurrenceDialog(IReadOnlyList<Person> people, IReadOnlyList<string> categories, RecurrenceRow? existing = null)
    {
        _day.Text = existing?.DueDay.ToString(CultureInfo.InvariantCulture) ?? DateTime.Today.Day.ToString(CultureInfo.InvariantCulture);
        Title = existing is null ? "Nova recorrência" : "Editar padrão mensal";
        var body = DialogUi.FormRoot(Title);
        DialogUi.AddField(body, "Receita ou despesa", _categoryKind);
        DialogUi.AddField(body, "Valor mensal", _kind);
        DialogUi.AddField(body, "Descrição", _description);
        DialogUi.AddField(body, "Valor fixo (R$)", _amount);
        _kind.SelectionChanged += (_, _) => _amount.IsEnabled = _kind.SelectedIndex == 0;
        DialogUi.AddField(body, "Dia de vencimento (1–31)", _day);
        DialogUi.AddField(body, "Começa em", _start);
        DialogUi.AddField(body, "Termina em (opcional)", _end);
        _categoryChoices = new ComboBox { IsEditable = true, ItemsSource = categories, FontSize = 17, MinHeight = 43, Margin = new Thickness(0, 5, 0, 12) };
        DialogUi.AddField(body, "Categoria", _categoryChoices);
        DialogUi.AddField(body, "Pessoa responsável", _person);
        _person.ItemsSource = people;

        if (existing is null)
        {
            if (people.Count == 1) _person.SelectedItem = people[0];
        }
        else
        {
            _description.Text = existing.Description;
            _amount.Text = existing.Amount?.ToString("0.00", CultureInfo.GetCultureInfo("pt-BR")) ?? "";
            _kind.SelectedIndex = existing.Kind == RecurrenceKind.Fixed ? 0 : 1;
            _categoryKind.SelectedIndex = existing.CategoryKind == CategoryKind.Income ? 1 : 0;
            _categoryKind.IsEnabled = false;
            _day.Text = existing.DueDay.ToString(CultureInfo.InvariantCulture);
            _start.SelectedDate = existing.StartDate.ToDateTime(TimeOnly.MinValue);
            _start.IsEnabled = false;
            _end.SelectedDate = existing.EndDate?.ToDateTime(TimeOnly.MinValue);
            _person.SelectedItem = people.FirstOrDefault(person => person.Id == existing.PersonId);
            _categoryChoices.Text = existing.Category;
        }
        _amount.IsEnabled = _kind.SelectedIndex == 0;
        body.Children.Add(new TextBlock
        {
            Text = "Fixa gera um valor previsto mensal; variável fica pendente até informar o valor. Alterar o padrão não modifica lançamentos já gerados.",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });
        body.Children.Add(DialogUi.Buttons(() =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Description) || string.IsNullOrWhiteSpace(Category) || _person.SelectedItem is null)
                    throw new InvalidOperationException("Preencha descrição, categoria e pessoa responsável.");
                if (_start.SelectedDate is null) throw new InvalidOperationException("Informe a data de início.");
                if (DueDay is < 1 or > 31) throw new InvalidOperationException("O dia deve estar entre 1 e 31.");
                if (_kind.SelectedIndex == 0) _ = Amount;
                if (EndDate is { } endDate && endDate < StartDate)
                    throw new InvalidOperationException("A data final não pode anteceder o início.");
                DialogResult = true;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Verifique os dados", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }));
        Content = body;
        Width = 500;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }
}

internal sealed class RecurrenceEndDialog : Window
{
    private readonly DatePicker _end = new();
    public DateOnly EndDate => DateOnly.FromDateTime(_end.SelectedDate!.Value);

    public RecurrenceEndDialog(DateOnly startDate, DateOnly suggestedEndDate)
    {
        Title = "Encerrar recorrência";
        _end.SelectedDate = suggestedEndDate.ToDateTime(TimeOnly.MinValue);
        _end.DisplayDateStart = startDate.ToDateTime(TimeOnly.MinValue);
        var body = DialogUi.FormRoot("Encerrar recorrência");
        DialogUi.AddField(body, "Último dia da recorrência", _end);
        body.Children.Add(new TextBlock
        {
            Text = "Os lançamentos já gerados serão preservados. Não serão criados novos lançamentos após esta data.",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });
        body.Children.Add(DialogUi.Buttons(() =>
        {
            if (_end.SelectedDate is null)
            {
                MessageBox.Show(this, "Informe a data final.", "Verifique os dados", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (EndDate < startDate)
            {
                MessageBox.Show(this, "A data final não pode anteceder o início.", "Verifique os dados", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }));
        Content = body;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }
}

internal sealed class FamilySalaryDefaultDialog : Window
{
    private readonly ComboBox _person = new() { DisplayMemberPath = "Name" };
    private readonly TextBox _amount = new();
    private readonly TextBox _dueDay = new();

    public int PersonId => ((Person)_person.SelectedItem).Id;
    public decimal Amount => DialogUi.ParseAmount(_amount.Text);
    public int DueDay => int.Parse(_dueDay.Text, CultureInfo.InvariantCulture);

    public FamilySalaryDefaultDialog(IReadOnlyList<Person> people, FamilySalaryRow? existing)
    {
        Title = existing is null ? "Cadastrar salário" : "Atualizar salário padrão";
        var body = DialogUi.FormRoot(Title);
        _person.ItemsSource = people;
        if (existing is not null)
        {
            _person.SelectedItem = people.FirstOrDefault(person => person.Id == existing.PersonId);
            _person.IsEnabled = false;
            _amount.Text = existing.StandardAmount.ToString("0.00", CultureInfo.GetCultureInfo("pt-BR"));
            _dueDay.Text = existing.DueDay.ToString(CultureInfo.InvariantCulture);
        }
        else if (people.Count == 1)
        {
            _person.SelectedItem = people[0];
        }

        DialogUi.AddField(body, "Pessoa", _person);
        DialogUi.AddField(body, "Salário mensal (R$)", _amount);
        DialogUi.AddField(body, "Dia habitual de recebimento (1–31)", _dueDay);
        body.Children.Add(new TextBlock
        {
            Text = "O padrão vale para próximos meses ainda não gerados. Meses já lançados permanecem iguais.",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });
        body.Children.Add(DialogUi.Buttons(() =>
        {
            try
            {
                if (_person.SelectedItem is null) throw new InvalidOperationException("Escolha a pessoa.");
                _ = Amount;
                if (DueDay is < 1 or > 31) throw new InvalidOperationException("O dia habitual deve estar entre 1 e 31.");
                DialogResult = true;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Verifique os dados", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }));
        Content = body;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }
}

internal sealed class FamilySalaryMonthDialog : Window
{
    private readonly TextBox _amount = new();
    private readonly CheckBox _received = new() { Content = "Já recebi este salário", FontSize = 16, Margin = new Thickness(0, 6, 0, 12) };

    public decimal Amount => DialogUi.ParseAmount(_amount.Text);
    public bool IsReceived => _received.IsChecked == true;

    public FamilySalaryMonthDialog(FamilySalaryRow salary, int year, int month)
    {
        Title = "Salário deste mês";
        var monthName = new DateOnly(year, month, 1).ToString("MMMM 'de' yyyy", CultureInfo.GetCultureInfo("pt-BR"));
        var body = DialogUi.FormRoot("Editar salário do mês");
        body.Children.Add(new TextBlock
        {
            Text = $"{salary.PersonName} · {char.ToUpper(monthName[0]) + monthName[1..]}",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 8),
        });
        _amount.Text = (salary.MonthAmount ?? salary.StandardAmount).ToString("0.00", CultureInfo.GetCultureInfo("pt-BR"));
        DialogUi.AddField(body, "Valor deste mês (R$)", _amount);
        _received.IsChecked = salary.IsReceived;
        body.Children.Add(_received);
        body.Children.Add(new TextBlock
        {
            Text = "A alteração vale somente para este mês. O padrão dos próximos meses não muda.",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });
        body.Children.Add(DialogUi.Buttons(() =>
        {
            try
            {
                _ = Amount;
                DialogResult = true;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Verifique os dados", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }));
        Content = body;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }
}

internal sealed class InstallmentDialog : Window
{
    private readonly TextBox _description = new(); private readonly TextBox _amount = new(); private readonly TextBox _count = new() { Text = "3" };
    private readonly ComboBox _person = new() { DisplayMemberPath = "Name" }; private readonly ComboBox _category;
    private readonly DatePicker _date = new() { SelectedDate = DateTime.Today };
    public string Description => _description.Text.Trim();
    public decimal Amount => decimal.Parse(_amount.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("pt-BR"));
    public int Count => int.Parse(_count.Text, CultureInfo.InvariantCulture);
    public int PersonId => ((Person)_person.SelectedItem).Id;
    public string Category => _category.Text.Trim();
    public DateOnly FirstDate => DateOnly.FromDateTime(_date.SelectedDate!.Value);
    public InstallmentDialog(IReadOnlyList<Person> people, IReadOnlyList<string> categories)
    {
        Title = "Compra parcelada"; var body = DialogUi.FormRoot("Compra parcelada");
        DialogUi.AddField(body, "Descrição", _description); DialogUi.AddField(body, "Valor total (R$)", _amount); DialogUi.AddField(body, "Quantidade de parcelas", _count); DialogUi.AddField(body, "Primeiro vencimento", _date);
        _category = new ComboBox { IsEditable = true, ItemsSource = categories, FontSize = 17, MinHeight = 43, Margin = new Thickness(0, 5, 0, 12) };
        DialogUi.AddField(body, "Categoria", _category); DialogUi.AddField(body, "Pessoa responsável", _person); _person.ItemsSource = people;
        body.Children.Add(new TextBlock { Text = "O sistema distribui os centavos entre as parcelas para preservar o total informado.", Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        body.Children.Add(DialogUi.Buttons(() =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Description) || string.IsNullOrWhiteSpace(Category) || _person.SelectedItem is null) throw new InvalidOperationException("Preencha descrição, categoria e pessoa responsável.");
                if (Amount <= 0 || Count < 1 || _date.SelectedDate is null) throw new InvalidOperationException("Informe valor, quantidade e data válidos.");
                DialogResult = true;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Verifique os dados", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }));
        Content = body; Width = 480; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }
}

internal static class DialogUi
{
    public static StackPanel FormRoot(string heading) => new() { Margin = new Thickness(28), Width = 400, Children = { new TextBlock { Text = heading, FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) } } };
    public static void AddField(Panel panel, string label, Control control)
    {
        panel.Children.Add(new TextBlock { Text = label, FontSize = 15, Foreground = Brushes.DimGray }); panel.Children.Add(control);
    }
    public static StackPanel Buttons(Action save)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var saveButton = new Button { Content = "Salvar", MinWidth = 110, Background = new SolidColorBrush(Color.FromRgb(40, 107, 98)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(40, 107, 98)) };
        saveButton.Click += (_, _) => save(); bar.Children.Add(saveButton);
        var cancel = new Button { Content = "Cancelar", MinWidth = 110, Margin = new Thickness(0) }; cancel.Click += (s, _) => Window.GetWindow((DependencyObject)s!).DialogResult = false; bar.Children.Add(cancel);
        return bar;
    }
    public static decimal ParseAmount(string value) => decimal.Parse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("pt-BR"));
}

internal static class SimplePrompt
{
    public static string? Show(Window owner, string prompt, string title)
    {
        var window = new Window { Title = title, Owner = owner, Width = 420, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = DialogUi.FormRoot(title); body.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        var input = new TextBox(); DialogUi.AddField(body, "Nome", input);
        body.Children.Add(DialogUi.Buttons(() => window.DialogResult = true)); window.Content = body;
        return window.ShowDialog() == true ? input.Text.Trim() : null;
    }
}
