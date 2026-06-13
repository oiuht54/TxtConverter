using System.Windows;
using TxtConverter.Services;
using QuestPDF.Infrastructure;

namespace TxtConverter;

public partial class App : Application {
    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);

        // 0. QuestPDF License Setup (Community)
        QuestPDF.Settings.License = LicenseType.Community;

        // 1. Load Settings
        PreferenceManager.Instance.Load();

        // 2. Set Language
        var savedLang = PreferenceManager.Instance.GetLanguage();
        LanguageManager.Instance.SetLanguage(savedLang);

        // 3. Telemetry Hook: App Launch using the centralized current version
        TelemetryService.Instance.TrackEvent("app_launch", new Dictionary<string, object> {
            { "app_version", Core.ProjectConstants.CurrentVersion },
            { "pdf_enabled", PreferenceManager.Instance.GetGeneratePdf() }
        });
    }
}