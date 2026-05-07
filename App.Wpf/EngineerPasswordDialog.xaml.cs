using System.Windows;

namespace TitanAILivePC;

public partial class EngineerPasswordDialog : Window
{
    private readonly Func<string?, bool> _verifyPassword;

    public EngineerPasswordDialog(Func<string?, bool> verifyPassword)
    {
        InitializeComponent();
        _verifyPassword = verifyPassword;
        Loaded += (_, _) => PasswordInputBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_verifyPassword(PasswordInputBox.Password))
        {
            DialogResult = true;
            Close();
            return;
        }

        MessageBox.Show(this, "Incorrect password.", "Engineer mode", MessageBoxButton.OK, MessageBoxImage.Warning);
        PasswordInputBox.SelectAll();
        PasswordInputBox.Focus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
