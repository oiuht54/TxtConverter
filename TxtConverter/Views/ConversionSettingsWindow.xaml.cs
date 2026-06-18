using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TxtConverter.Core.Enums;
using TxtConverter.Services;

namespace TxtConverter.Views;

public partial class ConversionSettingsWindow : Window {
    public ConversionSettingsWindow() {
        InitializeComponent();
        SetupCombos();
        LoadSettings();
        LocalizeUi();
    }

    private void SetupCombos() {
        // Compression
        CompressionCombo.Items.Clear();
        CompressionCombo.Items.Add(new ComboBoxItem { Content = Loc("ui_comp_none") ?? "None (Original)", Tag = CompressionLevel.None });
        CompressionCombo.Items.Add(new ComboBoxItem { Content = Loc("ui_comp_smart") ?? "Smart (Safe)", Tag = CompressionLevel.Smart });
        CompressionCombo.Items.Add(new ComboBoxItem { Content = Loc("ui_comp_max") ?? "Maximum (Semantic optimization)", Tag = CompressionLevel.Maximum });

        // PDF Mode
        PdfModeCombo.Items.Clear();
        PdfModeCombo.Items.Add(new ComboBoxItem { Content = Loc("ui_pdf_mode_std") ?? "Standard (Optimized for AI Vision)", Tag = PdfMode.Standard });
        PdfModeCombo.Items.Add(new ComboBoxItem { Content = Loc("ui_pdf_mode_compact") ?? "Compact (Page saver)", Tag = PdfMode.Compact });
        PdfModeCombo.Items.Add(new ComboBoxItem { Content = Loc("ui_pdf_mode_extreme") ?? "Extreme (Min pages, no margins)", Tag = PdfMode.Extreme });
    }

    private void LoadSettings() {
        var prefs = PreferenceManager.Instance;

        StructCb.IsChecked = prefs.GetGenerateStructure();
        CompactCb.IsChecked = prefs.GetCompactMode();
        MergedCb.IsChecked = prefs.GetGenerateMerged();
        PdfCb.IsChecked = prefs.GetGeneratePdf();

        // Safe setup for Compression Level Selection
        CompressionLevel savedComp = prefs.GetCompressionLevel();
        foreach (ComboBoxItem item in CompressionCombo.Items) {
            if (item.Tag is CompressionLevel lvl && lvl == savedComp) {
                CompressionCombo.SelectedItem = item;
                break;
            }
        }
        if (CompressionCombo.SelectedItem == null) {
            CompressionCombo.SelectedIndex = 1; // Fallback to Smart
        }

        // Safe setup for PDF Mode Selection
        PdfMode savedPdfMode = prefs.GetPdfMode();
        foreach (ComboBoxItem item in PdfModeCombo.Items) {
            if (item.Tag is PdfMode mode && mode == savedPdfMode) {
                PdfModeCombo.SelectedItem = item;
                break;
            }
        }
        if (PdfModeCombo.SelectedItem == null) {
            PdfModeCombo.SelectedIndex = 0; // Fallback to Standard
        }

        // Apply initial visual enabling
        CompactCb.IsEnabled = StructCb.IsChecked == true;
        PdfModeCombo.IsEnabled = PdfCb.IsChecked == true;
    }

    private void LocalizeUi() {
        TitleTxt.Text = Loc("ui_conversion_settings_title") ?? "Conversion Settings";
        StructCb.Content = Loc("ui_structure_cb") ?? "Generate Structure File";
        CompactCb.Content = Loc("ui_compact_structure_cb") ?? "Compact Mode (Collapse ignored files)";
        CompressionHeader.Text = Loc("ui_compression_label") ?? "Token Compression Level";
        MergedCb.Content = Loc("ui_merged_cb_generic") ?? "Generate Merged File";
        PdfCb.Content = Loc("ui_pdf_cb") ?? "Generate PDF Report";
        CancelBtn.Content = Loc("ui_preset_cancel") ?? "Cancel";
        SaveBtn.Content = Loc("ui_preset_confirm") ?? "Save";

        StructureHeader.Text = "Structure Configuration";
        OutputHeader.Text = "Output targets";
    }

    private void StructCb_Changed(object sender, RoutedEventArgs e) {
        if (CompactCb != null) {
            CompactCb.IsEnabled = StructCb.IsChecked == true;
        }
    }

    private void PdfCb_Changed(object sender, RoutedEventArgs e) {
        if (PdfModeCombo != null) {
            PdfModeCombo.IsEnabled = PdfCb.IsChecked == true;
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) {
        var prefs = PreferenceManager.Instance;

        prefs.SetGenerateStructure(StructCb.IsChecked == true);
        prefs.SetCompactMode(CompactCb.IsChecked == true);
        prefs.SetGenerateMerged(MergedCb.IsChecked == true);
        prefs.SetGeneratePdf(PdfCb.IsChecked == true);

        if (CompressionCombo.SelectedItem is ComboBoxItem cItem && cItem.Tag is CompressionLevel lvl) {
            prefs.SetCompressionLevel(lvl);
        }

        if (PdfModeCombo.SelectedItem is ComboBoxItem pItem && pItem.Tag is PdfMode mode) {
            prefs.SetPdfMode(mode);
        }

        prefs.Save();
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

    private string? Loc(string key) {
        string text = LanguageManager.Instance.GetString(key);
        return text.StartsWith("!") && text.EndsWith("!") ? null : text;
    }
}