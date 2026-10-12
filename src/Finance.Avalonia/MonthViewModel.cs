using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Finance.Core;
using Finance.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Finance.Avalonia;

/// <summary>
/// Coordina painel, histórico e recorrências. Toda aritmética financeira fica no FinanceService;
/// esta classe apenas consulta dados para exibição e chama os serviços.
/// </summary>
internal sealed class MonthViewModel
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly Window _owner;
    private MonthView? _dashboard;
    private HistoryView? _history;
    private RecurrencesView? _recurrences;
    private SettingsView? _settings;
    private FamilySalariesView? _familySalaries;
    private DateOnly _selectedMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private List<EntryRow> _entries = [];
    private EntryRow? _selectedEntry;

    public MonthViewModel(Window owner)
    {
        _owner = owner;
        PreviousMonthCommand = new AsyncRelayCommand(() => ShiftMonthAsync(-1));
        NextMonthCommand = new AsyncRelayCommand(() => ShiftMonthAsync(1));
    }

    public AsyncRelayCommand PreviousMonthCommand { get; }
    public AsyncRelayCommand NextMonthCommand { get; }

    public void AttachViews(MonthView dashboard, HistoryView history, RecurrencesView recurrences, SettingsView settings, FamilySalariesView familySalaries)
    {
        _dashboard = dashboard;
        _history = history;
        _recurrences = recurrences;
        _settings = settings;
        _familySalaries = familySalaries;
    }

    // ===== Navegação e carregamento =====

    private Task ShiftMonthAsync(int months) => RunExclusiveAsync(async () =>
    {
        _selectedMonth = _selectedMonth.AddMonths(months);
        _selectedEntry = null;
        await RefreshCoreAsync();
    });

    public Task RefreshAsync() => RunExclusiveAsync(RefreshCoreAsync);

    private async Task RefreshCoreAsync()
    {
        try
        {
            await App.Finance.GenerateRecurrencesAsync(_selectedMonth.Year, _selectedMonth.Month);
            var balance = await App.Finance.GetMonthlyBalanceAsync(_selectedMonth.Year, _selectedMonth.Month);
            var monthName = _selectedMonth.ToDateTime(TimeOnly.MinValue).ToString("MMMM 'de' yyyy", PtBr);

            SetText(_dashboard?.MonthHeading, char.ToUpper(monthName[0]) + monthName[1..]);
            SetText(_dashboard?.IncomeTotalText, balance.IncomeTotal.ToString("C", PtBr));
            SetText(_dashboard?.ExpenseTotalText, balance.ExpenseTotal.ToString("C", PtBr));
            SetText(_dashboard?.BalanceText, balance.Balance.ToString("C", PtBr));
            SetText(_dashboard?.ForecastText,
                $"Previsto, ainda não confirmado: receitas {balance.PredictedIncomeTotal.ToString("C", PtBr)} · despesas {balance.PredictedExpenseTotal.ToString("C", PtBr)}");
            var pending = balance.PendingExpenseCount + balance.PendingIncomeCount;
            SetText(_dashboard?.PendingText, pending == 0
                ? "Nenhum lançamento pendente"
                : $"{pending} lançamento(s) aguardando valor ({balance.PendingIncomeCount} receita(s), {balance.PendingExpenseCount} despesa(s))");

            var first = _selectedMonth;
            var last = first.AddMonths(1).AddDays(-1);
            var incomes = await App.Database.Incomes.Include(x => x.Person).Include(x => x.Category)
                .Where(x => x.Date >= first && x.Date <= last).ToListAsync();
            var expenses = await App.Database.Expenses.Include(x => x.Person).Include(x => x.Category).Include(x => x.InstallmentPlan)
                .Where(x => x.Date >= first && x.Date <= last).ToListAsync();

            var rows = incomes.Select(x => new EntryRow(x.Id, true, x.PersonId, x.Date, x.Description, x.Description,
                x.Category?.Name ?? "", x.Person?.Name ?? "", x.Amount, StatusOf(x.IsConfirmed, x.Amount))).ToList();
            foreach (var x in expenses)
            {
                var display = x.Description;
                if (x.InstallmentNumber is { } number && x.InstallmentPlanId is { } planId)
                    display = $"{display} ({number}/{x.InstallmentPlan?.InstallmentCount ?? await CountInstallmentsAsync(planId)})";
                rows.Add(new EntryRow(x.Id, false, x.PersonId, x.Date, x.Description, display,
                    x.Category?.Name ?? "", x.Person?.Name ?? "", x.Amount, StatusOf(x.IsConfirmed, x.Amount)));
            }
            _entries = rows.OrderByDescending(x => x.Date).ThenBy(x => x.DisplayDescription).ToList();

            RenderEntryCards(_dashboard?.MonthList, _entries.Take(8).ToList());
            if (_history is { } history)
            {
                SetText(history.HistoryHeading, $"Lançamentos de {char.ToUpper(monthName[0]) + monthName[1..]}");
                RenderEntryCards(history.HistoryList, _entries);
            }

            var recurrenceRows = await App.Database.Recurrences.Include(x => x.Person).Include(x => x.Category)
                .OrderBy(x => x.DueDay).Select(x => new RecurrenceRow(x.Description,
                    x.CategoryKind == CategoryKind.Income ? "Receita" : "Despesa",
                    x.Kind == RecurrenceKind.Fixed ? "Fixa" : "Variável",
                    x.FixedAmount, x.DueDay, x.Person!.Name, x.Category!.Name, x.StartDate, x.EndDate)).ToListAsync();
            if (_recurrences is { } recurrences)
                RenderRecurrenceCards(recurrences.RecurrenceList, recurrenceRows);
            if (_familySalaries is { } familySalaries)
            {
                var salaryOverview = await App.Finance.GetFamilySalaryOverviewAsync(_selectedMonth.Year, _selectedMonth.Month);
                SetText(familySalaries.MonthHeading, char.ToUpper(monthName[0]) + monthName[1..]);
                SetText(familySalaries.ExpectedTotalText, salaryOverview.ExpectedTotal.ToString("C", PtBr));
                SetText(familySalaries.ReceivedTotalText, salaryOverview.ReceivedTotal.ToString("C", PtBr));
                RenderFamilySalaryCards(familySalaries.SalaryList, salaryOverview);
            }
        }
        catch (Exception ex)
        {
            App.ShowError(_owner, "Não foi possível carregar os dados", ex);
        }
    }

    private static string StatusOf(bool isConfirmed, decimal? amount)
        => amount is null ? "Pendente" : isConfirmed ? "Confirmado" : "Previsto";

    private static async Task<int> CountInstallmentsAsync(int planId)
        => await App.Database.Expenses.CountAsync(x => x.InstallmentPlanId == planId);

    // ===== Renderização de cartões =====

    private static void SetText(TextBlock? target, string? value)
    {
        if (target is not null) target.Text = value;
    }

    private void RenderEntryCards(StackPanel? host, IReadOnlyList<EntryRow> rows)
    {
        if (host is null) return;
        host.Children.Clear();
        if (rows.Count == 0)
        {
            host.Children.Add(BuildEmptyState(
                "Tudo tranquilo por aqui",
                "Adicione uma receita ou despesa para começar."));
            return;
        }
        foreach (var row in rows)
            host.Children.Add(BuildEntryCard(row, IsSelected(row)));
    }

    private static Border BuildEmptyState(string titleText, string descriptionText)
    {
        var mark = new Border
        {
            Width = 46,
            Height = 46,
            CornerRadius = new CornerRadius(23),
            Background = new SolidColorBrush(Color.Parse("#EAF1ED")),
            Child = new TextBlock
            {
                Text = "＋",
                FontSize = 24,
                Foreground = new SolidColorBrush(Color.Parse("#236B5C")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var body = new StackPanel
        {
            Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        body.Children.Add(mark);
        body.Children.Add(new TextBlock
        {
            Text = titleText,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        body.Children.Add(new TextBlock
        {
            Text = descriptionText,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.Parse("#647176")),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        return new Border
        {
            Classes = { "card" },
            Padding = new Thickness(28),
            MinHeight = 160,
            Child = body,
        };
    }

    private bool IsSelected(EntryRow row)
        => _selectedEntry is { } selected && selected.Id == row.Id && selected.IsIncome == row.IsIncome;

    private Border BuildEntryCard(EntryRow row, bool selected)
    {
        var statusBrush = row.Status switch
        {
            "Confirmado" => new SolidColorBrush(Color.Parse("#23734D")),
            "Previsto" => new SolidColorBrush(Color.Parse("#8A6417")),
            _ => new SolidColorBrush(Color.Parse("#A5423E")),
        };
        var amountBrush = row.IsIncome
            ? new SolidColorBrush(Color.Parse("#23734D"))
            : new SolidColorBrush(Color.Parse("#A5423E"));

        var icon = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Color.Parse("#F0F4F1")),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = row.IsIncome ? "↗" : "↘",
                FontSize = 19,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#236B5C")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var title = new TextBlock
        {
            Text = row.DisplayDescription,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        var detail = new TextBlock
        {
            Text = $"{row.DateText} · {row.Category} · {row.Person}",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse("#647176")),
            TextWrapping = TextWrapping.Wrap,
        };
        var left = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(title);
        left.Children.Add(detail);

        var amount = new TextBlock
        {
            Text = row.AmountText,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = row.Amount is null ? new SolidColorBrush(Color.Parse("#647176")) : amountBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var status = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#F4F6F4")),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(9, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = new TextBlock
            {
                Text = row.Status,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = statusBrush,
            },
        };
        var right = new StackPanel
        {
            Spacing = 5,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        right.Children.Add(amount);
        right.Children.Add(status);

        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("44,*,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(icon);
        Grid.SetColumn(left, 1);
        grid.Children.Add(left);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        var card = new Border { Child = grid };
        if (selected) card.Classes.Add("cardSelected"); else card.Classes.Add("card");

        var selectable = new Button
        {
            Content = card,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Tag = row,
        };
        selectable.Click += (_, _) =>
        {
            if (selectable.Tag is EntryRow clicked) SelectEntry(clicked);
        };
        return new Border { Child = selectable };
    }


    private static void RenderRecurrenceCards(StackPanel? host, IReadOnlyList<RecurrenceRow> rows)
    {
        if (host is null) return;
        host.Children.Clear();
        if (rows.Count == 0)
        {
            host.Children.Add(BuildEmptyState(
                "Nenhuma recorrência cadastrada",
                "Crie uma recorrência para acompanhar contas fixas ou valores variáveis."));
            return;
        }
        foreach (var row in rows)
        {
            var title = new TextBlock
            {
                Text = $"{row.CategoryKind} · {row.Description}",
                FontSize = 17,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            };
            var detail = new TextBlock
            {
                Text = $"{row.Kind} · {row.ValueText} · {row.DueDayText} · {row.Category} · {row.Person} · {row.PeriodText}",
                FontSize = 14,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
            };
            var body = new StackPanel { Spacing = 2 };
            body.Children.Add(title);
            body.Children.Add(detail);
            host.Children.Add(new Border { Classes = { "card" }, Child = body });
        }
    }

    private void RenderFamilySalaryCards(StackPanel? host, FamilySalaryOverview overview)
    {
        if (host is null) return;
        host.Children.Clear();
        if (overview.Salaries.Count == 0)
        {
            host.Children.Add(BuildEmptyState(
                "Nenhum salário cadastrado",
                "Cadastre o salário mensal de cada pessoa da família. O valor previsto não entra no saldo até ser recebido."));
            return;
        }

        foreach (var salary in overview.Salaries)
        {
            var statusText = salary.IsReceived
                ? "Recebido"
                : salary.DueDate is null ? "Fora do período" : "Previsto";
            var statusColor = salary.IsReceived ? "#23734D" : "#8A6417";
            var person = new TextBlock
            {
                Text = salary.PersonName,
                FontSize = 18,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            };
            var pattern = new TextBlock
            {
                Text = $"{salary.DueDay}º dia habitual · Padrão {salary.StandardAmount.ToString("C", PtBr)}",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.Parse("#647176")),
                TextWrapping = TextWrapping.Wrap,
            };
            var expected = new TextBlock
            {
                Text = salary.MonthAmount?.ToString("C", PtBr) ?? "Sem salário previsto neste mês",
                FontSize = 17,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse(salary.IsReceived ? "#23734D" : "#202D31")),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var status = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#F4F6F4")),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(9, 4),
                HorizontalAlignment = HorizontalAlignment.Right,
                Child = new TextBlock
                {
                    Text = statusText,
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.Parse(statusColor)),
                },
            };
            var summary = new StackPanel
            {
                Spacing = 5,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            summary.Children.Add(expected);
            summary.Children.Add(status);

            var editMonth = new Button
            {
                Content = "Editar este mês",
                MinHeight = 38,
                Padding = new Thickness(11, 6),
                IsEnabled = salary.IncomeId is not null,
            };
            editMonth.Click += async (_, _) => await EditFamilySalaryMonthAsync(salary);

            var markReceived = new Button
            {
                Content = "Marcar recebido",
                MinHeight = 38,
                Padding = new Thickness(11, 6),
                IsEnabled = salary.IncomeId is not null && !salary.IsReceived && salary.MonthAmount is not null,
            };
            markReceived.Click += async (_, _) => await ConfirmFamilySalaryAsync(salary);

            var updatePattern = new Button
            {
                Content = "Atualizar padrão",
                MinHeight = 38,
                Padding = new Thickness(11, 6),
            };
            updatePattern.Click += async (_, _) => await UpdateFamilySalaryDefaultAsync(salary);

            var actions = new WrapPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                ItemWidth = 145,
            };
            editMonth.Margin = new Thickness(0, 7, 8, 0);
            markReceived.Margin = new Thickness(0, 7, 8, 0);
            updatePattern.Margin = new Thickness(0, 7, 0, 0);
            actions.Children.Add(editMonth);
            actions.Children.Add(markReceived);
            actions.Children.Add(updatePattern);

            var details = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            details.Children.Add(person);
            details.Children.Add(pattern);
            var left = new StackPanel { Spacing = 2 };
            left.Children.Add(details);

            var right = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right };
            right.Children.Add(summary);
            right.Children.Add(actions);

            var body = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
                ColumnSpacing = 18,
                VerticalAlignment = VerticalAlignment.Center,
            };
            body.Children.Add(left);
            Grid.SetColumn(right, 1);
            body.Children.Add(right);

            host.Children.Add(new Border { Classes = { "card" }, Child = body });
        }
    }

    // ===== Ações de lançamentos =====

    public void SelectEntry(EntryRow row)
    {
        _selectedEntry = row;
        RenderEntryCards(_dashboard?.MonthList, _entries.Take(8).ToList());
        if (_history is { } history) RenderEntryCards(history.HistoryList, _entries);
    }

    public void ShowDashboard()
    {
        if (_dashboard is { } dashboard) dashboard.IsVisible = true;
        if (_history is { } history) history.IsVisible = false;
        if (_recurrences is { } recurrences) recurrences.IsVisible = false;
        if (_settings is { } settings) settings.IsVisible = false;
        if (_familySalaries is { } familySalaries) familySalaries.IsVisible = false;
    }

    public void ShowHistory()
    {
        if (_dashboard is { } dashboard) dashboard.IsVisible = false;
        if (_history is { } history) history.IsVisible = true;
        if (_recurrences is { } recurrences) recurrences.IsVisible = false;
        if (_settings is { } settings) settings.IsVisible = false;
        if (_familySalaries is { } familySalaries) familySalaries.IsVisible = false;
    }

    public void ShowRecurrences()
    {
        if (_dashboard is { } dashboard) dashboard.IsVisible = false;
        if (_history is { } history) history.IsVisible = false;
        if (_recurrences is { } recurrences) recurrences.IsVisible = true;
        if (_settings is { } settings) settings.IsVisible = false;
        if (_familySalaries is { } familySalaries) familySalaries.IsVisible = false;
    }

    public void ShowSettings()
    {
        if (_dashboard is { } dashboard) dashboard.IsVisible = false;
        if (_history is { } history) history.IsVisible = false;
        if (_recurrences is { } recurrences) recurrences.IsVisible = false;
        if (_settings is { } settings) settings.IsVisible = true;
        if (_familySalaries is { } familySalaries) familySalaries.IsVisible = false;
    }

    public void ShowFamilySalaries()
    {
        if (_dashboard is { } dashboard) dashboard.IsVisible = false;
        if (_history is { } history) history.IsVisible = false;
        if (_recurrences is { } recurrences) recurrences.IsVisible = false;
        if (_settings is { } settings) settings.IsVisible = false;
        if (_familySalaries is { } familySalaries) familySalaries.IsVisible = true;
    }

    public Task AddFamilySalaryAsync() => RunExclusiveAsync(AddFamilySalaryCoreAsync);

    private async Task AddFamilySalaryCoreAsync()
    {
        List<Person> availablePeople;
        try
        {
            var people = await App.Database.People.OrderBy(x => x.Name).ToListAsync();
            if (people.Count == 0)
            {
                MessageBoxScreen.ShowInfo(_owner, "Cadastre as pessoas da família em Configurações antes de adicionar salários.", "Salários da família");
                return;
            }
            var overview = await App.Finance.GetFamilySalaryOverviewAsync(_selectedMonth.Year, _selectedMonth.Month);
            var peopleWithSalary = overview.Salaries.Select(x => x.PersonId).ToHashSet();
            availablePeople = people.Where(x => !peopleWithSalary.Contains(x.Id)).ToList();
        }
        catch (Exception ex)
        {
            App.ShowError(_owner, "Não foi possível carregar os dados dos salários", ex);
            return;
        }

        if (availablePeople.Count == 0)
        {
            MessageBoxScreen.ShowInfo(_owner, "Todas as pessoas cadastradas já têm um salário padrão. Use Atualizar padrão para alterar um deles.", "Salários da família");
            return;
        }

        var dialog = new FamilySalaryDefaultDialog(availablePeople, null);
        if (await dialog.ShowDialog<bool>(_owner) != true) return;
        try
        {
            await App.Finance.SaveFamilySalaryDefaultAsync(
                dialog.PersonId,
                dialog.Amount,
                dialog.DueDay,
                _selectedMonth);
            await RefreshCoreAsync();
            MessageBoxScreen.ShowInfo(_owner, "Salário padrão cadastrado. Os valores previstos ainda não entram no saldo.", "Salários da família");
        }
        catch (Exception ex)
        {
            App.ShowError(_owner, "Não foi possível cadastrar o salário", ex);
        }
    }

    public Task UpdateFamilySalaryDefaultAsync(FamilySalaryRow salary)
        => RunExclusiveAsync(() => UpdateFamilySalaryDefaultCoreAsync(salary));

    private async Task UpdateFamilySalaryDefaultCoreAsync(FamilySalaryRow salary)
    {
        var person = new Person { Id = salary.PersonId, Name = salary.PersonName };
        var dialog = new FamilySalaryDefaultDialog([person], salary);
        if (await dialog.ShowDialog<bool>(_owner) != true) return;
        try
        {
            await App.Finance.SaveFamilySalaryDefaultAsync(
                salary.PersonId,
                dialog.Amount,
                dialog.DueDay,
                _selectedMonth);
            await RefreshCoreAsync();
            MessageBoxScreen.ShowInfo(_owner, "Salário padrão atualizado. Os meses já gerados foram preservados.", "Salários da família");
        }
        catch (Exception ex)
        {
            App.ShowError(_owner, "Não foi possível atualizar o padrão salarial", ex);
        }
    }

    public Task EditFamilySalaryMonthAsync(FamilySalaryRow salary)
        => RunExclusiveAsync(() => EditFamilySalaryMonthCoreAsync(salary));

    private async Task EditFamilySalaryMonthCoreAsync(FamilySalaryRow salary)
    {
        if (salary.IncomeId is not { } incomeId)
        {
            MessageBoxScreen.ShowInfo(_owner, "Não há lançamento de salário para editar neste mês.", "Salários da família");
            return;
        }
        var dialog = new FamilySalaryMonthDialog(salary, _selectedMonth.Year, _selectedMonth.Month);
        if (await dialog.ShowDialog<bool>(_owner) != true) return;
        try
        {
            await App.Finance.UpdateIncomeAmountAsync(incomeId, dialog.Amount, dialog.IsReceived);
            await RefreshCoreAsync();
        }
        catch (Exception ex)
        {
            App.ShowError(_owner, "Não foi possível atualizar o salário deste mês", ex);
        }
    }

    public Task ConfirmFamilySalaryAsync(FamilySalaryRow salary)
        => RunExclusiveAsync(() => ConfirmFamilySalaryCoreAsync(salary));

    private async Task ConfirmFamilySalaryCoreAsync(FamilySalaryRow salary)
    {
        if (salary.IncomeId is not { } incomeId || salary.MonthAmount is null)
        {
            MessageBoxScreen.ShowInfo(_owner, "Não há salário previsto para confirmar neste mês.", "Salários da família");
            return;
        }
        if (salary.IsReceived) return;
        try
        {
            await App.Finance.ConfirmIncomeAsync(incomeId);
            await RefreshCoreAsync();
        }
        catch (Exception ex)
        {
            App.ShowError(_owner, "Não foi possível confirmar o recebimento do salário", ex);
        }
    }

    public Task AddPersonAsync() => RunExclusiveAsync(AddPersonCoreAsync);

    private async Task AddPersonCoreAsync()
    {
        var name = await SimplePrompt.Show(_owner, "Informe o nome da pessoa:", "Adicionar pessoa");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            await App.Finance.AddPersonAsync(name);
            MessageBoxScreen.ShowInfo(_owner, "Pessoa cadastrada.", "Finance");
        }
        catch (Exception ex)
        {
            App.ShowError(_owner, "Não foi possível cadastrar a pessoa", ex);
        }
    }

    public Task AddEntryAsync(bool income) => RunExclusiveAsync(() => OpenEntryAsync(income));

    private async Task OpenEntryAsync(bool income, EntryRow? existing = null)
    {
        List<Person> people;
        List<string> categories;
        try
        {
            people = await App.Database.People.OrderBy(x => x.Name).ToListAsync();
            if (people.Count == 0)
            {
                var personName = await SimplePrompt.Show(_owner, "Para começar, informe seu nome:", "Pessoa responsável");
                if (string.IsNullOrWhiteSpace(personName)) return;
                await App.Finance.AddPersonAsync(personName);
                people = await App.Database.People.OrderBy(x => x.Name).ToListAsync();
            }
            categories = await App.Database.Categories
                .Where(x => x.Kind == (income ? CategoryKind.Income : CategoryKind.Expense))
                .OrderBy(x => x.Name).Select(x => x.Name).ToListAsync();
        }
        catch (Exception ex)
        {
            App.ShowError(_owner, "Não foi possível carregar os dados do lançamento", ex);
            return;
        }

        var dialog = new EntryDialog(income, people, categories, existing);
        var confirmed = await dialog.ShowDialog<bool>(_owner);
        if (!confirmed) return;
        try
        {
            if (income)
            {
                if (existing is null)
                {
                    if (dialog.IsPending) await App.Finance.AddPendingIncomeAsync(dialog.Description, dialog.Date, dialog.PersonId, dialog.Category);
                    else await App.Finance.AddIncomeAsync(dialog.Description, dialog.Amount!.Value, dialog.Date, dialog.PersonId, dialog.Category);
                }
                else await App.Finance.UpdateIncomeAsync(existing.Id, dialog.Description, dialog.Amount, dialog.Date, dialog.PersonId, dialog.Category);
            }
            else
            {
                if (existing is null)
                {
                    if (dialog.IsPending) await App.Finance.AddPendingExpenseAsync(dialog.Description, dialog.Date, dialog.PersonId, dialog.Category);
                    else await App.Finance.AddExpenseAsync(dialog.Description, dialog.Amount!.Value, dialog.Date, dialog.PersonId, dialog.Category);
                }
                else await App.Finance.UpdateExpenseAsync(existing.Id, dialog.Description, dialog.IsPending ? null : dialog.Amount, dialog.Date, dialog.PersonId, dialog.Category);
            }
            _selectedEntry = null;
            await RefreshCoreAsync();
            MessageBoxScreen.ShowInfo(_owner, "Lançamento salvo.", "Finance");
        }
        catch (Exception ex)
        {
            App.ShowError(_owner, "Não foi possível salvar o lançamento", ex);
        }
    }

    public Task ConfirmSelectedAsync() => RunExclusiveAsync(ConfirmSelectedCoreAsync);

    private async Task ConfirmSelectedCoreAsync()
    {
        if (_selectedEntry is not { } row)
        {
            MessageBoxScreen.ShowInfo(_owner, "Selecione um lançamento para confirmar.", "Finance");
            return;
        }
        if (row.Amount is null)
        {
            var wantsEdit = await MessageBoxScreen.ShowAsync(_owner,
                "Este lançamento não tem valor. Deseja editá-lo para informar o valor?",
                "Valor pendente", MessageBoxButtons.YesNo, MessageBoxType.Information);
            if (wantsEdit == MessageBoxResult.Yes) await OpenEntryAsync(row.IsIncome, row);
            return;
        }
        if (row.Status != "Previsto")
        {
            MessageBoxScreen.ShowInfo(_owner, "Somente lançamentos previstos podem ser confirmados.", "Finance");
            return;
        }
        try
        {
            if (row.IsIncome) await App.Finance.ConfirmIncomeAsync(row.Id);
            else await App.Finance.ConfirmExpenseAsync(row.Id);
            _selectedEntry = null;
            await RefreshCoreAsync();
        }
        catch (Exception ex)
        {
            App.ShowError(_owner, "Não foi possível confirmar o lançamento", ex);
        }
    }

    public Task EditSelectedAsync() => RunExclusiveAsync(EditSelectedCoreAsync);

    private async Task EditSelectedCoreAsync()
    {
        if (_selectedEntry is not { } row)
        {
            MessageBoxScreen.ShowInfo(_owner, "Selecione um lançamento para editar.", "Finance");
            return;
        }
        await OpenEntryAsync(row.IsIncome, row);
    }

    public Task DeleteSelectedAsync() => RunExclusiveAsync(DeleteSelectedCoreAsync);

    private async Task DeleteSelectedCoreAsync()
    {
        if (_selectedEntry is not { } row)
        {
            MessageBoxScreen.ShowInfo(_owner, "Selecione um lançamento para excluir.", "Finance");
            return;
        }
        var confirm = await MessageBoxScreen.ShowAsync(_owner,
            $"Excluir “{row.Description}”? Esta ação não pode ser desfeita.",
            "Confirmar exclusão", MessageBoxButtons.YesNo, MessageBoxType.Warning);
        if (confirm != MessageBoxResult.Yes) return;
        try
        {
            if (row.IsIncome) await App.Finance.DeleteIncomeAsync(row.Id);
            else await App.Finance.DeleteExpenseAsync(row.Id);
            _selectedEntry = null;
            await RefreshCoreAsync();
            MessageBoxScreen.ShowInfo(_owner, "Lançamento excluído.", "Finance");
        }
        catch (Exception ex)
        {
            App.ShowError(_owner, "Não foi possível excluir o lançamento", ex);
        }
    }

    // ===== Recorrências e parcelamentos =====

    public Task AddRecurrenceAsync() => RunExclusiveAsync(AddRecurrenceCoreAsync);

    private async Task AddRecurrenceCoreAsync()
    {
        List<Person> people;
        List<string> categories;
        try
        {
            people = await App.Database.People.OrderBy(x => x.Name).ToListAsync();
            categories = await App.Database.Categories.OrderBy(x => x.Name).Select(x => x.Name).Distinct().ToListAsync();
        }
        catch (Exception ex)
        {
            App.ShowError(_owner, "Não foi possível carregar os dados da recorrência", ex);
            return;
        }
        if (people.Count == 0)
        {
            MessageBoxScreen.ShowInfo(_owner, "Cadastre primeiro um lançamento para informar a pessoa responsável.", "Finance");
            return;
        }
        var dialog = new RecurrenceDialog(people, categories);
        var confirmed = await dialog.ShowDialog<bool>(_owner);
        if (!confirmed) return;
        try
        {
            if (dialog.CategoryKind == CategoryKind.Income)
                await App.Finance.AddIncomeRecurrenceAsync(dialog.Description, dialog.Kind, dialog.Amount, dialog.DueDay, dialog.StartDate, dialog.EndDate, dialog.PersonId, dialog.Category);
            else
                await App.Finance.AddRecurrenceAsync(dialog.Description, dialog.Kind, dialog.Amount, dialog.DueDay, dialog.StartDate, dialog.EndDate, dialog.PersonId, dialog.Category);
            await RefreshCoreAsync();
            MessageBoxScreen.ShowInfo(_owner, "Recorrência cadastrada.", "Finance");
        }
        catch (Exception ex)
        {
            App.ShowError(_owner, "Não foi possível cadastrar a recorrência", ex);
        }
    }

    public Task GenerateRecurrencesAsync() => RunExclusiveAsync(GenerateRecurrencesCoreAsync);

    private async Task GenerateRecurrencesCoreAsync()
    {
        try
        {
            var created = await App.Finance.GenerateRecurrencesAsync(_selectedMonth.Year, _selectedMonth.Month);
            await RefreshCoreAsync();
            MessageBoxScreen.ShowInfo(_owner,
                created == 0 ? "Nenhum lançamento novo para gerar neste mês." : $"{created} lançamento(s) gerado(s).",
                "Finance");
        }
        catch (Exception ex)
        {
            App.ShowError(_owner, "Não foi possível gerar os lançamentos", ex);
        }
    }

    public Task AddInstallmentAsync() => RunExclusiveAsync(AddInstallmentCoreAsync);

    private async Task AddInstallmentCoreAsync()
    {
        List<Person> people;
        List<string> categories;
        try
        {
            people = await App.Database.People.OrderBy(x => x.Name).ToListAsync();
            categories = await App.Database.Categories.Where(x => x.Kind == CategoryKind.Expense)
                .OrderBy(x => x.Name).Select(x => x.Name).ToListAsync();
        }
        catch (Exception ex)
        {
            App.ShowError(_owner, "Não foi possível carregar os dados do parcelamento", ex);
            return;
        }
        if (people.Count == 0)
        {
            MessageBoxScreen.ShowInfo(_owner, "Cadastre primeiro um lançamento para informar a pessoa responsável.", "Finance");
            return;
        }
        var dialog = new InstallmentDialog(people, categories);
        var confirmed = await dialog.ShowDialog<bool>(_owner);
        if (!confirmed) return;
        try
        {
            await App.Finance.CreateInstallmentPlanAsync(dialog.Description, dialog.Amount, dialog.Count, dialog.FirstDate, dialog.PersonId, dialog.Category);
            await RefreshCoreAsync();
            MessageBoxScreen.ShowInfo(_owner, "Parcelamento cadastrado.", "Finance");
        }
        catch (Exception ex)
        {
            App.ShowError(_owner, "Não foi possível cadastrar o parcelamento", ex);
        }
    }
    private async Task RunExclusiveAsync(Func<Task> action)
    {
        await _operationGate.WaitAsync();
        try
        {
            await action();
        }
        finally
        {
            _operationGate.Release();
        }
    }
}

/// <summary>Comando simples sem parâmetros que impede cliques duplicados durante a execução.</summary>
internal sealed class AsyncRelayCommand(Func<Task> execute)
{
    private bool _running;

    public async void Execute(object? parameter)
    {
        if (_running) return;
        _running = true;
        try
        {
            await execute();
        }
        finally
        {
            _running = false;
        }
    }
}
