using System.IO.Packaging;
using System.Windows;
using TxtConverter.Core;

namespace TxtConverter.Services;

public class LanguageManager
{
    private static LanguageManager? _instance;
    public static LanguageManager Instance => _instance ??= new LanguageManager();

    /// <summary>
    /// Dictionary used when no WPF Application instance exists (headless CLI mode).
    /// Pack URIs are resolved relative to this assembly when ResourceAssembly is set manually.
    /// </summary>
    private ResourceDictionary? _headlessDictionary;

    private LanguageManager() { }

    public string CurrentLanguage { get; private set; } = ProjectConstants.LangEn;

    /// <summary>
    /// Loads the localized strings for the given language.
    /// </summary>
    /// <param name="languageCode">Language code (en, ru).</param>
    /// <param name="persist">When false the choice is applied for the current session only (used by the CLI).</param>
    public void SetLanguage(string languageCode, bool persist = true)
    {
        var uri = BuildLanguageUri(languageCode);

        try
        {
            var dict = new ResourceDictionary { Source = uri };

            if (Application.Current != null)
            {
                Application.Current.Resources.MergedDictionaries.Clear();
                Application.Current.Resources.MergedDictionaries.Add(dict);

                // Re-add Styles because clearing removes them too!
                var styles = new ResourceDictionary { Source = new Uri("pack://application:,,,/Resources/Styles.xaml") };
                Application.Current.Resources.MergedDictionaries.Add(styles);
            }
            else
            {
                _headlessDictionary = dict;
            }

            CurrentLanguage = languageCode;
            if (persist) PreferenceManager.Instance.SetLanguage(languageCode);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading language {languageCode}: {ex.Message}");
        }
    }

    /// <summary>
    /// Pack URIs need an explicit resource assembly when running without an Application instance,
    /// and the "pack" URI scheme has to be registered before the URI can be parsed at all
    /// (normally done by WPF while the GUI starts).
    /// </summary>
    private static Uri BuildLanguageUri(string languageCode) {
        try {
            // WPF registers this scheme while the GUI starts; the CLI has to do it itself.
            if (!UriParser.IsKnownScheme(PackUriHelper.UriSchemePack))
                UriParser.Register(new GenericUriParser(GenericUriParserOptions.GenericAuthority), PackUriHelper.UriSchemePack, -1);

            if (Application.ResourceAssembly == null)
                Application.ResourceAssembly = typeof(LanguageManager).Assembly;
        }
        catch {
            // Already initialized by another code path - nothing to do.
        }

        return new Uri($"pack://application:,,,/Resources/Lang/Strings.{languageCode}.xaml", UriKind.Absolute);
    }

    public string GetString(string key)
    {
        if (Application.Current != null && Application.Current.Resources.Contains(key))
        {
            return Application.Current.Resources[key] as string ?? $"!{key}!";
        }

        if (_headlessDictionary != null && _headlessDictionary.Contains(key))
        {
            return _headlessDictionary[key] as string ?? $"!{key}!";
        }

        return $"!{key}!";
    }
}