using System.Windows;
using TxtConverter.Services;

namespace TxtConverter;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Load Settings
        PreferenceManager.Instance.Load();

        // 2. Set Language
        var savedLang = PreferenceManager.Instance.GetLanguage();
        LanguageManager.Instance.SetLanguage(savedLang);

        // 3. Telemetry Hook: App Launch
        // We track this event to count daily active users (DAU)
        TelemetryService.Instance.TrackEvent("app_launch", new Dictionary<string, object> {
            { "app_version", "2.0.0" }
        });
    }
}