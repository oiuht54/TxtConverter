using System.Windows;
using System.Windows.Input;

namespace TxtConverter.Views;

public partial class PresetNameWindow : Window {
    public string PresetName { get; private set; } = string.Empty;

    public PresetNameWindow() {
        InitializeComponent();
        NameInputBox.Focus();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) {
        string name = NameInputBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) {
            MessageBox.Show("Please enter a valid preset name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        PresetName = name;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) {
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}