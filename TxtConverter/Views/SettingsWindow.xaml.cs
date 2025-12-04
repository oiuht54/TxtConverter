using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TxtConverter.Core;
using TxtConverter.Services;

namespace TxtConverter.Views;

public partial class SettingsWindow : Window
{
    private bool _ignoreChanges;

    public SettingsWindow()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        _ignoreChanges = true;

        // Language
        LanguageCombo.Items.Add(new ComboBoxItem { Content = "English", Tag = ProjectConstants.LangEn });
        LanguageCombo.Items.Add(new ComboBoxItem { Content = "Русский", Tag = ProjectConstants.LangRu });

        string current = LanguageManager.Instance.CurrentLanguage;
        foreach (ComboBoxItem item in LanguageCombo.Items)
        {
            if (item.Tag.ToString() == current)
            {
                LanguageCombo.SelectedItem = item;
                break;
            }
        }

        // Telemetry
        TelemetryCb.IsChecked = PreferenceManager.Instance.GetTelemetryEnabled();

        // AI
        ApiKeyBox.Password = PreferenceManager.Instance.GetAiApiKey();
        string savedModel = PreferenceManager.Instance.GetAiModel();
        ModelCombo.Text = savedModel;
        ThinkingCb.IsChecked = PreferenceManager.Instance.GetAiThinkingEnabled();
        BudgetBox.Text = PreferenceManager.Instance.GetAiThinkingBudget().ToString();

        _ignoreChanges = false;

        if (!string.IsNullOrEmpty(ApiKeyBox.Password))
        {
            FetchModels(savedModel);
        }
    }

    private async void RefreshModels_Click(object sender, RoutedEventArgs e)
    {
        await FetchModels(ModelCombo.Text);
    }

    private async Task FetchModels(string currentSelection)
    {
        if (string.IsNullOrEmpty(ApiKeyBox.Password)) return;

        ModelCombo.IsEnabled = false;

        var client = new GeminiClient();
        // Temporarily set key in manager so client picks it up if changed but not saved
        PreferenceManager.Instance.SetAiApiKey(ApiKeyBox.Password);

        var models = await client.GetAvailableModelsAsync();

        if (models.Count > 0)
        {
            ModelCombo.Items.Clear();
            foreach (var m in models)
            {
                ModelCombo.Items.Add(m);
            }

            var match = models.FirstOrDefault(m => m.Equals(currentSelection, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                ModelCombo.SelectedItem = match;
            }
            else
            {
                ModelCombo.Text = currentSelection;
            }
        }

        ModelCombo.IsEnabled = true;
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ignoreChanges) return;
        if (LanguageCombo.SelectedItem is ComboBoxItem item)
        {
            string code = item.Tag.ToString() ?? ProjectConstants.LangEn;
            LanguageManager.Instance.SetLanguage(code);
        }
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_ignoreChanges) return;
        PreferenceManager.Instance.SetAiApiKey(ApiKeyBox.Password);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            this.DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        this.Close();
    }

    private void SaveSettings()
    {
        PreferenceManager.Instance.SetAiModel(ModelCombo.Text);
        PreferenceManager.Instance.SetAiThinkingEnabled(ThinkingCb.IsChecked == true);

        if (int.TryParse(BudgetBox.Text, out int budget))
        {
            PreferenceManager.Instance.SetAiThinkingBudget(budget);
        }

        PreferenceManager.Instance.SetTelemetryEnabled(TelemetryCb.IsChecked == true);
    }

    private void Link_MouseDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://aistudio.google.com/app/apikey") { UseShellExecute = true });
        }
        catch { }
    }
}