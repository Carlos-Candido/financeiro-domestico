using Finance.Core;
using Finance.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Finance.Desktop;

public partial class MainWindow : Window
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private DateOnly _selectedMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private List<EntryRow> _entries = [];
    private int? _historyRecurrenceFilterId;
    private readonly SemaphoreSlim _databaseGate = new(1, 1);

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        await _databaseGate.WaitAsync();
        try
        {
        try
        {
            await App.Finance.GenerateRecurrencesAsync(_selectedMonth.Year, _selectedMonth.Month);
            var balance = await App.Finance.GetMonthlyBalanceAsync(_selectedMonth.Year, _selectedMonth.Month);
            var monthText = Capitalize(_selectedMonth.ToDateTime(TimeOnly.MinValue).ToString("MMMM 'de' yyyy", PtBr));
            MonthHeading.Text = monthText;
            HistoryMonthHeading.Text = $"Lançamentos de {monthText}";
            BalanceText.Text = balance.Balance.ToString("C", PtBr);
            IncomeTotalText.Text = balance.IncomeTotal.ToString("C", PtBr);
            ExpenseTotalText.Text = balance.ExpenseTotal.ToString("C", PtBr);
            var pending = balance.PendingExpenseCount + balance.PendingIncomeCount;
            PendingText.Text = pending == 0
                ? "Nenhum lançamento pendente"
                : $"{pending} aguardando valor · {balance.PendingIncomeCount} receita(s) · {balance.PendingExpenseCount} despesa(s)";
            ForecastText.Text = $"Ainda previstos: receitas {balance.PredictedIncomeTotal.ToString("C", PtBr)} · despesas {balance.PredictedExpenseTotal.ToString("C", PtBr)}";

            var first = _selectedMonth;
            var last = first.AddMonths(1).AddDays(-1);
            var incomes = await App.Database.Incomes.Include(x => x.Person).Include(x => x.Category)
                .Where(x => x.Date >= first && x.Date <= last).ToListAsync();
            var expenses = await App.Database.Expenses.Include(x => x.Person).Include(x => x.Category).Include(x => x.InstallmentPlan)
                .Where(x => x.Date >= first && x.Date <= last).ToListAsync();
            var rows = incomes.Select(x => new EntryRow(
                x.Id, true, x.Date, x.Description, x.Description, x.Category?.Name ?? "", x.Person?.Name ?? "", x.Amount,
                GetStatus(x.Amount, x.IsConfirmed), x.RecurrenceId, null, null, null)).ToList();
            rows.AddRange(expenses.Select(x =>
            {
                var displayDescription = x.InstallmentNumber is { } number && x.InstallmentPlan is { } plan
                    ? $"{x.Description} ({number}/{plan.InstallmentCount})"
                    : x.Description;
                return new EntryRow(
                    x.Id, false, x.Date, x.Description, displayDescription, x.Category?.Name ?? "", x.Person?.Name ?? "", x.Amount,
                    GetStatus(x.Amount, x.IsConfirmed), x.RecurrenceId, x.InstallmentPlanId, x.InstallmentNumber, x.InstallmentPlan?.InstallmentCount);
            }));
            _entries = rows.OrderByDescending(x => x.Date).ThenBy(x => x.Description).ToList();
            RecentGrid.ItemsSource = _entries.Take(6).ToList();
            RecentEmpty.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RecentGrid.Visibility = _entries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            RefreshHistoryGrid();

            var recurrences = await App.Database.Recurrences.Include(x => x.Person).Include(x => x.Category)
                .OrderBy(x => x.DueDay).ThenBy(x => x.Description).ToListAsync();
            var recurrenceRows = recurrences
                .Where(x => !IsFamilySalaryRecurrence(x))
                .Select(x => new RecurrenceRow(
                    x.Id,
                    x.Description,
                    x.CategoryKind,
                    x.Kind,
                    x.FixedAmount,
                    x.DueDay,
                    x.PersonId,
                    x.Person?.Name ?? "",
                    x.Category?.Name ?? "",
                    x.StartDate,
                    x.EndDate,
                    GetRecurrenceStatus(x.StartDate, x.EndDate, x.DueDay, first, last)))
                .ToList();
            ConfigureRecurrenceGrid(recurrenceRows);

            var salaryOverview = await App.Finance.GetFamilySalaryOverviewAsync(_selectedMonth.Year, _selectedMonth.Month);
            FamilySalaryMonthHeading.Text = $"Salários de {monthText}";
            FamilySalaryExpectedText.Text = salaryOverview.ExpectedTotal.ToString("C", PtBr);
            FamilySalaryReceivedText.Text = salaryOverview.ReceivedTotal.ToString("C", PtBr);
            RenderFamilySalaryCards(FamilySalaryList, salaryOverview.Salaries);

            await RefreshInstallmentPlansAsync(first, last);
        }
        catch (Exception ex)
        {
            ShowError("Não foi possível carregar os dados", ex);
        }
    }
        finally
        {
            _databaseGate.Release();
        }
    }

    private async Task<T> ReadDatabaseAsync<T>(Func<Task<T>> operation)
    {
        await _databaseGate.WaitAsync();
        try { return await operation(); }
        finally { _databaseGate.Release(); }
    }

    private async Task ExecuteDatabaseAsync(Func<Task> operation)
    {
        await _databaseGate.WaitAsync();
        try { await operation(); }
        finally { _databaseGate.Release(); }
    }

    private static string Capitalize(string value) => value.Length == 0 ? value : char.ToUpper(value[0], PtBr) + value[1..];
    private static string GetStatus(decimal? amount, bool isConfirmed) => amount is null ? "Pendente" : isConfirmed ? "Confirmado" : "Previsto";

    private static bool IsFamilySalaryRecurrence(Recurrence recurrence)
        => recurrence.CategoryKind == CategoryKind.Income
            && recurrence.Description == FinanceService.FamilySalaryDescription
            && recurrence.Category?.NormalizedName == "SALÁRIOS DA FAMÍLIA";

    private static string GetRecurrenceStatus(DateOnly startDate, DateOnly? endDate, int dueDay, DateOnly monthStart, DateOnly monthEnd)
    {
        if (startDate > monthEnd) return "Começa depois";
        var dueDate = new DateOnly(monthStart.Year, monthStart.Month, Math.Min(dueDay, monthEnd.Day));
        if (dueDate < startDate) return "Sem ocorrência";
        if (endDate is { } end && end < dueDate) return "Encerrada";
        return "Vigente";
    }

    private void RefreshHistoryGrid()
    {
        var items = _historyRecurrenceFilterId is { } recurrenceId
            ? _entries.Where(x => x.RecurrenceId == recurrenceId).ToList()
            : _entries;
        HistoryFilterText.Text = _historyRecurrenceFilterId is null
            ? "Todos os lançamentos do mês"
            : "Lançamentos desta recorrência no mês selecionado";
        ClearHistoryFilterButton.Visibility = _historyRecurrenceFilterId is null ? Visibility.Collapsed : Visibility.Visible;
        HistoryEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryGrid.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ConfigureGrid(HistoryGrid, items);
        UpdateHistoryActions();
    }

    private static void ConfigureGrid(DataGrid grid, List<EntryRow> items)
    {
        grid.Columns.Clear();
        AddColumn(grid, "Data", nameof(EntryRow.DateText), 105);
        AddColumn(grid, "Tipo", nameof(EntryRow.TypeText), 95);
        AddColumn(grid, "Descrição", nameof(EntryRow.DisplayDescription), new DataGridLength(2, DataGridLengthUnitType.Star));
        AddColumn(grid, "Categoria", nameof(EntryRow.Category), new DataGridLength(1, DataGridLengthUnitType.Star));
        AddColumn(grid, "Pessoa", nameof(EntryRow.Person), 115);
        AddColumn(grid, "Valor", nameof(EntryRow.AmountText), 135);
        AddColumn(grid, "Situação", nameof(EntryRow.Status), 112);
        grid.ItemsSource = items;
        grid.IsReadOnly = true;
    }

    private void ConfigureRecurrenceGrid(List<RecurrenceRow> items)
    {
        RecurrenceGrid.Columns.Clear();
        AddColumn(RecurrenceGrid, "Tipo", nameof(RecurrenceRow.CategoryKindText), 95);
        AddColumn(RecurrenceGrid, "Descrição", nameof(RecurrenceRow.Description), new DataGridLength(2, DataGridLengthUnitType.Star));
        AddColumn(RecurrenceGrid, "Tipo de valor", nameof(RecurrenceRow.KindText), 105);
        AddColumn(RecurrenceGrid, "Valor", nameof(RecurrenceRow.ValueText), 120);
        AddColumn(RecurrenceGrid, "Dia", nameof(RecurrenceRow.DueDay), 65);
        AddColumn(RecurrenceGrid, "Pessoa", nameof(RecurrenceRow.Person), 110);
        AddColumn(RecurrenceGrid, "Categoria", nameof(RecurrenceRow.Category), 140);
        AddColumn(RecurrenceGrid, "Vigência", nameof(RecurrenceRow.Status), 120);
        RecurrenceGrid.ItemsSource = items;
        RecurrenceGrid.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RecurrenceEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateRecurrenceActions();
    }

    private static void AddColumn(DataGrid grid, string title, string path, DataGridLength width)
        => grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path), Width = width, IsReadOnly = true });
    private static void AddColumn(DataGrid grid, string title, string path, double width)
        => AddColumn(grid, title, path, new DataGridLength(width));

    private void ShowView(Grid active)
    {
        DashboardView.Visibility = active == DashboardView ? Visibility.Visible : Visibility.Collapsed;
        HistoryView.Visibility = active == HistoryView ? Visibility.Visible : Visibility.Collapsed;
        PlanningView.Visibility = active == PlanningView ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = active == SettingsView ? Visibility.Visible : Visibility.Collapsed;
        HomeNavButton.Style = (Style)FindResource(active == DashboardView ? "NavActiveButton" : "NavButton");
        EntriesNavButton.Style = (Style)FindResource(active == HistoryView ? "NavActiveButton" : "NavButton");
        PlanningNavButton.Style = (Style)FindResource(active == PlanningView ? "NavActiveButton" : "NavButton");
        SettingsNavButton.Style = (Style)FindResource(active == SettingsView ? "NavActiveButton" : "NavButton");
    }

    private async void Dashboard_Click(object sender, RoutedEventArgs e)
    {
        _historyRecurrenceFilterId = null;
        ShowView(DashboardView);
        await RefreshAsync();
    }

    private async void History_Click(object sender, RoutedEventArgs e)
    {
        _historyRecurrenceFilterId = null;
        ShowView(HistoryView);
        await RefreshAsync();
    }

    private async void Planning_Click(object sender, RoutedEventArgs e)
    {
        ShowView(PlanningView);
        await RefreshAsync();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => ShowView(SettingsView);

    private async void PreviousMonth_Click(object sender, RoutedEventArgs e)
    {
        _selectedMonth = _selectedMonth.AddMonths(-1);
        await RefreshAsync();
    }

    private async void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        _selectedMonth = _selectedMonth.AddMonths(1);
        await RefreshAsync();
    }

    private void HistoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateHistoryActions();

    private void UpdateHistoryActions()
    {
        if (HistoryGrid is null) return;
        var row = HistoryGrid.SelectedItem as EntryRow;
        EditSelectedButton.IsEnabled = row is not null;
        // Generated occurrences would be recreated by the next idempotent generation pass.
        DeleteSelectedButton.IsEnabled = row is not null && row.RecurrenceId is null;
        ConfirmSelectedButton.Content = row?.Amount is null ? "Informar valor" : "Confirmar previsto";
        ConfirmSelectedButton.IsEnabled = row is not null && row.Status != "Confirmado";
    }

    private void ClearHistoryFilter_Click(object sender, RoutedEventArgs e)
    {
        _historyRecurrenceFilterId = null;
        RefreshHistoryGrid();
    }

    private void RecurrenceGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateRecurrenceActions();

    private void UpdateRecurrenceActions()
    {
        if (RecurrenceGrid is null) return;
        var row = RecurrenceGrid.SelectedItem as RecurrenceRow;
        EditRecurrenceButton.IsEnabled = row is not null;
        EndRecurrenceButton.IsEnabled = row is not null && row.Status != "Encerrada";
        OpenRecurrenceEntriesButton.IsEnabled = row is not null && _entries.Any(x => x.RecurrenceId == row.Id);
    }

    private async void OpenRecurrenceEntries_Click(object sender, RoutedEventArgs e)
    {
        if (RecurrenceGrid.SelectedItem is not RecurrenceRow row) return;
        _historyRecurrenceFilterId = row.Id;
        ShowView(HistoryView);
        await RefreshAsync();
    }

    private void RenderFamilySalaryCards(Panel host, IReadOnlyList<FamilySalaryRow> salaries)
    {
        host.Children.Clear();
        if (salaries.Count == 0)
        {
            host.Children.Add(new Border
            {
                Background = Brushes.White,
                BorderBrush = (Brush)FindResource("Line"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(24),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = "Nenhum salário cadastrado", FontSize = 18, FontWeight = FontWeights.SemiBold },
                        new TextBlock { Text = "Cadastre o salário mensal de cada pessoa da família.", Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 5, 0, 0) },
                    },
                },
            });
            return;
        }

        foreach (var salary in salaries)
        {
            var detail = new StackPanel();
            detail.Children.Add(new TextBlock { Text = salary.PersonName, FontSize = 18, FontWeight = FontWeights.SemiBold });
            detail.Children.Add(new TextBlock
            {
                Text = $"Dia {salary.DueDay} · Padrão {salary.StandardAmount.ToString("C", PtBr)}",
                Foreground = (Brush)FindResource("Muted"),
                Margin = new Thickness(0, 4, 12, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            var status = salary.IsReceived ? "Recebido" : salary.DueDate is null ? "Fora da vigência" : "Previsto";
            var summary = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            summary.Children.Add(new TextBlock
            {
                Text = salary.MonthAmount?.ToString("C", PtBr) ?? "Sem salário previsto neste mês",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource(salary.IsReceived ? "Positive" : "Ink"),
                TextAlignment = TextAlignment.Right,
            });
            summary.Children.Add(new TextBlock
            {
                Text = status,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource(salary.IsReceived ? "Positive" : "Muted"),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 4, 0, 0),
            });

            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.Children.Add(detail);
            Grid.SetColumn(summary, 1);
            top.Children.Add(summary);
            var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var editMonth = new Button
            {
                Content = "Editar este mês", Style = (Style)FindResource("SecondaryButton"), MinWidth = 135,
                Margin = new Thickness(0, 0, 8, 0), IsEnabled = salary.IncomeId is not null,
            };
            editMonth.Click += async (_, _) => await EditFamilySalaryMonthAsync(salary);
            var markReceived = new Button
            {
                Content = "Marcar recebido", Style = (Style)FindResource("SecondaryButton"), MinWidth = 135,
                Margin = new Thickness(0, 0, 8, 0), IsEnabled = salary.IncomeId is not null && !salary.IsReceived && salary.MonthAmount is not null,
            };
            markReceived.Click += async (_, _) => await ConfirmFamilySalaryAsync(salary);
            var updatePattern = new Button { Content = "Atualizar padrão", Style = (Style)FindResource("QuietButton"), MinWidth = 135, Margin = new Thickness(0) };
            updatePattern.Click += async (_, _) => await UpdateFamilySalaryDefaultAsync(salary);
            actions.Children.Add(editMonth);
            actions.Children.Add(markReceived);
            actions.Children.Add(updatePattern);
            var body = new StackPanel();
            body.Children.Add(top);
            body.Children.Add(actions);
            host.Children.Add(new Border
            {
                Background = Brushes.White,
                BorderBrush = (Brush)FindResource("Line"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(17),
                Margin = new Thickness(0, 0, 0, 9),
                Child = body,
            });
        }
    }

    private async void AddFamilySalary_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (people, overview) = await ReadDatabaseAsync(async () =>
            {
                var availablePeople = await App.Database.People.OrderBy(x => x.Name).ToListAsync();
                var salaryOverview = await App.Finance.GetFamilySalaryOverviewAsync(_selectedMonth.Year, _selectedMonth.Month);
                return (availablePeople, salaryOverview);
            });
            var assigned = overview.Salaries.Select(x => x.PersonId).ToHashSet();
            var available = people.Where(x => !assigned.Contains(x.Id)).ToList();
            if (available.Count == 0)
            {
                MessageBox.Show(this, "Todas as pessoas cadastradas já têm um salário padrão. Use Atualizar padrão para alterar um deles.", "Salários da família", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dialog = new FamilySalaryDefaultDialog(available, null) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            await ExecuteDatabaseAsync(async () => await App.Finance.SaveFamilySalaryDefaultAsync(dialog.PersonId, dialog.Amount, dialog.DueDay, _selectedMonth));
            await RefreshAsync();
        }
        catch (Exception ex) { ShowError("Não foi possível cadastrar o salário", ex); }
    }

    private async Task UpdateFamilySalaryDefaultAsync(FamilySalaryRow salary)
    {
        var person = new Person { Id = salary.PersonId, Name = salary.PersonName };
        var dialog = new FamilySalaryDefaultDialog([person], salary) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            await ExecuteDatabaseAsync(async () => await App.Finance.SaveFamilySalaryDefaultAsync(salary.PersonId, dialog.Amount, dialog.DueDay, _selectedMonth));
            await RefreshAsync();
        }
        catch (Exception ex) { ShowError("Não foi possível atualizar o padrão salarial", ex); }
    }

    private async Task EditFamilySalaryMonthAsync(FamilySalaryRow salary)
    {
        if (salary.IncomeId is not { } incomeId)
        {
            MessageBox.Show(this, "Não há lançamento de salário para editar neste mês.", "Salários da família", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new FamilySalaryMonthDialog(salary, _selectedMonth.Year, _selectedMonth.Month) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            await ExecuteDatabaseAsync(() => App.Finance.UpdateIncomeAmountAsync(incomeId, dialog.Amount, dialog.IsReceived));
            await RefreshAsync();
        }
        catch (Exception ex) { ShowError("Não foi possível atualizar o salário deste mês", ex); }
    }

    private async Task ConfirmFamilySalaryAsync(FamilySalaryRow salary)
    {
        if (salary.IncomeId is not { } incomeId || salary.MonthAmount is null) return;
        try
        {
            await ExecuteDatabaseAsync(() => App.Finance.ConfirmIncomeAsync(incomeId));
            await RefreshAsync();
        }
        catch (Exception ex) { ShowError("Não foi possível confirmar o recebimento do salário", ex); }
    }

    private async void AddPerson_Click(object sender, RoutedEventArgs e)
    {
        var name = SimplePrompt.Show(this, "Informe o nome da pessoa:", "Adicionar pessoa");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            await ExecuteDatabaseAsync(async () => await App.Finance.AddPersonAsync(name));
            MessageBox.Show(this, "Pessoa cadastrada.", "Finance", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { ShowError("Não foi possível cadastrar a pessoa", ex); }
    }

    private async void AddIncome_Click(object sender, RoutedEventArgs e) => await OpenEntryAsync(true);
    private async void AddExpense_Click(object sender, RoutedEventArgs e) => await OpenEntryAsync(false);

    private async Task OpenEntryAsync(bool income, EntryRow? existing = null)
    {
        List<Person> people;
        List<string> categories;
        try
        {
            people = await ReadDatabaseAsync(() => App.Database.People.OrderBy(x => x.Name).ToListAsync());
            if (people.Count == 0)
            {
                var personName = SimplePrompt.Show(this, "Para começar, informe seu nome:", "Pessoa responsável");
                if (string.IsNullOrWhiteSpace(personName)) return;
                await ExecuteDatabaseAsync(() => App.Finance.AddPersonAsync(personName));
                people = await ReadDatabaseAsync(() => App.Database.People.OrderBy(x => x.Name).ToListAsync());
            }
            categories = await ReadDatabaseAsync(() => App.Database.Categories.Where(x => x.Kind == (income ? CategoryKind.Income : CategoryKind.Expense))
                .OrderBy(x => x.Name).Select(x => x.Name).ToListAsync());
        }
        catch (Exception ex) { ShowError("Não foi possível carregar os dados do lançamento", ex); return; }

        var dialog = new EntryDialog(income, people, categories, existing, _selectedMonth) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (existing is { Status: "Previsto" } && !dialog.IsPending
            && MessageBox.Show(this, "Salvar o valor confirmará este lançamento e o incluirá no saldo. Deseja continuar?", "Confirmar lançamento", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await ExecuteDatabaseAsync(async () =>
            {
                if (income)
                {
                    if (existing is null)
                    {
                        if (dialog.IsPending) await App.Finance.AddPendingIncomeAsync(dialog.Description, dialog.Date, dialog.PersonId, dialog.Category);
                        else await App.Finance.AddIncomeAsync(dialog.Description, dialog.Amount!.Value, dialog.Date, dialog.PersonId, dialog.Category);
                    }
                    else await App.Finance.UpdateIncomeAsync(existing.Id, dialog.Description, dialog.IsPending ? null : dialog.Amount, dialog.Date, dialog.PersonId, dialog.Category);
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
            });
            await RefreshAsync();
        }
        catch (Exception ex) { ShowError("Não foi possível salvar o lançamento", ex); }
    }

    private async void EditSelected_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not EntryRow row) return;
        await OpenEntryAsync(row.IsIncome, row);
    }

    private async void ConfirmSelected_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not EntryRow row) return;
        if (row.Amount is null)
        {
            if (MessageBox.Show(this, "Este lançamento ainda não tem valor. Deseja informar o valor agora?", "Valor pendente", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                await OpenEntryAsync(row.IsIncome, row);
            return;
        }
        if (row.Status != "Previsto") return;
        try
        {
            await ExecuteDatabaseAsync(async () =>
            {
                if (row.IsIncome) await App.Finance.ConfirmIncomeAsync(row.Id);
                else await App.Finance.ConfirmExpenseAsync(row.Id);
            });
            await RefreshAsync();
        }
        catch (Exception ex) { ShowError("Não foi possível confirmar o lançamento", ex); }
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not EntryRow row) return;
        if (row.RecurrenceId is not null)
        {
            MessageBox.Show(this, "Este lançamento pertence a uma recorrência e pode ser gerado novamente. Edite o valor deste mês ou encerre a recorrência para os próximos meses.", "Lançamento recorrente", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"Excluir ‘{row.Description}’? Esta ação não pode ser desfeita.", "Confirmar exclusão", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await ExecuteDatabaseAsync(async () =>
            {
                if (row.IsIncome) await App.Finance.DeleteIncomeAsync(row.Id);
                else await App.Finance.DeleteExpenseAsync(row.Id);
            });
            await RefreshAsync();
        }
        catch (Exception ex) { ShowError("Não foi possível excluir o lançamento", ex); }
    }

    private async void AddRecurrence_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (people, categories) = await ReadDatabaseAsync(async () =>
            {
                var availablePeople = await App.Database.People.OrderBy(x => x.Name).ToListAsync();
                var categoryNames = await App.Database.Categories.OrderBy(x => x.Name).Select(x => x.Name).Distinct().ToListAsync();
                return (availablePeople, categoryNames);
            });
            var dialog = new RecurrenceDialog(people, categories) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            await ExecuteDatabaseAsync(async () =>
            {
                if (dialog.CategoryKind == CategoryKind.Income)
                    await App.Finance.AddIncomeRecurrenceAsync(dialog.Description, dialog.Kind, dialog.Amount, dialog.DueDay, dialog.StartDate, dialog.EndDate, dialog.PersonId, dialog.Category);
                else
                    await App.Finance.AddRecurrenceAsync(dialog.Description, dialog.Kind, dialog.Amount, dialog.DueDay, dialog.StartDate, dialog.EndDate, dialog.PersonId, dialog.Category);
            });
            await RefreshAsync();
        }
        catch (Exception ex) { ShowError("Não foi possível cadastrar a recorrência", ex); }
    }

    private async void EditRecurrence_Click(object sender, RoutedEventArgs e)
    {
        if (RecurrenceGrid.SelectedItem is not RecurrenceRow row) return;
        try
        {
            var (people, categories) = await ReadDatabaseAsync(async () =>
            {
                var availablePeople = await App.Database.People.OrderBy(x => x.Name).ToListAsync();
                var categoryNames = await App.Database.Categories.Where(x => x.Kind == row.CategoryKind)
                    .OrderBy(x => x.Name).Select(x => x.Name).ToListAsync();
                return (availablePeople, categoryNames);
            });
            var dialog = new RecurrenceDialog(people, categories, row) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            await ExecuteDatabaseAsync(() => App.Finance.UpdateRecurrenceAsync(row.Id, dialog.Description, dialog.Kind, dialog.Amount, dialog.DueDay, dialog.EndDate, dialog.PersonId, dialog.Category));
            await RefreshAsync();
        }
        catch (Exception ex) { ShowError("Não foi possível atualizar a recorrência", ex); }
    }

    private async void EndRecurrence_Click(object sender, RoutedEventArgs e)
    {
        if (RecurrenceGrid.SelectedItem is not RecurrenceRow row) return;
        var initialEnd = _selectedMonth.AddMonths(1).AddDays(-1);
        if (row.EndDate is { } currentEnd && currentEnd < initialEnd) initialEnd = currentEnd;
        if (initialEnd < row.StartDate) initialEnd = row.StartDate;
        var dialog = new RecurrenceEndDialog(row.StartDate, initialEnd) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (MessageBox.Show(this, $"Encerrar ‘{row.Description}’ em {dialog.EndDate.ToString("dd/MM/yyyy", PtBr)}? Os lançamentos já gerados serão preservados.", "Encerrar recorrência", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await ExecuteDatabaseAsync(() => App.Finance.EndRecurrenceAsync(row.Id, dialog.EndDate));
            await RefreshAsync();
        }
        catch (Exception ex) { ShowError("Não foi possível encerrar a recorrência", ex); }
    }

    private async void AddInstallment_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (people, categories) = await ReadDatabaseAsync(async () =>
            {
                var availablePeople = await App.Database.People.OrderBy(x => x.Name).ToListAsync();
                var categoryNames = await App.Database.Categories.Where(x => x.Kind == CategoryKind.Expense)
                    .OrderBy(x => x.Name).Select(x => x.Name).ToListAsync();
                return (availablePeople, categoryNames);
            });
            var dialog = new InstallmentDialog(people, categories) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            await ExecuteDatabaseAsync(() => App.Finance.CreateInstallmentPlanAsync(dialog.Description, dialog.Amount, dialog.Count, dialog.FirstDate, dialog.PersonId, dialog.Category));
            ShowView(PlanningView);
            PlanningTabs.SelectedIndex = 2;
            await RefreshAsync();
        }
        catch (Exception ex) { ShowError("Não foi possível cadastrar o parcelamento", ex); }
    }

    private async Task RefreshInstallmentPlansAsync(DateOnly first, DateOnly last)
    {
        var plans = await App.Database.InstallmentPlans.Include(x => x.Person).Include(x => x.Category).Include(x => x.Installments)
            .OrderByDescending(x => x.FirstDueDate).ToListAsync();
        var rows = plans.Select(plan =>
        {
            var installmentRows = plan.Installments.OrderBy(x => x.InstallmentNumber).Select(expense => new InstallmentDetailRow(
                expense.InstallmentNumber ?? 0,
                plan.InstallmentCount,
                expense.Date,
                expense.Amount,
                expense.Date < first ? "Antes do mês" : expense.Date > last ? "Depois do mês" : "Neste mês"))
                .ToList();
            var future = plan.Installments.Where(x => x.Date > last).ToList();
            return new InstallmentPlanRow(
                plan.Id,
                plan.Description,
                plan.Person?.Name ?? "",
                plan.Category?.Name ?? "",
                plan.TotalAmount,
                plan.InstallmentCount,
                plan.FirstDueDate,
                future.Count,
                future.Sum(x => x.Amount ?? 0m),
                installmentRows);
        }).ToList();
        rows = rows.OrderByDescending(x => x.FutureCount > 0).ThenByDescending(x => x.FirstDueDate).ToList();

        InstallmentReferenceText.Text = $"Parcelas futuras após {Capitalize(_selectedMonth.ToDateTime(TimeOnly.MinValue).ToString("MMMM 'de' yyyy", PtBr))}";
        InstallmentPlanGrid.Columns.Clear();
        AddColumn(InstallmentPlanGrid, "Compra", nameof(InstallmentPlanRow.Description), new DataGridLength(2, DataGridLengthUnitType.Star));
        AddColumn(InstallmentPlanGrid, "Pessoa", nameof(InstallmentPlanRow.Person), 110);
        AddColumn(InstallmentPlanGrid, "Total", nameof(InstallmentPlanRow.TotalText), 125);
        AddColumn(InstallmentPlanGrid, "Parcelas", nameof(InstallmentPlanRow.CountText), 105);
        AddColumn(InstallmentPlanGrid, "Futuras", nameof(InstallmentPlanRow.FutureCount), 80);
        AddColumn(InstallmentPlanGrid, "Total futuro", nameof(InstallmentPlanRow.FutureTotalText), 125);
        AddColumn(InstallmentPlanGrid, "Cronograma", nameof(InstallmentPlanRow.StatusText), 145);
        InstallmentPlanGrid.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        InstallmentPlanEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        InstallmentPlanGrid.ItemsSource = rows;
        InstallmentPlanGrid.SelectedIndex = rows.Count == 0 ? -1 : 0;
        if (InstallmentPlanGrid.SelectedItem is InstallmentPlanRow selected) ShowInstallmentDetails(selected);
        else ShowInstallmentDetails(null);
    }

    private void InstallmentPlanGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InstallmentPlanGrid.SelectedItem is InstallmentPlanRow selected) ShowInstallmentDetails(selected);
        else ShowInstallmentDetails(null);
    }

    private void ShowInstallmentDetails(InstallmentPlanRow? plan)
    {
        InstallmentDetailGrid.Columns.Clear();
        AddColumn(InstallmentDetailGrid, "Parcela", nameof(InstallmentDetailRow.NumberText), 100);
        AddColumn(InstallmentDetailGrid, "Vencimento", nameof(InstallmentDetailRow.DateText), 135);
        AddColumn(InstallmentDetailGrid, "Valor", nameof(InstallmentDetailRow.AmountText), 145);
        AddColumn(InstallmentDetailGrid, "Período", nameof(InstallmentDetailRow.Period), 150);
        InstallmentDetailHeading.Visibility = plan is null ? Visibility.Collapsed : Visibility.Visible;
        InstallmentDetailGrid.Visibility = plan is null ? Visibility.Collapsed : Visibility.Visible;
        InstallmentDetailGrid.ItemsSource = plan?.Installments ?? [];
        InstallmentDetailHeading.Text = plan is null
            ? "Selecione uma compra para ver as parcelas"
            : $"{plan.Description} · {plan.Category} · Primeira parcela {plan.FirstDueText} · {plan.Installments.Count} registro(s) de {plan.InstallmentCount}";
    }

    private static void ShowError(string message, Exception ex)
        => MessageBox.Show($"{message}.\n\n{ex.Message}", "Finance — erro", MessageBoxButton.OK, MessageBoxImage.Error);
}

