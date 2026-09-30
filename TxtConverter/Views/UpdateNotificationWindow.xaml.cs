using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using TxtConverter.Services;

namespace TxtConverter.Views;

public partial class UpdateNotificationWindow : Window {
    private readonly ReleaseInfo _release;

    public UpdateNotificationWindow(ReleaseInfo release) {
        InitializeComponent();
        _release = release;

        string infoFormat = LanguageManager.Instance.GetString("ui_update_info");
        VersionInfoText.Text = string.Format(infoFormat, _release.TagName, Core.ProjectConstants.CurrentVersion);
        ChangelogText.Text = string.IsNullOrWhiteSpace(_release.Body)
            ? (string.IsNullOrWhiteSpace(_release.Name) ? _release.TagName : _release.Name)
            : _release.Body;
    }

    private void Download_Click(object sender, RoutedEventArgs e) {
        try {
            string url = !string.IsNullOrWhiteSpace(_release.HtmlUrl)
                ? _release.HtmlUrl
                : Core.ProjectConstants.GitHubUrl;
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) {
            MessageBox.Show($"Could not open download link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) {
        Close();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}