using System.Windows;

namespace Daynote.App.Account;

/// <summary>The signed-out account view.</summary>
public partial class AccountSignInView : System.Windows.Controls.UserControl
{
    public AccountSignInView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// "Keep using Daynote without an account" simply closes the window. It is a real control rather
    /// than only the close box because declining has to look like a choice, not like giving up.
    /// </summary>
    private void OnSkip(object sender, RoutedEventArgs e) => Window.GetWindow(this)?.Close();
}
