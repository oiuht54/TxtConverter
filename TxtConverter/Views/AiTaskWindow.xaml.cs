using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TxtConverter.Core;
using TxtConverter.Core.Enums;
using TxtConverter.Core.Logic;
using TxtConverter.Services;
using TxtConverter.Services.Ai;

namespace TxtConverter.Views;

public partial class AiTaskWindow : Window {
    private readonly string _rootPath;
    private readonly List<string> _allFiles;
    private readonly AiProvider _provider;
    private bool _isInitializing = true;

    public List<string>? ResultPaths { get; private set; }

    public AiTaskWindow(string rootPath, List<string> allFiles) {
        InitializeComponent();

        _rootPath = rootPath;
        _allFiles = allFiles;
        _provider = PreferenceManager.Instance.GetAiProvider();

        ProviderHeaderLabel.Text = _provider switch {
            AiProvider.GoogleGemini => "Google Gemini",
            AiProvider.NvidiaNim => "NVIDIA NIM",
            AiProvider.OpenAiCompatible => "OpenAI Compatible",
            _ => _provider.ToString()
        };

        string currentModel = PreferenceManager.Instance.GetAiModel();
        ModelOverrideBox.Text = currentModel;

        if (_provider == AiProvider.GoogleGemini) {
            BudgetOverrideBox.IsEnabled = true;
            BudgetOverrideBox.Text = PreferenceManager.Instance.GetAiThinkingBudget().ToString();
        } else {
            BudgetOverrideBox.IsEnabled = false;
            BudgetOverrideBox.Text = "N/A";
        }

        // Load custom prompt from preferences
        SystemPromptBox.Text = PreferenceManager.Instance.GetAiSystemPrompt();

        PromptBox.Focus();
        StatusText.Text = Loc("ui_ai_status_ready") ?? "Ready to analyze.";

        _isInitializing = false;
        LoadModels(currentModel);
    }

    private async void LoadModels(string currentSelection) {
        try {
            var client = AiClientFactory.CreateClient();
            var models = await client.GetAvailableModelsAsync();
            if (models.Count > 0) {
                if (!string.IsNullOrWhiteSpace(ModelOverrideBox.Text) && ModelOverrideBox.Text != "N/A") {
                    currentSelection = ModelOverrideBox.Text;
                }
                ModelOverrideBox.Items.Clear();
                foreach (var m in models) ModelOverrideBox.Items.Add(m);
                var match = models.FirstOrDefault(m => m.Equals(currentSelection, StringComparison.OrdinalIgnoreCase));
                if (match != null) {
                    ModelOverrideBox.SelectedItem = match;
                } else {
                    ModelOverrideBox.Text = currentSelection;
                }
            }
        }
        catch { }
    }

