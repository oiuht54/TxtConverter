using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using TxtConverter.Core.Enums;
using TxtConverter.Core.Logic;
using TxtConverter.Services;

namespace TxtConverter.Views;

public partial class AiTaskWindow : Window
{
    private readonly string _rootPath;
    private readonly List<string> _allFiles;

    public List<string>? ResultPaths { get; private set; }

    public AiTaskWindow(string rootPath, List<string> allFiles)
    {
        InitializeComponent();
        _rootPath = rootPath;
        _allFiles = allFiles;

        // Load Defaults
        string defaultModel = PreferenceManager.Instance.GetAiModel();

        // Set text directly so it's visible immediately
        ModelOverrideBox.Text = defaultModel;
        BudgetOverrideBox.Text = PreferenceManager.Instance.GetAiThinkingBudget().ToString();

        PromptBox.Focus();

        // Auto-fetch models
        LoadModels(defaultModel);
    }

    private async void LoadModels(string currentSelection)
    {
        try
        {
            var client = new GeminiClient();
            var models = await client.GetAvailableModelsAsync();
            if (models.Count > 0)
            {
                if (!string.IsNullOrWhiteSpace(ModelOverrideBox.Text))
                {
                    currentSelection = ModelOverrideBox.Text;
                }

                ModelOverrideBox.Items.Clear();
                foreach (var m in models) ModelOverrideBox.Items.Add(m);

                var match = models.FirstOrDefault(m => m.Equals(currentSelection, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    ModelOverrideBox.SelectedItem = match;
                }
                else
                {
                    ModelOverrideBox.Text = currentSelection;
                }
            }
        }
        catch { }
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        string prompt = PromptBox.Text.Trim();
        if (string.IsNullOrEmpty(prompt))
        {
            MessageBox.Show("Please describe your task.", "Input required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Parse Overrides
        string model = ModelOverrideBox.Text.Trim();
        int budget = 0;
        if (!int.TryParse(BudgetOverrideBox.Text, out budget))
        {
            budget = 0;
        }

        SetLoading(true);
        RequestBox.Text = "Generating context...";
        ResponseBox.Text = "Waiting for response...";

        try
        {
            // 1. Build Context
            LoadingStatus.Text = "Packing project files...";
            var contextBuilder = new ContextBuilder(_rootPath, _allFiles, PreferenceManager.Instance.GetCompressionLevel());
            var reporter = new Progress<string>(s => LoadingStatus.Text = s);
            string projectContext = await contextBuilder.BuildContextAsync(reporter);

            // 2. Call API (Passing overrides)
            LoadingStatus.Text = "Sending to Gemini (Large projects may take 30+ sec)...";
            var client = new GeminiClient();
            var result = await client.AnalyzeProjectAsync(prompt, projectContext, model, budget);

            // 3. Update Debug Tabs
            RequestBox.Text = result.CleanRequestText;

            var sbResp = new StringBuilder();
            sbResp.AppendLine("=== AI CONTENT (Thoughts & Answer) ===");
            sbResp.AppendLine(result.RawContentText);
            sbResp.AppendLine();
            sbResp.AppendLine("=== RAW API JSON RESPONSE ===");
            sbResp.AppendLine(result.RawResponseJson);
            ResponseBox.Text = sbResp.ToString();

            if (result.SelectedFiles.Count == 0)
            {
                StatusText.Text = "AI returned 0 files.";
                MessageBox.Show("AI response was received but contained no file selection. Check the 'AI Response' tab.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 4. Smart Matching (ROBUST VERSION)
            var resolvedPaths = MatchFiles(result.SelectedFiles);

            if (resolvedPaths.Count == 0)
            {
                StatusText.Text = "Matching failed.";
                MessageBox.Show("AI suggested files, but none could be matched to local paths. Check 'AI Response' tab.", "Matching Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ResultPaths = resolvedPaths;
            StatusText.Text = $"Selected {resolvedPaths.Count} files.";

            var confirm = MessageBox.Show($"AI identified {resolvedPaths.Count} relevant files.\nApply this selection?", "Done", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm == MessageBoxResult.Yes)
            {
                DialogResult = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}\nCheck Debug Tabs for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Error occurred.";
        }
        finally
        {
            SetLoading(false);
        }
    }

    // --- ИСПРАВЛЕННЫЙ МЕТОД MATCHFILES ---
    private List<string> MatchFiles(List<string> aiPaths)
    {
        var matched = new HashSet<string>();

        // 1. Создаем карту для быстрого и точного поиска оригинала.
        // Ключ: полный путь в нижнем регистре (для нечувствительности к регистру).
        // Значение: ОРИГИНАЛЬНАЯ строка из _allFiles. 
        // Это критически важно, чтобы HashSet в MainWindow "узнал" эти строки.
        var fileMap = new Dictionary<string, string>();
        foreach (var file in _allFiles)
        {
            // Используем GetFullPath для стандартизации слешей
            string key = Path.GetFullPath(file).ToLower();
            if (!fileMap.ContainsKey(key)) fileMap[key] = file;
        }

        foreach (var rawAiPath in aiPaths)
        {
            // Очистка пути от AI (убираем кавычки, пробелы)
            string aiClean = rawAiPath.Trim().Trim('\"', '\'');
            if (string.IsNullOrWhiteSpace(aiClean)) continue;

            string? foundOriginalPath = null;

            // Попытка 1: Абсолютный путь (Combine с корнем)
            try
            {
                string fullPathCandidate = Path.GetFullPath(Path.Combine(_rootPath, aiClean)).ToLower();
                if (fileMap.TryGetValue(fullPathCandidate, out var original))
                {
                    foundOriginalPath = original;
                }
            }
            catch { }

            // Попытка 2: Поиск по суффиксу (если AI вернул относительный путь без полного совпадения)
            if (foundOriginalPath == null)
            {
                // Нормализуем слеши под текущую ОС для сравнения
                string aiNorm = aiClean.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).ToLower();

                // Ищем файл, путь которого заканчивается на то, что дал ИИ
                // Проверяем с разделителем, чтобы избежать совпадений типа "ile.cs" -> "Profile.cs"
                var matchKey = fileMap.Keys.FirstOrDefault(k =>
                    k.EndsWith(Path.DirectorySeparatorChar + aiNorm) ||
                    k == aiNorm // на случай если это просто имя файла
                );

                if (matchKey != null)
                {
                    foundOriginalPath = fileMap[matchKey];
                }
            }

            // Попытка 3: Просто по имени файла (Fallback, если AI ошибся в папках)
            if (foundOriginalPath == null)
            {
                string aiFileName = Path.GetFileName(aiClean).ToLower();
                var candidates = _allFiles.Where(f => Path.GetFileName(f).ToLower() == aiFileName).ToList();

                // Берем только если имя файла уникально во всем проекте
                if (candidates.Count == 1)
                {
                    foundOriginalPath = candidates[0];
                }
            }

            if (foundOriginalPath != null)
            {
                matched.Add(foundOriginalPath);
            }
        }

        return matched.ToList();
    }

    private void SetLoading(bool isLoading)
    {
        LoadingOverlay.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        PromptBox.IsEnabled = !isLoading;
        AnalyzeBtn.IsEnabled = !isLoading;
        ModelOverrideBox.IsEnabled = !isLoading;
        BudgetOverrideBox.IsEnabled = !isLoading;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}