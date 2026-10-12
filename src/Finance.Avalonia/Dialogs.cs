using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Finance.Core;
using System.Globalization;

namespace Finance.Avalonia;

/// <summary>
/// Diálogos de formulário (lançamento, recorrência, parcelamento) e utilitários compartilhados.
/// Os diálogos apenas coletam e validam o formato dos dados; cálculos financeiros ficam no FinanceService.
/// </summary>
internal static class DialogUi
{
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static ScrollViewer FormRoot(string heading, out StackPanel body)
    {
        body = new StackPanel { Margin = new Thickness(26), MinWidth = 380, MaxWidth = 520, Spacing = 4 };
        body.Children.Add(new TextBlock { Text = heading, FontSize = 23, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
        return new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    public static void AddField(StackPanel panel, string label, Control control)
    {
        panel.Children.Add(new TextBlock { Text = label, FontSize = 15, Foreground = Brushes.Gray, Margin = new Thickness(0, 8, 0, 3) });
        control.MinHeight = 40;
        control.Margin = new Thickness(0, 0, 0, 6);
        panel.Children.Add(control);
    }

    public static StackPanel Buttons(Window window, Action validateAndAccept)
    {
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 16, 0, 0),
        };

        var save = new Button { Content = "Salvar", MinWidth = 118, MinHeight = 40, Classes = { "primary" } };
        save.Click += async (_, _) =>
        {
            try
            {
                validateAndAccept();
                window.Close(true);
            }
            catch (Exception ex)
            {
                await MessageBoxScreen.ShowAsync(window, ex.Message, "Verifique os dados", MessageBoxButtons.Ok, MessageBoxType.Warning);
            }
        };
        bar.Children.Add(save);

        var cancel = new Button { Content = "Cancelar", MinWidth = 118, MinHeight = 40 };
        cancel.Click += (_, _) => window.Close(false);
        bar.Children.Add(cancel);

        save.AttachedToVisualTree += (_, _) => save.Focus();
        return bar;
    }

    public static TextBox TextField(string? text = null, string? placeholder = null)
        => new() { Text = text, PlaceholderText = placeholder };

    public static decimal ParseAmount(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException("Informe um valor maior que zero. Exemplo: 125,50");
        if (!decimal.TryParse(trimmed, NumberStyles.Number, PtBr, out var amount) || amount <= 0)
            throw new InvalidOperationException($"Valor inválido: “{value}”. Use um número positivo no formato brasileiro, por exemplo 1250,90.");
        return amount;
    }

    public static int ParseInt(string value, string fieldName)
    {
        if (!int.TryParse(value.Trim(), NumberStyles.Integer, PtBr, out var parsed))
            throw new InvalidOperationException($"Informe um número inteiro válido em “{fieldName}”.");
        return parsed;
    }

    public static void RequireText(string value, params string[] fields)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Preencha: {string.Join(", ", fields)}.");
    }
}

/// <summary>Diálogo para lançamentos confirmados ou pendentes, também usado na edição.</summary>
internal sealed class EntryDialog : Window
{
    private readonly TextBox _description = new();
    private readonly TextBox _amount = new();
    private readonly ComboBox _category;
    private readonly ComboBox _person = new();
    private readonly CheckBox _pending = new()
    {
        Content = "Deixar valor pendente para informar depois",
        FontSize = 15,
        Margin = new Thickness(0, 6, 0, 2),
    };
    private readonly DatePicker _date = new() { SelectedDate = DateTimeOffset.Now.Date };

    public string Description => _description.Text?.Trim() ?? "";
    public decimal? Amount => _pending.IsChecked == true ? null : DialogUi.ParseAmount(_amount.Text ?? "");
    public bool IsPending => _pending.IsChecked == true;
    public string Category => _category.Text?.Trim() ?? _category.SelectedItem?.ToString()?.Trim() ?? "";
    public DateOnly Date => DateOnly.FromDateTime(_date.SelectedDate?.DateTime ?? DateTime.Today);
    public int PersonId => (_person.SelectedItem as Person)?.Id ?? 0;

