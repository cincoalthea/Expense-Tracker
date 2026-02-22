using ExpenseTracker.Models;
using ExpenseTracker.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace ExpenseTracker;

public partial class ReportsPage : ContentPage
{
    private DatabaseService? _databaseService;
    private DateTime _viewMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    public ReportsPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnPageLoaded;
        _databaseService = Handler?.MauiContext?.Services.GetService<DatabaseService>();
        if (_databaseService is null)
        {
            await DisplayAlert("Database unavailable", "Database service is not available.", "OK");
            return;
        }

        await _databaseService.InitAsync();
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_databaseService is null)
        {
            return;
        }

        var year = _viewMonth.Year;
        var month = _viewMonth.Month;
        CurrentMonthLabel.Text = _viewMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

        var monthStartUtc = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextMonthStartUtc = monthStartUtc.AddMonths(1);
        var monthTransactions = (await _databaseService.GetTransactionsAsync())
            .Where(x => x.DateUtc >= monthStartUtc && x.DateUtc < nextMonthStartUtc)
            .ToList();

        var incomeTotal = monthTransactions.Where(x => x.Amount > 0).Sum(x => x.Amount);
        var expenseTotal = monthTransactions.Where(x => x.Amount < 0).Sum(x => Math.Abs(x.Amount));
        var net = incomeTotal - expenseTotal;

        IncomeSummaryLabel.Text = incomeTotal.ToString("C2", CultureInfo.CurrentCulture);
        ExpenseSummaryLabel.Text = expenseTotal.ToString("C2", CultureInfo.CurrentCulture);
        NetSummaryLabel.Text = $"Net: {net.ToString("C2", CultureInfo.CurrentCulture)}";
        NetSummaryLabel.TextColor = net >= 0m ? Color.FromArgb("#1D9169") : Color.FromArgb("#B24A4A");

        var categorySpending = await _databaseService.GetMonthlyCategorySpendingAsync(year, month);
        var maxCategory = categorySpending.Count == 0 ? 1m : categorySpending.Max(x => x.SpentAmount);
        var categoryRows = categorySpending.Count == 0
            ? new List<ChartRow> { new(0, "No manual spending this month", "-", string.Empty, 0d) }
            : categorySpending.Select(x => new ChartRow(
                1,
                x.Category,
                x.SpentAmount.ToString("C2", CultureInfo.CurrentCulture),
                string.Empty,
                (double)(x.SpentAmount / maxCategory))).ToList();
        BindableLayout.SetItemsSource(CategoryListLayout, categoryRows);

        var budgetStatus = await _databaseService.GetBudgetStatusAsync(year, month);
        var budgetRows = budgetStatus.Count == 0
            ? new List<ChartRow> { new(0, "No budget limits set", "-", "Add one below", 0d) }
            : budgetStatus.Select(x =>
            {
                var state = x.IsOverLimit ? "Over" : x.IsNearLimit ? "Near" : "Safe";
                var percent = $"{x.PercentUsed:0.#}%";
                return new ChartRow(
                    x.BudgetLimitId,
                    x.Category,
                    $"{x.SpentAmount.ToString("C2", CultureInfo.CurrentCulture)} / {x.MonthlyLimit.ToString("C2", CultureInfo.CurrentCulture)}",
                    $"{state} ({percent})",
                    0d);
            }).ToList();
        BindableLayout.SetItemsSource(BudgetListLayout, budgetRows);
    }

    private async void OnPreviousMonthClicked(object? sender, EventArgs e)
    {
        _viewMonth = _viewMonth.AddMonths(-1);
        await RefreshAsync();
    }

    private async void OnNextMonthClicked(object? sender, EventArgs e)
    {
        _viewMonth = _viewMonth.AddMonths(1);
        await RefreshAsync();
    }

    private async void OnAddBudgetClicked(object? sender, EventArgs e)
    {
        if (_databaseService is null)
        {
            return;
        }

        var category = await DisplayPromptAsync("Budget Limit", "Category", initialValue: "Food");
        if (string.IsNullOrWhiteSpace(category))
        {
            return;
        }

        var limitInput = await DisplayPromptAsync("Budget Limit", "Monthly limit amount", initialValue: "300", keyboard: Keyboard.Numeric);
        if (!TryParseAmount(limitInput, out var amount) || amount <= 0m)
        {
            await DisplayAlert("Invalid amount", "Please enter a limit greater than zero.", "OK");
            return;
        }

        await _databaseService.SaveBudgetLimitAsync(new BudgetLimit
        {
            Category = category.Trim(),
            MonthlyLimit = amount
        });

        await RefreshAsync();
    }

    private async void OnEditBudgetClicked(object? sender, EventArgs e)
    {
        if (_databaseService is null || !TryGetRowId(sender, out var id))
        {
            return;
        }

        var item = (await _databaseService.GetBudgetLimitsAsync()).FirstOrDefault(x => x.Id == id);
        if (item is null)
        {
            return;
        }

        var category = await DisplayPromptAsync("Edit Budget", "Category", initialValue: item.Category);
        if (string.IsNullOrWhiteSpace(category))
        {
            return;
        }

        var limitInput = await DisplayPromptAsync("Edit Budget", "Monthly limit amount", initialValue: item.MonthlyLimit.ToString("0.##", CultureInfo.InvariantCulture), keyboard: Keyboard.Numeric);
        if (!TryParseAmount(limitInput, out var amount) || amount <= 0m)
        {
            await DisplayAlert("Invalid amount", "Please enter a limit greater than zero.", "OK");
            return;
        }

        item.Category = category.Trim();
        item.MonthlyLimit = amount;
        await _databaseService.SaveBudgetLimitAsync(item);
        await RefreshAsync();
    }

    private async void OnDeleteBudgetClicked(object? sender, EventArgs e)
    {
        if (_databaseService is null || !TryGetRowId(sender, out var id))
        {
            return;
        }

        var item = (await _databaseService.GetBudgetLimitsAsync()).FirstOrDefault(x => x.Id == id);
        if (item is null)
        {
            return;
        }

        var confirm = await DisplayAlert("Delete Budget", $"Delete budget '{item.Category}'?", "Delete", "Cancel");
        if (!confirm)
        {
            return;
        }

        await _databaseService.DeleteBudgetLimitAsync(item);
        await RefreshAsync();
    }

    private async void OnExportJsonClicked(object? sender, EventArgs e)
    {
        if (_databaseService is null)
        {
            return;
        }

        var backupDir = Path.Combine(FileSystem.AppDataDirectory, "Backups");
        var path = await _databaseService.ExportBackupJsonAsync(backupDir);
        await DisplayAlert("Backup exported", path, "OK");
    }

    private async void OnExportCsvClicked(object? sender, EventArgs e)
    {
        if (_databaseService is null)
        {
            return;
        }

        var backupDir = Path.Combine(FileSystem.AppDataDirectory, "Backups");
        var path = await _databaseService.ExportTransactionsCsvAsync(backupDir);
        await DisplayAlert("CSV exported", path, "OK");
    }

    private async void OnImportJsonClicked(object? sender, EventArgs e)
    {
        if (_databaseService is null)
        {
            return;
        }

        var path = await DisplayPromptAsync(
            "Import Backup",
            "Enter full JSON file path",
            initialValue: Path.Combine(FileSystem.AppDataDirectory, "Backups"));
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!File.Exists(path))
        {
            await DisplayAlert("File not found", "Please enter a valid file path.", "OK");
            return;
        }

        var confirm = await DisplayAlert(
            "Replace data?",
            "Import will replace current data (subscriptions, income, transactions, goals, budgets). Continue?",
            "Import",
            "Cancel");
        if (!confirm)
        {
            return;
        }

        await _databaseService.RestoreFromJsonFileAsync(path);
        await DisplayAlert("Import completed", "Data restored from backup.", "OK");
        await RefreshAsync();
    }

    private static bool TryParseAmount(string? input, out decimal amount)
    {
        amount = 0m;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        return decimal.TryParse(input, NumberStyles.Number, CultureInfo.CurrentCulture, out amount) ||
               decimal.TryParse(input, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    private static bool TryGetRowId(object? sender, out int id)
    {
        id = 0;
        if (sender is not Button button || button.CommandParameter is null)
        {
            return false;
        }

        return int.TryParse(button.CommandParameter.ToString(), out id) && id > 0;
    }

    private record ChartRow(int Id, string Name, string AmountText, string DetailText, double Progress);
}
