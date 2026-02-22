using ExpenseTracker.Models;
using ExpenseTracker.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace ExpenseTracker;

public partial class SavingsGoalsPage : ContentPage
{
    private DatabaseService? _databaseService;

    public SavingsGoalsPage()
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
            SavingsSummaryLabel.Text = "Database unavailable";
            return;
        }

        await _databaseService.InitAsync();
        await RefreshGoalsAsync();
    }

    private async Task RefreshGoalsAsync()
    {
        if (_databaseService is null)
        {
            SavingsSummaryLabel.Text = "Database unavailable";
            return;
        }

        var goals = await _databaseService.GetSavingsGoalsAsync();
        var incomeSources = await _databaseService.GetIncomeSourcesAsync();
        var monthlyIncome = incomeSources
            .Where(x => x.IsActive)
            .Sum(x => Math.Abs(x.Amount));

        if (goals.Count == 0)
        {
            BindableLayout.SetItemsSource(SavingsGoalListLayout, new List<SavingsGoalRow>());
            SavingsSummaryLabel.Text = "No goals yet. Add one to start tracking.";
            SavingsLiteracyLabel.Text = monthlyIncome <= 0m
                ? "Tip: Add monthly income to unlock budget-aware savings suggestions."
                : $"Literacy tip: Keep savings around 10%-20% of income ({(monthlyIncome * 0.10m).ToString("C2", CultureInfo.CurrentCulture)} to {(monthlyIncome * 0.20m).ToString("C2", CultureInfo.CurrentCulture)} monthly).";
            return;
        }

        var rows = goals.Select(x => MapToRow(x, monthlyIncome)).ToList();
        BindableLayout.SetItemsSource(SavingsGoalListLayout, rows);

        var totalCurrent = goals.Sum(x => Math.Abs(x.CurrentAmount));
        var totalTarget = goals.Sum(x => Math.Max(0m, x.TargetAmount));
        var overallProgress = totalTarget <= 0m ? 0m : Math.Min(100m, totalCurrent / totalTarget * 100m);
        SavingsSummaryLabel.Text =
            $"{totalCurrent.ToString("C2", CultureInfo.CurrentCulture)} saved toward {totalTarget.ToString("C2", CultureInfo.CurrentCulture)} ({overallProgress:0.#}%)";
        SavingsLiteracyLabel.Text = monthlyIncome <= 0m
            ? "Tip: Add monthly income to unlock budget-aware savings suggestions."
            : $"Literacy tip: Keep savings around 10%-20% of income ({(monthlyIncome * 0.10m).ToString("C2", CultureInfo.CurrentCulture)} to {(monthlyIncome * 0.20m).ToString("C2", CultureInfo.CurrentCulture)} monthly).";
    }

    private async void OnAddGoalClicked(object? sender, EventArgs e)
    {
        if (_databaseService is null)
        {
            return;
        }

        var name = await DisplayPromptAsync("Savings Goal", "Goal name", initialValue: "Emergency Fund");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var targetInput = await DisplayPromptAsync("Savings Goal", "Goal amount to save", initialValue: "1000", keyboard: Keyboard.Numeric);
        if (!TryParseNonNegativeAmount(targetInput, out var targetAmount) || targetAmount <= 0m)
        {
            await DisplayAlert("Invalid amount", "Please enter a goal amount greater than zero.", "OK");
            return;
        }

        var defaultTargetDate = DateTime.Today.AddMonths(12).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var targetDateInput = await DisplayPromptAsync("Savings Goal", "Target date (YYYY-MM-DD)", initialValue: defaultTargetDate);
        if (!TryParseTargetDate(targetDateInput, out var targetDate))
        {
            await DisplayAlert("Invalid date", "Please enter target date as YYYY-MM-DD.", "OK");
            return;
        }

        var currentInput = await DisplayPromptAsync("Savings Goal", "How much have you already saved?", initialValue: "0", keyboard: Keyboard.Numeric);
        if (!TryParseNonNegativeAmount(currentInput, out var currentAmount))
        {
            await DisplayAlert("Invalid amount", "Please enter a valid saved amount.", "OK");
            return;
        }

        await _databaseService.SaveSavingsGoalAsync(new SavingsGoal
        {
            Name = name.Trim(),
            TargetAmount = targetAmount,
            CurrentAmount = currentAmount,
            TargetMonths = CalculateTargetMonthsFromDate(targetDate),
            TargetDateUtc = targetDate.ToUniversalTime()
        });

        await RefreshGoalsAsync();
    }

    private async void OnAddSavedClicked(object? sender, EventArgs e)
    {
        if (_databaseService is null || !TryGetRowId(sender, out var id))
        {
            return;
        }

        var goal = (await _databaseService.GetSavingsGoalsAsync()).FirstOrDefault(x => x.Id == id);
        if (goal is null)
        {
            return;
        }

        var addInput = await DisplayPromptAsync("Add Saved Amount", $"How much did you save for '{goal.Name}'?", initialValue: "50", keyboard: Keyboard.Numeric);
        if (!TryParseNonNegativeAmount(addInput, out var addAmount))
        {
            await DisplayAlert("Invalid amount", "Please enter a valid amount.", "OK");
            return;
        }

        goal.CurrentAmount += addAmount;
        await _databaseService.SaveSavingsGoalAsync(goal);
        await RefreshGoalsAsync();
    }

    private async void OnEditGoalClicked(object? sender, EventArgs e)
    {
        if (_databaseService is null || !TryGetRowId(sender, out var id))
        {
            return;
        }

        var goal = (await _databaseService.GetSavingsGoalsAsync()).FirstOrDefault(x => x.Id == id);
        if (goal is null)
        {
            return;
        }

        var name = await DisplayPromptAsync("Edit Savings Goal", "Goal name", initialValue: goal.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var targetInput = await DisplayPromptAsync("Edit Savings Goal", "Goal amount to save", initialValue: goal.TargetAmount.ToString("0.##", CultureInfo.InvariantCulture), keyboard: Keyboard.Numeric);
        if (!TryParseNonNegativeAmount(targetInput, out var targetAmount) || targetAmount <= 0m)
        {
            await DisplayAlert("Invalid amount", "Please enter a goal amount greater than zero.", "OK");
            return;
        }

        var initialTargetDate = (goal.TargetDateUtc?.ToLocalTime().Date ?? DateTime.Today.AddMonths(Math.Max(1, goal.TargetMonths)))
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var targetDateInput = await DisplayPromptAsync("Edit Savings Goal", "Target date (YYYY-MM-DD)", initialValue: initialTargetDate);
        if (!TryParseTargetDate(targetDateInput, out var targetDate))
        {
            await DisplayAlert("Invalid date", "Please enter target date as YYYY-MM-DD.", "OK");
            return;
        }

        var currentInput = await DisplayPromptAsync("Edit Savings Goal", "Saved so far", initialValue: goal.CurrentAmount.ToString("0.##", CultureInfo.InvariantCulture), keyboard: Keyboard.Numeric);
        if (!TryParseNonNegativeAmount(currentInput, out var currentAmount))
        {
            await DisplayAlert("Invalid amount", "Please enter a valid saved amount.", "OK");
            return;
        }

        goal.Name = name.Trim();
        goal.TargetAmount = targetAmount;
        goal.CurrentAmount = currentAmount;
        goal.TargetMonths = CalculateTargetMonthsFromDate(targetDate);
        goal.TargetDateUtc = targetDate.ToUniversalTime();
        await _databaseService.SaveSavingsGoalAsync(goal);
        await RefreshGoalsAsync();
    }

    private async void OnDeleteGoalClicked(object? sender, EventArgs e)
    {
        if (_databaseService is null || !TryGetRowId(sender, out var id))
        {
            return;
        }

        var goal = (await _databaseService.GetSavingsGoalsAsync()).FirstOrDefault(x => x.Id == id);
        if (goal is null)
        {
            return;
        }

        var confirm = await DisplayAlert("Delete Goal", $"Delete '{goal.Name}'?", "Delete", "Cancel");
        if (!confirm)
        {
            return;
        }

        await _databaseService.DeleteSavingsGoalAsync(goal);
        await RefreshGoalsAsync();
    }

    private static SavingsGoalRow MapToRow(SavingsGoal goal, decimal monthlyIncome)
    {
        var target = Math.Max(0m, goal.TargetAmount);
        var current = Math.Max(0m, goal.CurrentAmount);
        var remaining = Math.Max(0m, target - current);
        var targetDateLocal = goal.TargetDateUtc?.ToLocalTime().Date;
        var referenceToday = DateTime.Today;
        var estimatedTargetDate = targetDateLocal ?? referenceToday.AddMonths(goal.TargetMonths <= 0 ? 12 : goal.TargetMonths);

        var daysRemaining = Math.Max(1, (estimatedTargetDate - referenceToday).Days);
        var weeksRemaining = Math.Max(1m / 7m, daysRemaining / 7m);
        var monthsRemaining = Math.Max(1m / 30m, daysRemaining / 30.4375m);

        var progress = target <= 0m ? 0d : (double)Math.Min(1m, current / target);
        var percent = progress * 100d;
        var monthlyNeeded = remaining / monthsRemaining;
        var weeklyNeeded = remaining / weeksRemaining;
        var dailyNeeded = remaining / daysRemaining;
        var incomePercent = monthlyIncome <= 0m ? 0m : monthlyNeeded / monthlyIncome * 100m;

        var tip = remaining <= 0m
            ? "Goal reached. Redirect this amount to your next goal."
            : monthlyIncome <= 0m
                ? "Add monthly income to compare this goal against your budget."
                : incomePercent <= 20m
                    ? $"On track: this needs about {incomePercent:0.#}% of monthly income."
                    : $"Stretch goal: this needs about {incomePercent:0.#}% of monthly income. Consider a longer timeline.";

        return new SavingsGoalRow(
            goal.Id,
            goal.Name,
            $"{current.ToString("C2", CultureInfo.CurrentCulture)} / {target.ToString("C2", CultureInfo.CurrentCulture)} by {estimatedTargetDate:MMM dd, yyyy}",
            $"{percent:0.#}%",
            progress,
            $"Save about {monthlyNeeded.ToString("C2", CultureInfo.CurrentCulture)} per month",
            $"Save about {weeklyNeeded.ToString("C2", CultureInfo.CurrentCulture)} per week",
            $"Save about {dailyNeeded.ToString("C2", CultureInfo.CurrentCulture)} per day",
            tip);
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

    private static bool TryParseNonNegativeAmount(string? input, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var parsed = decimal.TryParse(input, NumberStyles.Number, CultureInfo.CurrentCulture, out value) ||
                     decimal.TryParse(input, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        return parsed && value >= 0m;
    }

    private static int CalculateTargetMonthsFromDate(DateTime targetDate)
    {
        var days = Math.Max(1, (targetDate.Date - DateTime.Today).Days);
        return Math.Max(1, (int)Math.Ceiling(days / 30.4375d));
    }

    private static bool TryParseTargetDate(string? input, out DateTime targetDate)
    {
        targetDate = DateTime.MinValue;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (!DateTime.TryParseExact(input.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out targetDate))
        {
            return false;
        }

        return targetDate.Date >= DateTime.Today;
    }

    private record SavingsGoalRow(
        int Id,
        string Name,
        string AmountSummaryText,
        string PercentText,
        double Progress,
        string AdviceMonthlyText,
        string AdviceWeeklyText,
        string AdviceDailyText,
        string TipText);
}