    public EntryDialog(bool income, IReadOnlyList<Person> people, IReadOnlyList<string> categories, EntryRow? existing)
    {
        Title = existing is null ? (income ? "Adicionar dinheiro" : "Registrar gasto") : "Editar lançamento";
        var root = DialogUi.FormRoot(income ? "Receita" : "Despesa", out var body);

        _person.ItemsSource = people;
        _person.DisplayMemberBinding = new ReflectionBinding(nameof(Person.Name));

        _category = new ComboBox { IsEditable = true, ItemsSource = categories };

        DialogUi.AddField(body, "Descrição", _description);
        DialogUi.AddField(body, "Valor (R$)", _amount);
        _amount.PlaceholderText = "Exemplo: 125,50";
        DialogUi.AddField(body, "Data", _date);
        DialogUi.AddField(body, "Categoria (se nova, será cadastrada)", _category);
        DialogUi.AddField(body, "Pessoa responsável", _person);
        body.Children.Add(_pending);

        body.Children.Add(DialogUi.Buttons(this, () =>
        {
            if (_person.SelectedItem is null)
                throw new InvalidOperationException("Escolha a pessoa responsável. Cadastre uma pessoa pelo menu se necessário.");
            DialogUi.RequireText(Description, "descrição");
            if (string.IsNullOrWhiteSpace(Category))
                throw new InvalidOperationException("Informe a categoria.");
            if (_date.SelectedDate is null)
                throw new InvalidOperationException("Informe a data.");
            if (!IsPending) _ = Amount;
        }));

        if (existing is not null)
        {
            _description.Text = existing.Description;
            _amount.Text = existing.Amount?.ToString("0.##", DialogUi.PtBr) ?? "";
            _category.Text = existing.Category;
            _date.SelectedDate = existing.Date.ToDateTime(TimeOnly.MinValue);
            _person.SelectedItem = people.FirstOrDefault(p => p.Id == existing.PersonId) ?? people.FirstOrDefault();
            _pending.IsChecked = existing.Amount is null;
        }

        Content = root;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }
}

/// <summary>Diálogo de recorrência fixa ou variável, para receitas e despesas.</summary>
internal sealed class RecurrenceDialog : Window
{
    private readonly TextBox _description = new();
    private readonly TextBox _amount = new();
    private readonly TextBox _day = new() { Text = DateTime.Today.Day.ToString(CultureInfo.InvariantCulture) };
    private readonly ComboBox _kind = new() { ItemsSource = new[] { "Fixa", "Variável" }, SelectedIndex = 0 };
    private readonly ComboBox _categoryKind = new() { ItemsSource = new[] { "Despesa", "Receita" }, SelectedIndex = 0 };
    private readonly ComboBox _person = new();
    private readonly ComboBox _category;
    private readonly DatePicker _start = new() { SelectedDate = DateTimeOffset.Now.Date };
    private readonly DatePicker _end = new();

    public string Description => _description.Text?.Trim() ?? "";
    public decimal? Amount => _kind.SelectedIndex == 0 ? DialogUi.ParseAmount(_amount.Text ?? "") : null;
    public RecurrenceKind Kind => _kind.SelectedIndex == 0 ? RecurrenceKind.Fixed : RecurrenceKind.Variable;
    public CategoryKind CategoryKind => _categoryKind.SelectedIndex == 1 ? CategoryKind.Income : CategoryKind.Expense;
    public int DueDay => DialogUi.ParseInt(_day.Text ?? "", "dia de vencimento");
    public DateOnly StartDate => DateOnly.FromDateTime(_start.SelectedDate?.DateTime ?? DateTime.Today);
    public DateOnly? EndDate => _end.SelectedDate is { } date ? DateOnly.FromDateTime(date.DateTime) : null;
    public int PersonId => (_person.SelectedItem as Person)?.Id ?? 0;
    public string Category => _category.Text?.Trim() ?? _category.SelectedItem?.ToString()?.Trim() ?? "";

    public RecurrenceDialog(IReadOnlyList<Person> people, IReadOnlyList<string> categories)
    {
        Title = "Nova recorrência";
        var root = DialogUi.FormRoot("Nova recorrência", out var body);

        _person.ItemsSource = people;
        _person.DisplayMemberBinding = new ReflectionBinding(nameof(Person.Name));
        _category = new ComboBox { IsEditable = true, ItemsSource = categories };

        DialogUi.AddField(body, "Receita ou despesa", _categoryKind);
        DialogUi.AddField(body, "Frequência", _kind);
        DialogUi.AddField(body, "Descrição", _description);
        DialogUi.AddField(body, "Valor fixo (R$)", _amount);
        _amount.PlaceholderText = "Exemplo: 199,90";
        _kind.SelectionChanged += (_, _) => _amount.IsEnabled = _kind.SelectedIndex == 0;
        DialogUi.AddField(body, "Dia de vencimento (1–31)", _day);
        DialogUi.AddField(body, "Começa em", _start);
        DialogUi.AddField(body, "Termina em (opcional)", _end);
        DialogUi.AddField(body, "Categoria", _category);
        DialogUi.AddField(body, "Pessoa responsável", _person);

        body.Children.Add(new TextBlock
        {
            Text = "Recorrências fixas geram lançamentos previstos; variáveis ficam pendentes até alguém informar o valor.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
            FontSize = 14,
            Margin = new Thickness(0, 4, 0, 0),
        });

        body.Children.Add(DialogUi.Buttons(this, () =>
        {
            DialogUi.RequireText(Description, "descrição");
            if (string.IsNullOrWhiteSpace(Category))
                throw new InvalidOperationException("Informe a categoria.");
            if (_person.SelectedItem is null)
                throw new InvalidOperationException("Escolha a pessoa responsável.");
            if (_start.SelectedDate is null)
                throw new InvalidOperationException("Informe a data de início.");
            var day = DueDay;
            if (day is < 1 or > 31)
                throw new InvalidOperationException("O dia de vencimento deve estar entre 1 e 31.");
            if (_kind.SelectedIndex == 0) _ = Amount;
            if (_end.SelectedDate is { } end && _start.SelectedDate is { } start && end < start)
                throw new InvalidOperationException("A data final não pode ser anterior à data de início.");
        }));

        Content = root;
        Width = 500;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }
}

