using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using TxtConverter.Core.Enums;
using TxtConverter.Core.Logic;
using TxtConverter.Services;
using TxtConverter.Services.Ai; 

namespace TxtConverter.Views;

public partial class AiTaskWindow : Window {
    private readonly string _rootPath;
    private readonly List<string> _allFiles;
    private readonly AiProvider _provider;

    public List<string>? ResultPaths { get; private set; }

    public AiTaskWindow(string rootPath, List<string> allFiles) {
        InitializeComponent();
        _rootPath = rootPath;
        _allFiles = allFiles;
        
        _provider = PreferenceManager.Instance.GetAiProvider();
        string currentModel = (_provider == AiProvider.NvidiaNim) 
            ? PreferenceManager.Instance.GetNvidiaModel() 
            : PreferenceManager.Instance.GetGeminiModel();

        ModelOverrideBox.Text = currentModel;
        
        if (_provider == AiProvider.GoogleGemini) {
            BudgetOverrideBox.Text = PreferenceManager.Instance.GetAiThinkingBudget().ToString();
        } else {
            BudgetOverrideBox.IsEnabled = false;
            BudgetOverrideBox.Text = "N/A";
        }

        PromptBox.Focus();
        StatusText.Text = $"Using: {_provider}";
        
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

    private async void Analyze_Click(object sender, RoutedEventArgs e) {
        string prompt = PromptBox.Text.Trim();
        if (string.IsNullOrEmpty(prompt)) {
            MessageBox.Show("Please describe your task.", "Input required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string model = ModelOverrideBox.Text.Trim();
        int budget = 0;
        int.TryParse(BudgetOverrideBox.Text, out budget);

        SetLoading(true);
        RequestBox.Text = "Generating context...";
        ResponseBox.Text = "Waiting for response...";

        try {
            LoadingStatus.Text = "Packing project files...";
            var contextBuilder = new ContextBuilder(_rootPath, _allFiles, PreferenceManager.Instance.GetCompressionLevel());
            var reporter = new Progress<string>(s => LoadingStatus.Text = s);
            string projectContext = await contextBuilder.BuildContextAsync(reporter);

            LoadingStatus.Text = $"Sending to {_provider} (Large projects may take 30+ sec)...";
            
            var client = AiClientFactory.CreateClient();
            var result = await client.AnalyzeProjectAsync(prompt, projectContext, model, budget);

            // ИЗМЕНЕНИЕ: Теперь мы показываем полный RequestJson (дамп HTTP), а не просто текст промпта
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
                MessageBox.Show("AI response was received but contained no file selection. Check the 'AI Response' tab.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var resolvedPaths = MatchFiles(result.SelectedFiles);
            if (resolvedPaths.Count == 0) {
                StatusText.Text = "Matching failed.";
                MessageBox.Show("AI suggested files, but none could be matched to local paths. Check 'AI Response' tab.", "Matching Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ResultPaths = resolvedPaths;
            StatusText.Text = $"Selected {resolvedPaths.Count} files.";
            
            var confirm = MessageBox.Show($"AI identified {resolvedPaths.Count} relevant files.\nApply this selection?", "Done", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm == MessageBoxResult.Yes) {
                DialogResult = true;
                Close();
            }
        }
        catch (Exception ex) {
            MessageBox.Show($"Error: {ex.Message}\nCheck Debug Tabs for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Error occurred.";
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
        if (_provider == AiProvider.GoogleGemini) BudgetOverrideBox.IsEnabled = !isLoading;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) {
        Close();
    }
}