using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TxtConverter.Services;

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

        // 4. Pre-warm QuestPDF layout and Skia/HarfBuzz font engine in the background
        // to completely eliminate the 1.5 - 3.0s cold-start lag during the first conversion.
        Task.Run(() => {
            try {
                Document.Create(container => {
                    container.Page(page => {
                        page.Size(PageSizes.A4);
                        page.DefaultTextStyle(x => x.FontFamily(Fonts.CourierNew));
                        page.Content().Text("Warmup");
                    });
                }).GeneratePdf();
            }
            catch {
                // Silently ignore warmup errors
            }
        });
    }
}