    private void PromptBox_TextChanged(object sender, TextChangedEventArgs e) {
        if (PromptWatermark != null) {
            PromptWatermark.Visibility = string.IsNullOrEmpty(PromptBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void SystemPromptBox_TextChanged(object sender, TextChangedEventArgs e) {
        if (_isInitializing) return;
        PreferenceManager.Instance.SetAiSystemPrompt(SystemPromptBox.Text);
    }

    private void ResetPrompt_Click(object sender, RoutedEventArgs e) {
        string confirmMsg = Loc("ui_ai_prompt_reset_confirm") ?? "Reset system prompt to factory default?";
        string title = Loc("ui_ai_window_title") ?? "AI Smart Select";

        var confirm = MessageBox.Show(confirmMsg, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm == MessageBoxResult.Yes) {
            PreferenceManager.Instance.ResetAiSystemPrompt();
            SystemPromptBox.Text = PreferenceManager.Instance.GetAiSystemPrompt();
        }
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e) {
        string prompt = PromptBox.Text.Trim();
        if (string.IsNullOrEmpty(prompt)) {
            MessageBox.Show(Loc("ui_ai_input_required") ?? "Please describe your task.", Loc("ui_status_error") ?? "Input required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string model = ModelOverrideBox.Text.Trim();
        int budget = 0;
        int.TryParse(BudgetOverrideBox.Text, out budget);

        // Ensure latest prompt changes are persisted
        string customPrompt = SystemPromptBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(customPrompt)) {
            customPrompt = ProjectConstants.DefaultAiSystemPrompt;
        }
        PreferenceManager.Instance.SetAiSystemPrompt(customPrompt);

        SetLoading(true);
        RequestBox.Text = Loc("ui_ai_building_context") ?? "Generating context...";
        ResponseBox.Text = "Waiting for response...";

        try {
            LoadingStatus.Text = Loc("ui_ai_packing_files") ?? "Packing project files...";
            var contextBuilder = new ContextBuilder(_rootPath, _allFiles, PreferenceManager.Instance.GetCompressionLevel());
            var reporter = new Progress<string>(s => LoadingStatus.Text = s);
            string projectContext = await contextBuilder.BuildContextAsync(reporter);

            string sendFormat = Loc("ui_ai_sending_to_ai") ?? "Sending to {0} (Large projects may take 30+ sec)...";
            LoadingStatus.Text = string.Format(sendFormat, _provider);

            var client = AiClientFactory.CreateClient();
            var result = await client.AnalyzeProjectAsync(prompt, projectContext, model, budget, customPrompt);

            RequestBox.Text = result.RequestJson;

            var sbResp = new StringBuilder();
            sbResp.AppendLine($"=== {result.ProviderName} Response ===");
            sbResp.AppendLine(result.RawContentText);
            sbResp.AppendLine();
            sbResp.AppendLine("=== RAW API JSON RESPONSE ===");
            sbResp.AppendLine(result.RawResponseJson);
            ResponseBox.Text = sbResp.ToString();

            if (result.SelectedFiles.Count == 0) {
                StatusText.Text = "AI returned 0 files.";
                MessageBox.Show(Loc("ui_ai_no_selection_msg") ?? "AI response was received but contained no file selection. Check the 'AI Response' tab.", Loc("ui_status_error") ?? "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var resolvedPaths = MatchFiles(result.SelectedFiles);
            if (resolvedPaths.Count == 0) {
                StatusText.Text = "Matching failed.";
                MessageBox.Show(Loc("ui_ai_matching_failed_msg") ?? "AI suggested files, but none could be matched to local paths. Check 'AI Response' tab.", Loc("ui_status_error") ?? "Matching Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ResultPaths = resolvedPaths;
            StatusText.Text = string.Format(Loc("ui_ai_status_analyzed") ?? "Selected {0} files.", resolvedPaths.Count);

            string confirmTemplate = Loc("ui_ai_apply_selection_confirm") ?? "AI identified {0} relevant files.\nApply this selection?";
            var confirm = MessageBox.Show(string.Format(confirmTemplate, resolvedPaths.Count), Loc("ui_status_done") ?? "Done", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm == MessageBoxResult.Yes) {
                DialogResult = true;
                Close();
            }
        }
        catch (Exception ex) {
            MessageBox.Show($"Error: {ex.Message}\nCheck Debug Tabs for details.", Loc("ui_status_error") ?? "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = Loc("ui_status_error") ?? "Error occurred.";
        }
        finally {
            SetLoading(false);
        }
    }

    private List<string> MatchFiles(List<string> aiPaths) {
        var matched = new HashSet<string>();
        var fileMap = new Dictionary<string, string>();
        foreach (var file in _allFiles) {
            string key = Path.GetFullPath(file).ToLower();
            if (!fileMap.ContainsKey(key)) fileMap[key] = file;
        }

        foreach (var rawAiPath in aiPaths) {
            string aiClean = rawAiPath.Trim().Trim('\"', '\'');
            if (string.IsNullOrWhiteSpace(aiClean)) continue;

            string? foundOriginalPath = null;
            try {
                string fullPathCandidate = Path.GetFullPath(Path.Combine(_rootPath, aiClean)).ToLower();
                if (fileMap.TryGetValue(fullPathCandidate, out var original)) {
                    foundOriginalPath = original;
                }
            } catch { }

            if (foundOriginalPath == null) {
                string aiNorm = aiClean.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).ToLower();
                var matchKey = fileMap.Keys.FirstOrDefault(k =>
                    k.EndsWith(Path.DirectorySeparatorChar + aiNorm) ||
                    k == aiNorm
                );
                if (matchKey != null) {
                    foundOriginalPath = fileMap[matchKey];
                }
            }

            if (foundOriginalPath == null) {
                string aiFileName = Path.GetFileName(aiClean).ToLower();
                var candidates = _allFiles.Where(f => Path.GetFileName(f).ToLower() == aiFileName).ToList();
                if (candidates.Count == 1) {
                    foundOriginalPath = candidates[0];
                }
            }

            if (foundOriginalPath != null) {
                matched.Add(foundOriginalPath);
            }
        }

        return matched.ToList();
    }

    private void SetLoading(bool isLoading) {
        LoadingOverlay.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        PromptBox.IsEnabled = !isLoading;
        AnalyzeBtn.IsEnabled = !isLoading;
        ModelOverrideBox.IsEnabled = !isLoading;
        SystemPromptBox.IsEnabled = !isLoading;
        if (_provider == AiProvider.GoogleGemini) BudgetOverrideBox.IsEnabled = !isLoading;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) {
        if (!string.IsNullOrWhiteSpace(SystemPromptBox.Text)) {
            PreferenceManager.Instance.SetAiSystemPrompt(SystemPromptBox.Text);
        }
        Close();
    }

    private string? Loc(string key) {
        string text = LanguageManager.Instance.GetString(key);
        return text.StartsWith("!") && text.EndsWith("!") ? null : text;
    }
}