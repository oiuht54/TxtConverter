using System.Windows;
using System.Windows.Controls;
using TxtConverter.Core;
using TxtConverter.Services;

namespace TxtConverter.Views;

public partial class SettingsWindow : Window
{
    private bool _ignoreSelectionChange;

    public SettingsWindow()
    {
        InitializeComponent();
        LoadLanguages();
    }

    private void LoadLanguages()
    {
        _ignoreSelectionChange = true;
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
        _ignoreSelectionChange = false;
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ignoreSelectionChange) return;

        if (LanguageCombo.SelectedItem is ComboBoxItem item)
        {
            string code = item.Tag.ToString() ?? ProjectConstants.LangEn;
            LanguageManager.Instance.SetLanguage(code);
        }
    }

    private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            this.DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => this.Close();
}