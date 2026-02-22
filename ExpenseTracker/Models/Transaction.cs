using SQLite;

namespace ExpenseTracker.Models;

public class Transaction
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull]
    public string Type { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public DateTime DateUtc { get; set; }

    public int? SubscriptionId { get; set; }

    public int? IncomeSourceId { get; set; }

    public string Notes { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;
}
