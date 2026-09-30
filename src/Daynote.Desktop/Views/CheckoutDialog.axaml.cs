using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Daynote.App.Account;
using Daynote.Desktop.ViewModels;

namespace Daynote.Desktop.Views;

/// <summary>The checkout dialog. Behaviour is the account view model's; this only closes on the scrim.</summary>
public partial class CheckoutDialog : UserControl
{
    public CheckoutDialog()
    {
        InitializeComponent();
    }

    public AppStringsProxy Strings => AppStringsProxy.Instance;

    /// <summary>A click outside the card closes it, as the design's scrim does; one inside does not.</summary>
    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source && (source == Card || source.GetVisualAncestors().Contains(Card)))
        {
            return;
        }

        if (DataContext is AccountViewModel account)
        {
            account.CloseCheckoutCommand.Execute(null);
        }
    }
}