/// <summary>Diálogo de compra parcelada; a divisão dos centavos é feita pelo FinanceService.</summary>
internal sealed class InstallmentDialog : Window
{
    private readonly TextBox _description = new();
    private readonly TextBox _amount = new();
    private readonly TextBox _count = new() { Text = "3" };
    private readonly ComboBox _person = new();
    private readonly ComboBox _category;
    private readonly DatePicker _date = new() { SelectedDate = DateTimeOffset.Now.Date };

    public string Description => _description.Text?.Trim() ?? "";
    public decimal Amount => DialogUi.ParseAmount(_amount.Text ?? "");
    public int Count => DialogUi.ParseInt(_count.Text ?? "", "quantidade de parcelas");
    public int PersonId => (_person.SelectedItem as Person)?.Id ?? 0;
    public string Category => _category.Text?.Trim() ?? _category.SelectedItem?.ToString()?.Trim() ?? "";
    public DateOnly FirstDate => DateOnly.FromDateTime(_date.SelectedDate?.DateTime ?? DateTime.Today);

    public InstallmentDialog(IReadOnlyList<Person> people, IReadOnlyList<string> categories)
    {
        Title = "Compra parcelada";
        var root = DialogUi.FormRoot("Compra parcelada", out var body);

        _person.ItemsSource = people;
        _person.DisplayMemberBinding = new ReflectionBinding(nameof(Person.Name));
        _category = new ComboBox { IsEditable = true, ItemsSource = categories };

        DialogUi.AddField(body, "Descrição", _description);
        DialogUi.AddField(body, "Valor total (R$)", _amount);
        _amount.PlaceholderText = "Exemplo: 1200,00";
        DialogUi.AddField(body, "Quantidade de parcelas", _count);
        DialogUi.AddField(body, "Primeiro vencimento", _date);
        DialogUi.AddField(body, "Categoria", _category);
        DialogUi.AddField(body, "Pessoa responsável", _person);

        body.Children.Add(new TextBlock
        {
            Text = "O sistema distribui os centavos entre as parcelas para preservar o total informado.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
            FontSize = 14,
            Margin = new Thickness(0, 4, 0, 0),
        });

        body.Children.Add(DialogUi.Buttons(this, () =>
        {
            DialogUi.RequireText(Description, "descrição");
            if (string.IsNullOrWhiteSpace(Category))
                throw new InvalidOperationException("Informe a categoria.");
            if (_person.SelectedItem is null)
                throw new InvalidOperationException("Escolha a pessoa responsável.");
            if (_date.SelectedDate is null)
                throw new InvalidOperationException("Informe o primeiro vencimento.");
            if (Amount <= 0)
                throw new InvalidOperationException("O valor total deve ser maior que zero.");
            if (Count < 1)
                throw new InvalidOperationException("A quantidade de parcelas deve ser pelo menos 1.");
        }));

        Content = root;
        Width = 500;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }
}

/// <summary>Prompt de texto simples, usado para cadastrar pessoas.</summary>
internal static class SimplePrompt
{
    public static async Task<string?> Show(Window owner, string prompt, string title)
    {
        string? result = null;
        var window = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        var root = DialogUi.FormRoot(title, out var body);
        body.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, FontSize = 16, Margin = new Thickness(0, 0, 0, 8) });
        var input = DialogUi.TextField();
        DialogUi.AddField(body, "Nome", input);
        body.Children.Add(DialogUi.Buttons(window, () =>
        {
            if (string.IsNullOrWhiteSpace(input.Text))
                throw new InvalidOperationException("Informe o nome.");
            result = input.Text.Trim();
        }));
        window.Content = root;
        await window.ShowDialog<bool>(owner);
        return result;
    }
}
