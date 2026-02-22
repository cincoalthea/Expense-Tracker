namespace ExpenseTracker;

public partial class FinancialQuotePage : ContentPage
{
    private static readonly string[] Quotes =
    {
        "Do not save what is left after spending; spend what is left after saving.",
        "A budget tells your money where to go instead of wondering where it went.",
        "Small daily choices become big financial results over time.",
        "Emergency savings are self-care for your future self.",
        "Debt grows quietly; so does wealth when you stay consistent.",
        "Every dollar should have a job.",
        "Investing is less about timing the market and more about time in the market.",
        "Financial freedom starts with financial awareness."
    };

    public FinancialQuotePage()
    {
        InitializeComponent();
        QuoteLabel.Text = Quotes[Random.Shared.Next(Quotes.Length)];
    }

    private void OnEnterAppClicked(object? sender, EventArgs e)
    {
        var window = Application.Current?.Windows.FirstOrDefault();
        if (window is not null)
        {
            window.Page = new AppShell();
        }
    }
}
