using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TxtConverter.Core;
using TxtConverter.Core.Enums;
using TxtConverter.Core.Logic;
using TxtConverter.Services;

namespace TxtConverter.Views;

public partial class MainWindow : Window
{
    private List<string> _allFoundFiles = new();
    private HashSet<string> _filesSelectedForMerge = new();
    private bool _isProcessing;

    public MainWindow()
    {
        InitializeComponent();

        // Принудительно обновляем текст чекбокса при старте, чтобы не было пусто
        UpdateMergedCheckboxLabel();

        SetupCompressionCombo();
        SetupPresets();

        // Загружаем настройки ПОСЛЕ инициализации UI
        LoadPreferences();

        Log(Loc("log_app_ready"));
    }

    // --- Lifecycle & Settings ---

    // ВАЖНО: Переопределяем стандартный метод закрытия окна
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isProcessing)
        {
            e.Cancel = true; // Не даем закрыть, если идет работа
            return;
        }

        // Сохраняем настройки перед смертью окна
        SavePreferences();

        base.OnClosing(e);
    }

    private void SavePreferences()
    {
        var prefs = PreferenceManager.Instance;

        // Сохраняем путь
        prefs.SetLastSourceDir(SourceDirBox.Text);

        // Сохраняем пресет
        if (PresetCombo.SelectedItem is string preset)
            prefs.SetLastPreset(preset);

        // Сохраняем галочки
        prefs.SetGenerateStructure(StructCb.IsChecked == true);
        prefs.SetCompactMode(CompactCb.IsChecked == true);
        prefs.SetGenerateMerged(MergedCb.IsChecked == true);

        // Сохраняем сжатие
        if (CompressionCombo.SelectedItem is ComboBoxItem item && item.Tag is CompressionLevel lvl)
        {
            prefs.SetCompressionLevel(lvl);
        }

        // Принудительно сбрасываем на диск
        prefs.Save();
    }

    private void LoadPreferences()
    {
        var prefs = PreferenceManager.Instance;

        // 1. Восстанавливаем папку
        string lastDir = prefs.GetLastSourceDir();
        if (!string.IsNullOrWhiteSpace(lastDir) && Directory.Exists(lastDir))
        {
            SourceDirBox.Text = lastDir;
            UpdateMergedCheckboxLabel();
            // Разблокируем кнопку Rescan, так как папка валидная
            RescanBtn.IsEnabled = true;
        }

        // 2. Восстанавливаем пресет
        string lastPreset = prefs.GetLastPreset();
        if (PresetManager.Instance.HasPreset(lastPreset))
            PresetCombo.SelectedItem = lastPreset;
        else
            PresetCombo.SelectedIndex = 0;

        // 3. Восстанавливаем галочки
        StructCb.IsChecked = prefs.GetGenerateStructure();
        CompactCb.IsChecked = prefs.GetCompactMode();
        MergedCb.IsChecked = prefs.GetGenerateMerged();

        // 4. Восстанавливаем сжатие
        CompressionLevel savedComp = prefs.GetCompressionLevel();
        foreach (ComboBoxItem item in CompressionCombo.Items)
        {
            if (item.Tag is CompressionLevel lvl && lvl == savedComp)
            {
                CompressionCombo.SelectedItem = item;
                break;
            }
        }
    }

    // --- Initialization ---

    private void SetupCompressionCombo()
    {
        CompressionCombo.Items.Add(new ComboBoxItem { Content = Loc("ui_comp_none"), Tag = CompressionLevel.None });
        CompressionCombo.Items.Add(new ComboBoxItem { Content = Loc("ui_comp_smart"), Tag = CompressionLevel.Smart });
        CompressionCombo.Items.Add(new ComboBoxItem { Content = Loc("ui_comp_max"), Tag = CompressionLevel.Maximum });

        CompressionCombo.SelectedIndex = 1;
    }

    private void SetupPresets()
    {
        foreach (var preset in PresetManager.Instance.GetPresetNames())
        {
            PresetCombo.Items.Add(preset);
        }
    }

    // --- Logic Handlers ---

    private void SelectSource_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = Loc("ui_source_dir"),
            Multiselect = false
        };

        if (Directory.Exists(SourceDirBox.Text))
        {
            dialog.InitialDirectory = SourceDirBox.Text;
        }

        if (dialog.ShowDialog() == true)
        {
            SetSourceDirectory(dialog.FolderName);
        }
    }

    private void SetSourceDirectory(string path)
    {
        SourceDirBox.Text = path;
        Log(string.Format(Loc("log_dir_selected"), path));

        UpdateMergedCheckboxLabel();

        string? detected = PresetManager.Instance.AutoDetectPreset(path);
        if (detected != null)
        {
            Log($"🤖 Auto-detected project type: {detected}");
            PresetCombo.SelectedItem = detected;
        }

        Rescan_Click(this, new RoutedEventArgs());
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length == 1 && Directory.Exists(files[0]))
            {
                SetSourceDirectory(files[0]);
            }
        }
    }

    private void Preset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetCombo.SelectedItem is string presetName)
        {
            if (presetName != "Manual")
            {
                ExtensionsBox.Text = PresetManager.Instance.GetExtensionsFor(presetName);
                IgnoredBox.Text = PresetManager.Instance.GetIgnoredFoldersFor(presetName);
            }
            Log(string.Format(Loc("log_preset_selected"), presetName));
        }
    }

    private async void Rescan_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SourceDirBox.Text))
        {
            Log(Loc("log_error_no_dir"));
            return;
        }

        SetUiBlocked(true);
        StatusLabel.Text = Loc("ui_status_scanning");
        Log(Loc("log_scanning_start"));
        StatusProgressBar.IsIndeterminate = true;

        try
        {
            var exts = ExtensionsBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            var ignored = IgnoredBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

            var scanner = new FileScanner(exts, ignored);
            _allFoundFiles = await scanner.ScanAsync(SourceDirBox.Text);

            _filesSelectedForMerge = new HashSet<string>(_allFoundFiles);

            Log(string.Format(Loc("log_scan_complete"), _allFoundFiles.Count));
            UpdateButtonsState();
        }
        catch (Exception ex)
        {
            Log(string.Format(Loc("log_scan_error"), ex.Message));
        }
        finally
        {
            SetUiBlocked(false);
            StatusProgressBar.IsIndeterminate = false;
            StatusLabel.Text = Loc("ui_status_waiting");
        }
    }

    private void SelectFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_allFoundFiles.Count == 0) return;

        var dialog = new SelectionWindow(_allFoundFiles, _filesSelectedForMerge, SourceDirBox.Text);
        dialog.Owner = this;

        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            _filesSelectedForMerge = dialog.Result;
            Log(string.Format(Loc("log_files_selected"), _filesSelectedForMerge.Count, _allFoundFiles.Count));
        }
    }

    private async void Convert_Click(object sender, RoutedEventArgs e)
    {
        if (_allFoundFiles.Count == 0)
        {
            Log(Loc("log_no_files"));
            return;
        }

        SetUiBlocked(true);
        LogBox.Clear();
        Log(Loc("log_conversion_start"));
        StatusLabel.Text = Loc("ui_status_converting");
        StatusProgressBar.IsIndeterminate = false;
        StatusProgressBar.Value = 0;

        try
        {
            var ignored = IgnoredBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            CompressionLevel compLevel = CompressionLevel.Smart;
            if (CompressionCombo.SelectedItem is ComboBoxItem item && item.Tag is CompressionLevel lvl)
                compLevel = lvl;

            var converter = new Converter(
                SourceDirBox.Text,
                _allFoundFiles,
                _filesSelectedForMerge,
                ignored,
                StructCb.IsChecked == true,
                CompactCb.IsChecked == true,
                compLevel,
                MergedCb.IsChecked == true
            );

            var progress = new Progress<double>(p => StatusProgressBar.Value = p);
            var status = new Progress<string>(s => StatusLabel.Text = s);

            await converter.RunConversionAsync(progress, status);

            Log("====================");
            Log(Loc("log_conversion_success"));
            Log("====================");
            Log(string.Format(Loc("log_result_path"), Path.Combine(SourceDirBox.Text, ProjectConstants.OutputDirName)));

            StatusLabel.Text = Loc("ui_status_done");
        }
        catch (Exception ex)
        {
            Log(string.Format(Loc("log_conversion_error"), ex.Message));
            StatusLabel.Text = Loc("ui_status_error");
        }
        finally
        {
            SetUiBlocked(false);
            StatusProgressBar.Value = 1;
        }
    }

    // --- Helpers ---

    private void SetUiBlocked(bool isBlocked)
    {
        _isProcessing = isBlocked;
        SelectSourceBtn.IsEnabled = !isBlocked;
        PresetCombo.IsEnabled = !isBlocked;
        RescanBtn.IsEnabled = !isBlocked && !string.IsNullOrEmpty(SourceDirBox.Text);
        ConvertBtn.IsEnabled = !isBlocked && _allFoundFiles.Count > 0;
        SelectFilesBtn.IsEnabled = !isBlocked && _allFoundFiles.Count > 0;

        if (isBlocked) Mouse.OverrideCursor = Cursors.Wait;
        else Mouse.OverrideCursor = null;
    }

    private void UpdateButtonsState()
    {
        bool hasFiles = _allFoundFiles.Count > 0;
        bool hasDir = !string.IsNullOrEmpty(SourceDirBox.Text);

        RescanBtn.IsEnabled = hasDir;
        SelectFilesBtn.IsEnabled = hasFiles;
        ConvertBtn.IsEnabled = hasFiles;
    }

    private void UpdateMergedCheckboxLabel()
    {
        string fileName = "_MergedOutput.txt";
        if (!string.IsNullOrEmpty(SourceDirBox.Text))
        {
            string projName = Path.GetFileName(SourceDirBox.Text);
            fileName = "_" + projName + ProjectConstants.MergedFileSuffix;
        }

        string baseStr = LanguageManager.Instance.GetString("ui_merged_cb");
        // Защита от потери плейсхолдера, если строка из ресурсов битая
        if (baseStr.Contains("{0}"))
            MergedCb.Content = string.Format(baseStr, fileName);
        else
            MergedCb.Content = baseStr + $" ({fileName})";
    }

    private void Log(string message)
    {
        LogBox.AppendText(message + Environment.NewLine);
        LogBox.ScrollToEnd();
    }

    private string Loc(string key) => LanguageManager.Instance.GetString(key);

    // --- Window Control ---

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWin = new SettingsWindow();
        settingsWin.Owner = this;
        settingsWin.ShowDialog();

        // Обновляем тексты, которые созданы вручную в коде
        UpdateManualTexts();
    }

    private void UpdateManualTexts()
    {
        if (CompressionCombo.Items.Count >= 3)
        {
            ((ComboBoxItem)CompressionCombo.Items[0]).Content = Loc("ui_comp_none");
            ((ComboBoxItem)CompressionCombo.Items[1]).Content = Loc("ui_comp_smart");
            ((ComboBoxItem)CompressionCombo.Items[2]).Content = Loc("ui_comp_max");
        }
        UpdateMergedCheckboxLabel();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        // Просто закрываем окно. 
        // Вся логика сохранения теперь в OnClosing.
        Close();
    }
}