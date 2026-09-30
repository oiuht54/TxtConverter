using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TxtConverter.Core;
using TxtConverter.Core.Enums;
using TxtConverter.Services;
using TxtConverter.Services.Ai;

namespace TxtConverter.Views;

public partial class SettingsWindow : Window {
    private bool _ignoreChanges;
    private AiProvider _currentProvider;

    public SettingsWindow() {
        InitializeComponent();
        LoadSettings();
        UpdateAiCardVisibility();
    }

    private void AiEnabledCb_StateChanged(object sender, RoutedEventArgs e) {
        if (_ignoreChanges) return;
        if (AiEnabledCb != null) {
#pragma warning disable CS0618
            PreferenceManager.Instance.SetAiEnabled(AiEnabledCb.IsChecked == true);
#pragma warning restore CS0618
            UpdateAiCardVisibility();
        }
    }

    private void UpdateAiCardVisibility() {
#pragma warning disable CS0618
        bool aiEnabled = PreferenceManager.Instance.GetAiEnabled();
#pragma warning restore CS0618
        if (AiSettingsCard != null) {
            AiSettingsCard.Visibility = aiEnabled ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void LoadSettings() {
        _ignoreChanges = true;

        // Language settings
        LanguageCombo.Items.Add(new ComboBoxItem { Content = "English", Tag = ProjectConstants.LangEn });
        LanguageCombo.Items.Add(new ComboBoxItem { Content = "Русский", Tag = ProjectConstants.LangRu });
        string currentLang = LanguageManager.Instance.CurrentLanguage;
        foreach (ComboBoxItem item in LanguageCombo.Items) {
            if (item.Tag.ToString() == currentLang) {
                LanguageCombo.SelectedItem = item;
                break;
            }
        }

        // Global exclusions & ignores
        GlobalIgnoredBox.Text = PreferenceManager.Instance.GetGlobalIgnoredFolders();
        GlobalExcludedBox.Text = PreferenceManager.Instance.GetGlobalExcludedPaths();

        // Telemetry
        TelemetryCb.IsChecked = PreferenceManager.Instance.GetTelemetryEnabled();

        // AI Enablement Checkbox
#pragma warning disable CS0618
        AiEnabledCb.IsChecked = PreferenceManager.Instance.GetAiEnabled();
#pragma warning restore CS0618

        // AI Providers
        _currentProvider = PreferenceManager.Instance.GetAiProvider();
        foreach (ComboBoxItem item in ProviderCombo.Items) {
            if (item.Tag is string tag && tag == _currentProvider.ToString()) {
                ProviderCombo.SelectedItem = item;
                break;
            }
        }

        UpdateAiFields();

        // Version info display
        VersionText.Text = ProjectConstants.CurrentVersion;

        _ignoreChanges = false;

        if ((!string.IsNullOrEmpty(ApiKeyBox.Password) || _currentProvider == AiProvider.OpenAiCompatible) && ModelCombo.Items.Count == 0) {
            _ = FetchModels(ModelCombo.Text);
        }
    }

    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (_ignoreChanges) return;
        SaveAiStateForProvider(_currentProvider);
        if (ProviderCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag) {
            if (Enum.TryParse<AiProvider>(tag, out var newProvider)) {
                _currentProvider = newProvider;
                UpdateAiFields();
            }
        }
    }

    private void UpdateAiFields() {
        _ignoreChanges = true;
        if (TestStatusLabel != null) {
            TestStatusLabel.Text = string.Empty;
        }

        ModelCombo.Items.Clear();

        if (_currentProvider == AiProvider.GoogleGemini) {
            CustomEndpointPanel.Visibility = Visibility.Collapsed;
            GeminiPanel.Visibility = Visibility.Visible;
            NvidiaPanel.Visibility = Visibility.Collapsed;
            CustomOpenAiPanel.Visibility = Visibility.Collapsed;

            ApiKeyBox.Password = PreferenceManager.Instance.GetGeminiApiKey();
            string geminiModel = PreferenceManager.Instance.GetGeminiModel();
            ThinkingCb.IsChecked = PreferenceManager.Instance.GetAiThinkingEnabled();
            BudgetBox.Text = PreferenceManager.Instance.GetAiThinkingBudget().ToString();
            ApiKeyLink.Text = "https://aistudio.google.com/app/apikey";

            var defaultGeminiModels = new[] { "gemini-2.5-flash", "gemini-2.0-flash", "gemini-1.5-flash", "gemini-1.5-pro" };
            foreach (var m in defaultGeminiModels) ModelCombo.Items.Add(m);
            if (!defaultGeminiModels.Contains(geminiModel) && !string.IsNullOrWhiteSpace(geminiModel)) {
                ModelCombo.Items.Insert(0, geminiModel);
            }
            ModelCombo.Text = geminiModel;
        }
        else if (_currentProvider == AiProvider.NvidiaNim) {
            CustomEndpointPanel.Visibility = Visibility.Collapsed;
            GeminiPanel.Visibility = Visibility.Collapsed;
            NvidiaPanel.Visibility = Visibility.Visible;
            CustomOpenAiPanel.Visibility = Visibility.Collapsed;

            ApiKeyBox.Password = PreferenceManager.Instance.GetNvidiaApiKey();
            string nvidiaModel = PreferenceManager.Instance.GetNvidiaModel();
            int tokens = PreferenceManager.Instance.GetNvidiaMaxTokens();
            if (tokens == 4096 && PreferenceManager.Instance.GetNvidiaReasoningEnabled()) tokens = 8192;
            NvMaxTokensBox.Text = tokens.ToString();
            NvTempBox.Text = PreferenceManager.Instance.GetNvidiaTemperature().ToString("F1", CultureInfo.InvariantCulture);
            NvTopPBox.Text = PreferenceManager.Instance.GetNvidiaTopP().ToString("F2", CultureInfo.InvariantCulture);
            NvReasoningCb.IsChecked = PreferenceManager.Instance.GetNvidiaReasoningEnabled();
            ApiKeyLink.Text = "https://build.nvidia.com/explore/discover";

            var defaultNvidiaModels = new[] { "minimaxai/minimax-m2", "deepseek-ai/deepseek-r1", "meta/llama-3.1-70b-instruct", "nvidia/llama-3.1-nemotron-70b-instruct" };
            foreach (var m in defaultNvidiaModels) ModelCombo.Items.Add(m);
            if (!defaultNvidiaModels.Contains(nvidiaModel) && !string.IsNullOrWhiteSpace(nvidiaModel)) {
                ModelCombo.Items.Insert(0, nvidiaModel);
            }
            ModelCombo.Text = nvidiaModel;
        }
        else if (_currentProvider == AiProvider.OpenAiCompatible) {
            CustomEndpointPanel.Visibility = Visibility.Visible;
            GeminiPanel.Visibility = Visibility.Collapsed;
            NvidiaPanel.Visibility = Visibility.Collapsed;
            CustomOpenAiPanel.Visibility = Visibility.Visible;

            CustomEndpointBox.Text = PreferenceManager.Instance.GetCustomOpenAiEndpoint();
            ApiKeyBox.Password = PreferenceManager.Instance.GetCustomOpenAiApiKey();
            string customModel = PreferenceManager.Instance.GetCustomOpenAiModel();
            CustomMaxTokensBox.Text = PreferenceManager.Instance.GetCustomOpenAiMaxTokens().ToString();
            CustomTempBox.Text = PreferenceManager.Instance.GetCustomOpenAiTemperature().ToString("F1", CultureInfo.InvariantCulture);
            ApiKeyLink.Text = "https://platform.openai.com/api-keys";

            var defaultCustomModels = new[] { "gpt-4o-mini", "gpt-4o", "deepseek-chat", "qwen-2.5-coder-32b", "llama-3.1-8b" };
            foreach (var m in defaultCustomModels) ModelCombo.Items.Add(m);
            if (!defaultCustomModels.Contains(customModel) && !string.IsNullOrWhiteSpace(customModel)) {
                ModelCombo.Items.Insert(0, customModel);
            }
            ModelCombo.Text = customModel;
        }

        _ignoreChanges = false;
    }

    private void SaveAiStateForProvider(AiProvider provider) {
        if (provider == AiProvider.GoogleGemini) {
            PreferenceManager.Instance.SetGeminiApiKey(ApiKeyBox.Password);
            PreferenceManager.Instance.SetGeminiModel(ModelCombo.Text);
            PreferenceManager.Instance.SetAiThinkingEnabled(ThinkingCb.IsChecked == true);
            if (int.TryParse(BudgetBox.Text, out int budget))
                PreferenceManager.Instance.SetAiThinkingBudget(budget);
        }
        else if (provider == AiProvider.NvidiaNim) {
            PreferenceManager.Instance.SetNvidiaApiKey(ApiKeyBox.Password);
            PreferenceManager.Instance.SetNvidiaModel(ModelCombo.Text);
            if (int.TryParse(NvMaxTokensBox.Text, out int tokens))
                PreferenceManager.Instance.SetNvidiaMaxTokens(tokens);
            if (double.TryParse(NvTempBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double temp))
                PreferenceManager.Instance.SetNvidiaTemperature(temp);
            if (double.TryParse(NvTopPBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double topP))
                PreferenceManager.Instance.SetNvidiaTopP(topP);
            PreferenceManager.Instance.SetNvidiaReasoningEnabled(NvReasoningCb.IsChecked == true);
        }
        else if (provider == AiProvider.OpenAiCompatible) {
            PreferenceManager.Instance.SetCustomOpenAiEndpoint(CustomEndpointBox.Text.Trim());
            PreferenceManager.Instance.SetCustomOpenAiApiKey(ApiKeyBox.Password);
            PreferenceManager.Instance.SetCustomOpenAiModel(ModelCombo.Text.Trim());
            if (int.TryParse(CustomMaxTokensBox.Text, out int tokens))
                PreferenceManager.Instance.SetCustomOpenAiMaxTokens(tokens);
            if (double.TryParse(CustomTempBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double temp))
                PreferenceManager.Instance.SetCustomOpenAiTemperature(temp);
        }
    }

    private async void RefreshModels_Click(object sender, RoutedEventArgs e) {
        await FetchModels(ModelCombo.Text);
        if (ModelCombo.Items.Count > 0) {
            ModelCombo.IsDropDownOpen = true;
        }
    }

    private async Task FetchModels(string currentSelection) {
        if (_currentProvider != AiProvider.OpenAiCompatible && string.IsNullOrEmpty(ApiKeyBox.Password)) return;
        if (_currentProvider == AiProvider.OpenAiCompatible && string.IsNullOrWhiteSpace(CustomEndpointBox.Text)) return;

        ModelCombo.IsEnabled = false;
        RefreshModelsBtn.IsEnabled = false;
        if (TestStatusLabel != null) {
            TestStatusLabel.Foreground = Brushes.Orange;
            TestStatusLabel.Text = LanguageManager.Instance.GetString("ui_settings_fetching_models");
        }

        try {
            string? endpoint = _currentProvider == AiProvider.OpenAiCompatible ? CustomEndpointBox.Text.Trim() : null;
            var client = AiClientFactory.CreateSpecific(_currentProvider, ApiKeyBox.Password, currentSelection, endpoint);
            var models = await client.GetAvailableModelsAsync();
            if (models.Count > 0) {
                ModelCombo.Items.Clear();
                foreach (var m in models) ModelCombo.Items.Add(m);

                var match = models.FirstOrDefault(m => m.Equals(currentSelection, StringComparison.OrdinalIgnoreCase));
                if (match != null) ModelCombo.SelectedItem = match;
                else ModelCombo.Text = currentSelection;

                if (TestStatusLabel != null) {
                    TestStatusLabel.Foreground = Brushes.LightGreen;
                    string formatStr = LanguageManager.Instance.GetString("ui_settings_models_found");
                    TestStatusLabel.Text = string.Format(formatStr, models.Count);
                }
            } else {
                if (TestStatusLabel != null) {
                    TestStatusLabel.Foreground = Brushes.Tomato;
                    TestStatusLabel.Text = LanguageManager.Instance.GetString("ui_settings_no_models_found");
                }
            }
        }
        catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Error fetching models: {ex.Message}");
            if (TestStatusLabel != null) {
                TestStatusLabel.Foreground = Brushes.Tomato;
                TestStatusLabel.Text = ex.Message;
            }
        }
        finally {
            ModelCombo.IsEnabled = true;
            RefreshModelsBtn.IsEnabled = true;
        }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e) {
        string model = ModelCombo.Text.Trim();
        string apiKey = ApiKeyBox.Password.Trim();
        string endpoint = CustomEndpointBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(model)) {
            MessageBox.Show(
                LanguageManager.Instance.GetString("msg_test_no_model"),
                LanguageManager.Instance.GetString("ui_status_error"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        if (_currentProvider != AiProvider.OpenAiCompatible && string.IsNullOrWhiteSpace(apiKey)) {
            MessageBox.Show(
                LanguageManager.Instance.GetString("msg_test_no_key"),
                LanguageManager.Instance.GetString("ui_status_error"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        if (_currentProvider == AiProvider.OpenAiCompatible && string.IsNullOrWhiteSpace(endpoint)) {
            MessageBox.Show(
                LanguageManager.Instance.GetString("msg_test_no_endpoint"),
                LanguageManager.Instance.GetString("ui_status_error"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        TestConnectionBtn.IsEnabled = false;
        TestStatusLabel.Foreground = Brushes.Orange;
        TestStatusLabel.Text = LanguageManager.Instance.GetString("ui_settings_test_testing");

        try {
            string? resolvedEndpoint = _currentProvider == AiProvider.OpenAiCompatible ? endpoint : null;
            var client = AiClientFactory.CreateSpecific(_currentProvider, apiKey, model, resolvedEndpoint);
            string response = await client.TestConnectionAsync(model);

            TestStatusLabel.Foreground = Brushes.LightGreen;
            TestStatusLabel.Text = $"OK: {response}";

            string successTemplate = LanguageManager.Instance.GetString("msg_test_success");
            string title = LanguageManager.Instance.GetString("ui_settings_test_title");
            MessageBox.Show(
                string.Format(successTemplate, model, response),
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (Exception ex) {
            TestStatusLabel.Foreground = Brushes.Tomato;
            TestStatusLabel.Text = ex.Message;

            string errorTemplate = LanguageManager.Instance.GetString("msg_test_error");
            string errorTitle = LanguageManager.Instance.GetString("ui_status_error");
            MessageBox.Show(
                string.Format(errorTemplate, ex.Message),
                errorTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        finally {
            TestConnectionBtn.IsEnabled = true;
        }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e) {
        CheckUpdateBtn.IsEnabled = false;
        UpdateStatusLabel.Visibility = Visibility.Visible;
        UpdateStatusLabel.Foreground = Brushes.Orange;
        UpdateStatusLabel.Text = LanguageManager.Instance.GetString("ui_update_checking");

        try {
            var result = await UpdateCheckerService.Instance.CheckForUpdatesDetailedAsync();
            if (result.Success) {
                if (result.IsUpdateAvailable && result.Release != null) {
                    UpdateStatusLabel.Foreground = Brushes.LightGreen;
                    UpdateStatusLabel.Text = string.Format(LanguageManager.Instance.GetString("ui_update_info"), result.Release.TagName, ProjectConstants.CurrentVersion);

                    var updateWin = new UpdateNotificationWindow(result.Release);
                    updateWin.Owner = this;
                    updateWin.ShowDialog();
                }
                else {
                    UpdateStatusLabel.Foreground = Brushes.LightGreen;
                    UpdateStatusLabel.Text = string.Format(LanguageManager.Instance.GetString("ui_update_latest"), ProjectConstants.CurrentVersion);
                }
            }
            else {
                UpdateStatusLabel.Foreground = Brushes.Tomato;
                UpdateStatusLabel.Text = string.Format(LanguageManager.Instance.GetString("ui_update_failed"), result.ErrorMessage);
            }
        }
        catch (Exception ex) {
            UpdateStatusLabel.Foreground = Brushes.Tomato;
            UpdateStatusLabel.Text = string.Format(LanguageManager.Instance.GetString("ui_update_failed"), ex.Message);
        }
        finally {
            CheckUpdateBtn.IsEnabled = true;
        }
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (_ignoreChanges) return;
        if (LanguageCombo.SelectedItem is ComboBoxItem item) {
            string code = item.Tag.ToString() ?? ProjectConstants.LangEn;
            LanguageManager.Instance.SetLanguage(code);
        }
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e) { }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton == MouseButton.Left)
            this.DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) {
        SaveSettings();
        this.Close();
    }

    private void SaveSettings() {
        SaveAiStateForProvider(_currentProvider);
        PreferenceManager.Instance.SetAiProvider(_currentProvider);
        PreferenceManager.Instance.SetTelemetryEnabled(TelemetryCb.IsChecked == true);
        PreferenceManager.Instance.SetGlobalIgnoredFolders(GlobalIgnoredBox.Text);
        PreferenceManager.Instance.SetGlobalExcludedPaths(GlobalExcludedBox.Text);
        if (AiEnabledCb != null) {
#pragma warning disable CS0618
            PreferenceManager.Instance.SetAiEnabled(AiEnabledCb.IsChecked == true);
#pragma warning restore CS0618
        }
        PreferenceManager.Instance.Save();
    }

    private void Link_MouseDown(object sender, MouseButtonEventArgs e) {
        try {
            if (sender is TextBlock tb) {
                string url = tb.Tag?.ToString() ?? tb.Text;
                if (!string.IsNullOrWhiteSpace(url)) {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
            }
        }
        catch { }
    }
}