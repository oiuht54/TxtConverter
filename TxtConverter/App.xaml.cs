using System.Windows;
using TxtConverter.Services;

namespace TxtConverter;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Инициализация сервисов
        PreferenceManager.Instance.Load();

        // Применение сохраненного языка
        var savedLang = PreferenceManager.Instance.GetLanguage();
        LanguageManager.Instance.SetLanguage(savedLang);
    }
}