internal sealed record EntryRow(
    int Id,
    bool IsIncome,
    DateOnly Date,
    string Description,
    string DisplayDescription,
    string Category,
    string Person,
    decimal? Amount,
    string Status,
    int? RecurrenceId,
    int? InstallmentPlanId,
    int? InstallmentNumber,
    int? InstallmentPlanCount)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    public string DateText => Date.ToString("dd/MM/yyyy", PtBr);
    public string AmountText => Amount?.ToString("C", PtBr) ?? "A informar";
    public string TypeText => IsIncome ? "Receita" : "Despesa";
    public string FlowGlyph => IsIncome ? "↗" : "↘";
    public string DetailText => $"{DateText}  ·  {Category}  ·  {Person}";
}

internal sealed record RecurrenceRow(
    int Id,
    string Description,
    CategoryKind CategoryKind,
    RecurrenceKind Kind,
    decimal? Amount,
    int DueDay,
    int PersonId,
    string Person,
    string Category,
    DateOnly StartDate,
    DateOnly? EndDate,
    string Status)
{
    public string CategoryKindText => CategoryKind == CategoryKind.Income ? "Receita" : "Despesa";
    public string KindText => Kind == RecurrenceKind.Fixed ? "Fixa" : "Variável";
    public string ValueText => Amount?.ToString("C", CultureInfo.GetCultureInfo("pt-BR")) ?? "Variável";
}

internal sealed record InstallmentPlanRow(
    int Id,
    string Description,
    string Person,
    string Category,
    decimal TotalAmount,
    int InstallmentCount,
    DateOnly FirstDueDate,
    int FutureCount,
    decimal FutureTotal,
    IReadOnlyList<InstallmentDetailRow> Installments)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    public string TotalText => TotalAmount.ToString("C", PtBr);
    public string CountText => $"{InstallmentCount} parcelas";
    public string FirstDueText => FirstDueDate.ToString("dd/MM/yyyy", PtBr);
    public string StatusText => FutureCount > 0 ? "Com parcelas futuras" : "Sem futuras registradas";
    public string FutureTotalText => FutureTotal.ToString("C", PtBr);
}

internal sealed record InstallmentDetailRow(int Number, int PlanCount, DateOnly Date, decimal? Amount, string Period)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    public string NumberText => $"{Number}/{PlanCount}";
    public string DateText => Date.ToString("dd/MM/yyyy", PtBr);
    public string AmountText => Amount?.ToString("C", PtBr) ?? "A informar";
}
