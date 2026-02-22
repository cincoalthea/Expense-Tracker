using ExpenseTracker.Models;
using SQLite;
using System.Text.Json;

namespace ExpenseTracker.Services;

public class DatabaseService
{
    private const string RecurringIncomeType = "RecurringIncome";
    private const string RecurringSubscriptionType = "RecurringSubscription";
    private const string ManualExpenseType = "ManualExpense";

    private readonly SQLiteAsyncConnection _db;
    private bool _initialized;

    public DatabaseService()
    {
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "expense_tracker.db3");
        _db = new SQLiteAsyncConnection(dbPath);
    }

    public async Task InitAsync()
    {
        if (_initialized)
        {
            return;
        }

        await _db.CreateTableAsync<Subscription>();
        await _db.CreateTableAsync<IncomeSource>();
        await _db.CreateTableAsync<Transaction>();
        await _db.CreateTableAsync<SavingsGoal>();
        await _db.CreateTableAsync<BudgetLimit>();
        await EnsureTransactionSchemaAsync();
        await EnsureSavingsGoalSchemaAsync();
        _initialized = true;
    }

    public async Task<List<Subscription>> GetSubscriptionsAsync()
    {
        await InitAsync();
        return await _db.Table<Subscription>().ToListAsync();
    }

    public async Task<int> SaveSubscriptionAsync(Subscription item)
    {
        await InitAsync();
        return item.Id == 0
            ? await _db.InsertAsync(item)
            : await _db.UpdateAsync(item);
    }

    public async Task<int> DeleteSubscriptionAsync(Subscription item)
    {
        await InitAsync();
        return await _db.DeleteAsync(item);
    }

    public async Task<List<IncomeSource>> GetIncomeSourcesAsync()
    {
        await InitAsync();
        return await _db.Table<IncomeSource>().ToListAsync();
    }

    public async Task<int> SaveIncomeSourceAsync(IncomeSource item)
    {
        await InitAsync();
        return item.Id == 0
            ? await _db.InsertAsync(item)
            : await _db.UpdateAsync(item);
    }

    public async Task<int> DeleteIncomeSourceAsync(IncomeSource item)
    {
        await InitAsync();
        return await _db.DeleteAsync(item);
    }

    public async Task<List<Transaction>> GetTransactionsAsync()
    {
        await InitAsync();
        return await _db.Table<Transaction>()
            .OrderByDescending(x => x.DateUtc)
            .ToListAsync();
    }

    public async Task<int> SaveTransactionAsync(Transaction item)
    {
        await InitAsync();
        return item.Id == 0
            ? await _db.InsertAsync(item)
            : await _db.UpdateAsync(item);
    }

    public async Task<int> DeleteTransactionAsync(Transaction item)
    {
        await InitAsync();
        return await _db.DeleteAsync(item);
    }

    public async Task<List<Transaction>> GetManualExpensesAsync(int year, int month)
    {
        if (year < 1 || year > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }

        if (month < 1 || month > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month));
        }

        await InitAsync();

        var monthStartUtc = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextMonthStartUtc = monthStartUtc.AddMonths(1);

        return await _db.Table<Transaction>()
            .Where(x =>
                x.Type == ManualExpenseType &&
                x.DateUtc >= monthStartUtc &&
                x.DateUtc < nextMonthStartUtc)
            .OrderByDescending(x => x.DateUtc)
            .ToListAsync();
    }

    public async Task<int> SaveManualExpenseAsync(
        string notes,
        decimal amount,
        DateTime dateUtc,
        string category,
        int? existingId = null)
    {
        await InitAsync();

        var item = new Transaction
        {
            Id = existingId ?? 0,
            Type = ManualExpenseType,
            Amount = -Math.Abs(amount),
            DateUtc = dateUtc,
            Notes = notes,
            Category = category
        };

        return item.Id == 0
            ? await _db.InsertAsync(item)
            : await _db.UpdateAsync(item);
    }

    public async Task<Transaction?> GetManualExpenseByIdAsync(int id)
    {
        await InitAsync();
        return await _db.Table<Transaction>()
            .Where(x => x.Id == id && x.Type == ManualExpenseType)
            .FirstOrDefaultAsync();
    }

    public async Task<List<BudgetLimit>> GetBudgetLimitsAsync()
    {
        await InitAsync();
        return await _db.Table<BudgetLimit>()
            .OrderBy(x => x.Category)
            .ToListAsync();
    }

    public async Task<int> SaveBudgetLimitAsync(BudgetLimit item)
    {
        await InitAsync();
        item.Category = item.Category.Trim();
        return item.Id == 0
            ? await _db.InsertAsync(item)
            : await _db.UpdateAsync(item);
    }

    public async Task<int> DeleteBudgetLimitAsync(BudgetLimit item)
    {
        await InitAsync();
        return await _db.DeleteAsync(item);
    }

    public async Task<List<SavingsGoal>> GetSavingsGoalsAsync()
    {
        await InitAsync();
        return await _db.Table<SavingsGoal>()
            .OrderBy(x => x.CreatedUtc)
            .ToListAsync();
    }

    public async Task<int> SaveSavingsGoalAsync(SavingsGoal item)
    {
        await InitAsync();
        return item.Id == 0
            ? await _db.InsertAsync(item)
            : await _db.UpdateAsync(item);
    }

    public async Task<int> DeleteSavingsGoalAsync(SavingsGoal item)
    {
        await InitAsync();
        return await _db.DeleteAsync(item);
    }

    public async Task ProcessMonthlyRecurringAsync(int year, int month)
    {
        if (year < 1 || year > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }

        if (month < 1 || month > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month));
        }

        await InitAsync();

        var monthStartUtc = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextMonthStartUtc = monthStartUtc.AddMonths(1);
        var daysInMonth = DateTime.DaysInMonth(year, month);

        var incomeSources = await _db.Table<IncomeSource>()
            .Where(x => x.IsActive)
            .ToListAsync();

        foreach (var incomeSource in incomeSources)
        {
            var existingCount = await _db.Table<Transaction>()
                .Where(x =>
                    x.Type == RecurringIncomeType &&
                    x.IncomeSourceId == incomeSource.Id &&
                    x.DateUtc >= monthStartUtc &&
                    x.DateUtc < nextMonthStartUtc)
                .CountAsync();

            if (existingCount > 0)
            {
                continue;
            }

            var scheduledDay = ClampDayOfMonth(incomeSource.DayOfMonth, daysInMonth);
            var scheduledUtc = new DateTime(year, month, scheduledDay, 12, 0, 0, DateTimeKind.Utc);

            await _db.InsertAsync(new Transaction
            {
                Type = RecurringIncomeType,
                Amount = Math.Abs(incomeSource.Amount),
                DateUtc = scheduledUtc,
                IncomeSourceId = incomeSource.Id,
                Notes = $"{incomeSource.Name} monthly income"
            });
        }

        var subscriptions = await _db.Table<Subscription>()
            .Where(x => x.IsActive)
            .ToListAsync();

        foreach (var subscription in subscriptions)
        {
            var existingCount = await _db.Table<Transaction>()
                .Where(x =>
                    x.Type == RecurringSubscriptionType &&
                    x.SubscriptionId == subscription.Id &&
                    x.DateUtc >= monthStartUtc &&
                    x.DateUtc < nextMonthStartUtc)
                .CountAsync();

            if (existingCount > 0)
            {
                continue;
            }

            var scheduledDay = ClampDayOfMonth(subscription.DayOfMonth, daysInMonth);
            var scheduledUtc = new DateTime(year, month, scheduledDay, 12, 0, 0, DateTimeKind.Utc);

            await _db.InsertAsync(new Transaction
            {
                Type = RecurringSubscriptionType,
                Amount = -Math.Abs(subscription.Amount),
                DateUtc = scheduledUtc,
                SubscriptionId = subscription.Id,
                Notes = $"{subscription.Name} monthly subscription"
            });
        }
    }

    public async Task<decimal> GetMonthlyNetBalanceAsync(int year, int month)
    {
        if (year < 1 || year > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }

        if (month < 1 || month > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month));
        }

        await InitAsync();

        var monthStartUtc = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextMonthStartUtc = monthStartUtc.AddMonths(1);

        var monthlyTransactions = await _db.Table<Transaction>()
            .Where(x => x.DateUtc >= monthStartUtc && x.DateUtc < nextMonthStartUtc)
            .ToListAsync();

        return monthlyTransactions.Sum(x => x.Amount);
    }

    public async Task RebuildMonthlyRecurringAsync(int year, int month)
    {
        if (year < 1 || year > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }

        if (month < 1 || month > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month));
        }

        await InitAsync();

        var monthStartUtc = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextMonthStartUtc = monthStartUtc.AddMonths(1);

        var recurringTransactions = await _db.Table<Transaction>()
            .Where(x =>
                (x.Type == RecurringIncomeType || x.Type == RecurringSubscriptionType) &&
                x.DateUtc >= monthStartUtc &&
                x.DateUtc < nextMonthStartUtc)
            .ToListAsync();

        foreach (var item in recurringTransactions)
        {
            await _db.DeleteAsync(item);
        }

        await ProcessMonthlyRecurringAsync(year, month);
    }

    public async Task<List<CategorySpendingReportItem>> GetMonthlyCategorySpendingAsync(int year, int month)
    {
        if (year < 1 || year > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }

        if (month < 1 || month > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month));
        }

        await InitAsync();

        var monthStartUtc = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextMonthStartUtc = monthStartUtc.AddMonths(1);

        var expenses = await _db.Table<Transaction>()
            .Where(x =>
                x.Type == ManualExpenseType &&
                x.DateUtc >= monthStartUtc &&
                x.DateUtc < nextMonthStartUtc)
            .ToListAsync();

        return expenses
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Category) ? "Uncategorized" : x.Category.Trim())
            .Select(g => new CategorySpendingReportItem(g.Key, Math.Abs(g.Sum(x => x.Amount))))
            .OrderByDescending(x => x.SpentAmount)
            .ToList();
    }

    public async Task<List<BudgetStatusItem>> GetBudgetStatusAsync(int year, int month)
    {
        await InitAsync();
        var limits = await GetBudgetLimitsAsync();
        var spending = await GetMonthlyCategorySpendingAsync(year, month);

        return limits
            .Select(limit =>
            {
                var spent = spending
                    .Where(x => string.Equals(x.Category, limit.Category, StringComparison.OrdinalIgnoreCase))
                    .Sum(x => x.SpentAmount);
                return new BudgetStatusItem(limit.Id, limit.Category, limit.MonthlyLimit, spent);
            })
            .OrderByDescending(x => x.PercentUsed)
            .ToList();
    }

    public async Task<BackupSnapshot> CreateBackupSnapshotAsync()
    {
        await InitAsync();
        return new BackupSnapshot
        {
            ExportedUtc = DateTime.UtcNow,
            Subscriptions = await GetSubscriptionsAsync(),
            IncomeSources = await GetIncomeSourcesAsync(),
            SavingsGoals = await GetSavingsGoalsAsync(),
            Transactions = await GetTransactionsAsync(),
            BudgetLimits = await GetBudgetLimitsAsync()
        };
    }

    public async Task RestoreFromBackupSnapshotAsync(BackupSnapshot snapshot)
    {
        await InitAsync();
        await _db.RunInTransactionAsync(conn =>
        {
            conn.DeleteAll<Subscription>();
            conn.DeleteAll<IncomeSource>();
            conn.DeleteAll<SavingsGoal>();
            conn.DeleteAll<Transaction>();
            conn.DeleteAll<BudgetLimit>();

            foreach (var item in snapshot.Subscriptions ?? new List<Subscription>())
            {
                conn.Insert(item);
            }

            foreach (var item in snapshot.IncomeSources ?? new List<IncomeSource>())
            {
                conn.Insert(item);
            }

            foreach (var item in snapshot.SavingsGoals ?? new List<SavingsGoal>())
            {
                conn.Insert(item);
            }

            foreach (var item in snapshot.Transactions ?? new List<Transaction>())
            {
                conn.Insert(item);
            }

            foreach (var item in snapshot.BudgetLimits ?? new List<BudgetLimit>())
            {
                conn.Insert(item);
            }
        });
    }

    public async Task<string> ExportBackupJsonAsync(string outputDirectory)
    {
        var snapshot = await CreateBackupSnapshotAsync();
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, $"expense_backup_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
        return path;
    }

    public async Task<string> ExportTransactionsCsvAsync(string outputDirectory)
    {
        await InitAsync();
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, $"transactions_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        var transactions = await GetTransactionsAsync();
        var lines = new List<string> { "Id,Type,Amount,DateUtc,Category,Notes" };
        lines.AddRange(transactions.Select(t =>
            $"{t.Id},\"{EscapeCsv(t.Type)}\",{t.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)},{t.DateUtc:O},\"{EscapeCsv(t.Category)}\",\"{EscapeCsv(t.Notes)}\""));
        await File.WriteAllLinesAsync(path, lines);
        return path;
    }

    public async Task RestoreFromJsonFileAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        var snapshot = JsonSerializer.Deserialize<BackupSnapshot>(json);
        if (snapshot is null)
        {
            throw new InvalidOperationException("Backup JSON is invalid.");
        }

        await RestoreFromBackupSnapshotAsync(snapshot);
    }

    private static int ClampDayOfMonth(int configuredDay, int daysInMonth)
    {
        if (configuredDay < 1)
        {
            return 1;
        }

        if (configuredDay > daysInMonth)
        {
            return daysInMonth;
        }

        return configuredDay;
    }

    private async Task EnsureSavingsGoalSchemaAsync()
    {
        var columns = await _db.QueryAsync<ColumnMetadata>("PRAGMA table_info('SavingsGoal')");
        if (columns.All(x => !string.Equals(x.name, "TargetMonths", StringComparison.OrdinalIgnoreCase)))
        {
            await _db.ExecuteAsync("ALTER TABLE SavingsGoal ADD COLUMN TargetMonths INTEGER NOT NULL DEFAULT 12");
        }

        if (columns.All(x => !string.Equals(x.name, "TargetDateUtc", StringComparison.OrdinalIgnoreCase)))
        {
            await _db.ExecuteAsync("ALTER TABLE SavingsGoal ADD COLUMN TargetDateUtc TEXT NULL");
        }
    }

    private async Task EnsureTransactionSchemaAsync()
    {
        var columns = await _db.QueryAsync<ColumnMetadata>("PRAGMA table_info('Transaction')");
        if (columns.All(x => !string.Equals(x.name, "Category", StringComparison.OrdinalIgnoreCase)))
        {
            await _db.ExecuteAsync("ALTER TABLE \"Transaction\" ADD COLUMN Category TEXT NULL");
        }
    }

    private static string EscapeCsv(string? input)
    {
        return (input ?? string.Empty).Replace("\"", "\"\"");
    }

    private sealed class ColumnMetadata
    {
        public string name { get; set; } = string.Empty;
    }

    public sealed class CategorySpendingReportItem
    {
        public CategorySpendingReportItem(string category, decimal spentAmount)
        {
            Category = category;
            SpentAmount = spentAmount;
        }

        public string Category { get; }
        public decimal SpentAmount { get; }
    }

    public sealed class BudgetStatusItem
    {
        public BudgetStatusItem(int budgetLimitId, string category, decimal monthlyLimit, decimal spentAmount)
        {
            BudgetLimitId = budgetLimitId;
            Category = category;
            MonthlyLimit = monthlyLimit;
            SpentAmount = spentAmount;
        }

        public int BudgetLimitId { get; }
        public string Category { get; }
        public decimal MonthlyLimit { get; }
        public decimal SpentAmount { get; }
        public decimal Remaining => MonthlyLimit - SpentAmount;
        public decimal PercentUsed => MonthlyLimit <= 0m ? 0m : SpentAmount / MonthlyLimit * 100m;
        public bool IsOverLimit => MonthlyLimit > 0m && SpentAmount > MonthlyLimit;
        public bool IsNearLimit => !IsOverLimit && MonthlyLimit > 0m && PercentUsed >= 80m;
    }

    public sealed class BackupSnapshot
    {
        public DateTime ExportedUtc { get; set; }
        public List<Subscription> Subscriptions { get; set; } = new();
        public List<IncomeSource> IncomeSources { get; set; } = new();
        public List<SavingsGoal> SavingsGoals { get; set; } = new();
        public List<Transaction> Transactions { get; set; } = new();
        public List<BudgetLimit> BudgetLimits { get; set; } = new();
    }
}
