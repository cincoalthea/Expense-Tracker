using SQLite;

namespace ExpenseTracker.Models;

public class BudgetLimit
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull]
    public string Category { get; set; } = string.Empty;

    public decimal MonthlyLimit { get; set; }
}
