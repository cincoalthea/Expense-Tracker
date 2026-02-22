using ExpenseTracker.Models;
using ExpenseTracker.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace ExpenseTracker;

public partial class MainPage : ContentPage
{
    private DatabaseService? _databaseService;

    public MainPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnPageLoaded;

        try
        {
            _databaseService = Handler?.MauiContext?.Services.GetService<DatabaseService>();
            if (_databaseService is null)
            {
                await DisplayAlert("Database unavailable", "Database service is not available.", "OK");
                return;
            }

            await _databaseService.InitAsync();
            var subscriptions = await _databaseService.GetSubscriptionsAsync();
            var incomeSources = await _databaseService.GetIncomeSourcesAsync();

            if (subscriptions.Count == 0)
            {
                await _databaseService.SaveSubscriptionAsync(new Subscription
                {
                    Name = "Sample Subscription",
                    Amount = 9.99m,
                    DayOfMonth = 1,
                    IsActive = true
                });
            }

            if (incomeSources.Count == 0)
            {
                await _databaseService.SaveIncomeSourceAsync(new IncomeSource
                {
                    Name = "Sample Allowance",
                    Amount = 500.00m,
                    DayOfMonth = 1,
                    IsActive = true
                });
            }

            await RefreshDashboardAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Database error", ex.Message, "OK");
            MonthBalanceLabel.Text = "$0.00";
        }
    }

    private async Task RefreshDashboardAsync()
    {
        if (_databaseService is null)
        {
            return;
        }

        var subscriptions = await _databaseService.GetSubscriptionsAsync();
        var incomeSources = await _databaseService.GetIncomeSourcesAsync();

        var subscriptionRows = subscriptions
            .OrderBy(x => x.DayOfMonth)
            .Select(x => new AmountRow(x.Id, x.Name, (-Math.Abs(x.Amount)).ToString("C2", CultureInfo.CurrentCulture)))
            .ToList();
        if (subscriptionRows.Count == 0)
        {
            subscriptionRows.Add(new AmountRow(0, "No subscriptions yet", "$0.00"));
        }

        var incomeRows = incomeSources
            .OrderBy(x => x.DayOfMonth)
            .Select(x => new AmountRow(x.Id, x.Name, $"+{Math.Abs(x.Amount).ToString("C2", CultureInfo.CurrentCulture)}"))
            .ToList();
        if (incomeRows.Count == 0)
        {
            incomeRows.Add(new AmountRow(0, "No income sources yet", "$0.00"));
        }

        BindableLayout.SetItemsSource(SubscriptionListLayout, subscriptionRows);
        BindableLayout.SetItemsSource(IncomeSourceListLayout, incomeRows);
        UpdateAllowanceSplit(incomeSources);

        var nowUtc = DateTime.UtcNow;
        var manualExpenses = await _databaseService.GetManualExpensesAsync(nowUtc.Year, nowUtc.Month);
        var expenseRows = manualExpenses
            .Select(x => new AmountRow(
                x.Id,
                string.IsNullOrWhiteSpace(x.Category)
                    ? (string.IsNullOrWhiteSpace(x.Notes) ? "Expense" : x.Notes)
                    : $"{x.Category}: {x.Notes}",
                x.Amount.ToString("C2", CultureInfo.CurrentCulture),
                x.DateUtc.ToLocalTime().ToString("MMM dd, yyyy", CultureInfo.CurrentCulture)))
            .ToList();
        if (expenseRows.Count == 0)
        {
            expenseRows.Add(new AmountRow(0, "No expenses logged yet", "$0.00", string.Empty));
        }
        BindableLayout.SetItemsSource(SpendingListLayout, expenseRows);

        var budgetStatus = await _databaseService.GetBudgetStatusAsync(nowUtc.Year, nowUtc.Month);
        var alertRows = budgetStatus
            .Where(x => x.IsOverLimit || x.IsNearLimit)
            .Select(x => new AmountRow(
                x.BudgetLimitId,
                x.IsOverLimit ? $"{x.Category} is over budget" : $"{x.Category} is nearing limit",
                $"{x.SpentAmount.ToString("C2", CultureInfo.CurrentCulture)} / {x.MonthlyLimit.ToString("C2", CultureInfo.CurrentCulture)}",
                string.Empty,
                x.IsOverLimit ? "#B24A4A" : "#B27A2B"))
            .ToList();
        if (alertRows.Count == 0)
        {
            alertRows.Add(new AmountRow(0, "No budget alerts this month", "-", string.Empty, "#2A6D95"));
        }
        BindableLayout.SetItemsSource(BudgetAlertListLayout, alertRows);

        await _databaseService.RebuildMonthlyRecurringAsync(nowUtc.Year, nowUtc.Month);
        var monthlyNet = await _databaseService.GetMonthlyNetBalanceAsync(nowUtc.Year, nowUtc.Month);

        MonthBalanceLabel.Text = monthlyNet.ToString("C2", CultureInfo.CurrentCulture);
    }

    private async void OnAddSubscriptionClicked(object? sender, EventArgs e)
    {
        if (_databaseService is null)
        {
            return;
        }

        var name = await DisplayPromptAsync("Add Subscription", "Name", initialValue: "New Subscription");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var amountInput = await DisplayPromptAsync("Add Subscription", "Amount", initialValue: "9.99", keyboard: Keyboard.Numeric);
        if (!TryParseAmount(amountInput, out var amount))
        {
            await DisplayAlert("Invalid amount", "Please enter a valid number.", "OK");
            return;
        }

        var dayInput = await DisplayPromptAsync("Add Subscription", "Day of month (1-31)", initialValue: "1", keyboard: Keyboard.Numeric);
        if (!TryParseDayOfMonth(dayInput, out var dayOfMonth))
        {
            await DisplayAlert("Invalid day", "Please enter a number from 1 to 31.", "OK");
            return;
        }

        await _databaseService.SaveSubscriptionAsync(new Subscription
        {
            Name = name.Trim(),
            Amount = Math.Abs(amount),
            DayOfMonth = dayOfMonth,
            IsActive = true
        });

        await RefreshDashboardAsync();
    }

    private async void OnAddIncomeSourceClicked(object? sender, EventArgs e)
    {
        if (_databaseService is null)
        {
            return;
        }

        var name = await DisplayPromptAsync("Add Income Source", "Name", initialValue: "New Income");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var amountInput = await DisplayPromptAsync("Add Income Source", "Amount", initialValue: "500.00", keyboard: Keyboard.Numeric);
        if (!TryParseAmount(amountInput, out var amount))
        {
            await DisplayAlert("Invalid amount", "Please enter a valid number.", "OK");
            return;
        }

        var dayInput = await DisplayPromptAsync("Add Income Source", "Day of month (1-31)", initialValue: "1", keyboard: Keyboard.Numeric);
        if (!TryParseDayOfMonth(dayInput, out var dayOfMonth))
        {
            await DisplayAlert("Invalid day", "Please enter a number from 1 to 31.", "OK");
            return;
        }

        await _databaseService.SaveIncomeSourceAsync(new IncomeSource
        {
            Name = name.Trim(),
            Amount = Math.Abs(amount),
            DayOfMonth = dayOfMonth,
            IsActive = true
        });

        await RefreshDashboardAsync();
    }

    private async void OnEditSubscriptionClicked(object? sender, EventArgs e)
    {
        if (_databaseService is null || !TryGetRowId(sender, out var id))
        {
            return;
        }

        var item = (await _databaseService.GetSubscriptionsAsync()).FirstOrDefault(x => x.Id == id);
        if (item is null)
        {
            return;
        }

        var name = await DisplayPromptAsync("Edit Subscription", "Name", initialValue: item.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var amountInput = await DisplayPromptAsync("Edit Subscription", "Amount", initialValue: item.Amount.ToString("0.##", CultureInfo.InvariantCulture), keyboard: Keyboard.Numeric);
        if (!TryParseAmount(amountInput, out var amount))
        {
            await DisplayAlert("Invalid amount", "Please enter a valid number.", "OK");
            return;
        }

        var dayInput = await DisplayPromptAsync("Edit Subscription", "Day of month (1-31)", initialValue: item.DayOfMonth.ToString(CultureInfo.InvariantCulture), keyboard: Keyboard.Numeric);
        if (!TryParseDayOfMonth(dayInput, out var dayOfMonth))
        {
            await DisplayAlert("Invalid day", "Please enter a number from 1 to 31.", "OK");
            return;
        }

        item.Name = name.Trim();
        item.Amount = Math.Abs(amount);
        item.DayOfMonth = dayOfMonth;
        await _databaseService.SaveSubscriptionAsync(item);
        await RefreshDashboardAsync();
    }

    private async void OnDeleteSubscriptionClicked(object? sender, EventArgs e)
    {
        if (_databaseService is null || !TryGetRowId(sender, out var id))
        {
            return;
        }

        var item = (await _databaseService.GetSubscriptionsAsync()).FirstOrDefault(x => x.Id == id);
        if (item is null)
        {
            return;
        }

        var confirm = await DisplayAlert("Delete Subscription", $"Delete '{item.Name}'?", "Delete", "Cancel");
        if (!confirm)
        {
            return;
        }

        await _databaseService.DeleteSubscriptionAsync(item);
        await RefreshDashboardAsync();
    }

    private async void OnEditIncomeSourceClicked(object? sender, EventArgs e)
    {
        if (_databaseService is null || !TryGetRowId(sender, out var id))
        {
            return;
        }

        var item = (await _databaseService.GetIncomeSourcesAsync()).FirstOrDefault(x => x.Id == id);
        if (item is null)
        {
            return;
        }

        var name = await DisplayPromptAsync("Edit Income Source", "Name", initialValue: item.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var amountInput = await DisplayPromptAsync("Edit Income Source", "Amount", initialValue: item.Amount.ToString("0.##", CultureInfo.InvariantCulture), keyboard: Keyboard.Numeric);
        if (!TryParseAmount(amountInput, out var amount))
        {
            await DisplayAlert("Invalid amount", "Please enter a valid number.", "OK");
            return;
        }

        var dayInput = await DisplayPromptAsync("Edit Income Source", "Day of month (1-31)", initialValue: item.DayOfMonth.ToString(CultureInfo.InvariantCulture), keyboard: Keyboard.Numeric);
        if (!TryParseDayOfMonth(dayInput, out var dayOfMonth))
        {
            await DisplayAlert("Invalid day", "Please enter a number from 1 to 31.", "OK");
            return;
        }

        item.Name = name.Trim();
        item.Amount = Math.Abs(amount);
        item.DayOfMonth = dayOfMonth;
        await _databaseService.SaveIncomeSourceAsync(item);
        await RefreshDashboardAsync();
    }

    private async void OnDeleteIncomeSourceClicked(object? sender, EventArgs e)
    {
        if (_databaseService is null || !TryGetRowId(sender, out var id))
        {
            return;
        }

        var item = (await _databaseService.GetIncomeSourcesAsync()).FirstOrDefault(x => x.Id == id);
        if (item is null)
        {
            return;
        }

        var confirm = await DisplayAlert("Delete Income Source", $"Delete '{item.Name}'?", "Delete", "Cancel");
        if (!confirm)
        {
            return;
        }

        await _databaseService.DeleteIncomeSourceAsync(item);
        await RefreshDashboardAsync();
    }

    private async void OnAddExpenseClicked(object? sender, EventArgs e)
    {
        if (_databaseService is null)
        {
            return;
        }

        var note = await DisplayPromptAsync("Add Expense", "What did you buy?", initialValue: "Groceries");
        if (string.IsNullOrWhiteSpace(note))
        {
            return;
        }

        var amountInput = await DisplayPromptAsync("Add Expense", "Amount spent", initialValue: "20.00", keyboard: Keyboard.Numeric);
        if (!TryParseAmount(amountInput, out var amount) || amount <= 0m)
        {
            await DisplayAlert("Invalid amount", "Please enter a valid amount greater than zero.", "OK");
            return;
        }

        var category = await DisplayPromptAsync("Add Expense", "Category", initialValue: "Food");
        if (string.IsNullOrWhiteSpace(category))
        {
            category = "Uncategorized";
        }

        var dateInput = await DisplayPromptAsync(
            "Add Expense",
            "Date (YYYY-MM-DD)",
            initialValue: DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (!TryParseDate(dateInput, out var localDate))
        {
            await DisplayAlert("Invalid date", "Please enter date as YYYY-MM-DD.", "OK");
            return;
        }

        var utcDate = new DateTime(localDate.Year, localDate.Month, localDate.Day, 12, 0, 0, DateTimeKind.Utc);
        await _databaseService.SaveManualExpenseAsync(note.Trim(), amount, utcDate, category.Trim());
        await RefreshDashboardAsync();
    }

    private async void OnEditExpenseClicked(object? sender, EventArgs e)
    {
        if (_databaseService is null || !TryGetRowId(sender, out var id))
        {
            return;
        }

        var transaction = await _databaseService.GetManualExpenseByIdAsync(id);
        if (transaction is null)
        {
            return;
        }

        var note = await DisplayPromptAsync("Edit Expense", "What did you buy?", initialValue: transaction.Notes);
        if (string.IsNullOrWhiteSpace(note))
        {
            return;
        }

        var amountInput = await DisplayPromptAsync("Edit Expense", "Amount spent", initialValue: Math.Abs(transaction.Amount).ToString("0.##", CultureInfo.InvariantCulture), keyboard: Keyboard.Numeric);
        if (!TryParseAmount(amountInput, out var amount) || amount <= 0m)
        {
            await DisplayAlert("Invalid amount", "Please enter a valid amount greater than zero.", "OK");
            return;
        }

        var category = await DisplayPromptAsync(
            "Edit Expense",
            "Category",
            initialValue: string.IsNullOrWhiteSpace(transaction.Category) ? "Uncategorized" : transaction.Category);
        if (string.IsNullOrWhiteSpace(category))
        {
            category = "Uncategorized";
        }

        var dateInput = await DisplayPromptAsync(
            "Edit Expense",
            "Date (YYYY-MM-DD)",
            initialValue: transaction.DateUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (!TryParseDate(dateInput, out var localDate))
        {
            await DisplayAlert("Invalid date", "Please enter date as YYYY-MM-DD.", "OK");
            return;
        }

        var utcDate = new DateTime(localDate.Year, localDate.Month, localDate.Day, 12, 0, 0, DateTimeKind.Utc);
        await _databaseService.SaveManualExpenseAsync(note.Trim(), amount, utcDate, category.Trim(), transaction.Id);
        await RefreshDashboardAsync();
    }

    private async void OnDeleteExpenseClicked(object? sender, EventArgs e)
    {
        if (_databaseService is null || !TryGetRowId(sender, out var id))
        {
            return;
        }

        var transaction = await _databaseService.GetManualExpenseByIdAsync(id);
        if (transaction is null)
        {
            return;
        }

        var displayName = string.IsNullOrWhiteSpace(transaction.Notes) ? "this expense" : transaction.Notes;
        var confirm = await DisplayAlert("Delete Expense", $"Delete '{displayName}'?", "Delete", "Cancel");
        if (!confirm)
        {
            return;
        }

        await _databaseService.DeleteTransactionAsync(transaction);
        await RefreshDashboardAsync();
    }

    private void UpdateAllowanceSplit(IEnumerable<IncomeSource> incomeSources)
    {
        var allowanceTotal = incomeSources
            .Where(x => x.IsActive && x.Name.Contains("allowance", StringComparison.OrdinalIgnoreCase))
            .Sum(x => Math.Abs(x.Amount));

        var needsAmount = allowanceTotal * 0.50m;
        var savingsAmount = allowanceTotal * 0.30m;
        var funAmount = allowanceTotal * 0.20m;

        SplitPlannerSubtitle.Text =
            $"Auto split {allowanceTotal.ToString("C2", CultureInfo.CurrentCulture)} allowance into goals (50/30/20)";
        NeedsAmountLabel.Text = needsAmount.ToString("C2", CultureInfo.CurrentCulture);
        SavingsAmountLabel.Text = savingsAmount.ToString("C2", CultureInfo.CurrentCulture);
        FunAmountLabel.Text = funAmount.ToString("C2", CultureInfo.CurrentCulture);
    }

    private static bool TryParseAmount(string? amountInput, out decimal amount)
    {
        amount = 0m;
        if (string.IsNullOrWhiteSpace(amountInput))
        {
            return false;
        }

        return decimal.TryParse(amountInput, NumberStyles.Number, CultureInfo.CurrentCulture, out amount) ||
               decimal.TryParse(amountInput, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    private static bool TryParseDayOfMonth(string? dayInput, out int dayOfMonth)
    {
        dayOfMonth = 0;
        if (!int.TryParse(dayInput, out dayOfMonth))
        {
            return false;
        }

        return dayOfMonth is >= 1 and <= 31;
    }

    private static bool TryParseDate(string? input, out DateTime date)
    {
        date = DateTime.MinValue;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        return DateTime.TryParseExact(input.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
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

    private record AmountRow(int Id, string Name, string AmountText, string DateText = "", string AlertColor = "#145780");
}
