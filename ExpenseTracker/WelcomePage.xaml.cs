namespace ExpenseTracker;

public partial class WelcomePage : ContentPage
{
    public WelcomePage()
    {
        InitializeComponent();
    }

    private async void OnStartClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new FinancialQuotePage());
    }
}
