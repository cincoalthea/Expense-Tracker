using SQLite;

namespace ExpenseTracker.Models;

public class IncomeSource
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull]
    public string Name { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public int DayOfMonth { get; set; }

    public bool IsActive { get; set; } = true;
}
