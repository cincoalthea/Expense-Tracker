using SQLite;

namespace ExpenseTracker.Models;

public class SavingsGoal
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull]
    public string Name { get; set; } = string.Empty;

    public decimal TargetAmount { get; set; }

    public decimal CurrentAmount { get; set; }

    public int TargetMonths { get; set; } = 12;

    public DateTime? TargetDateUtc { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
