using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TxtConverter.Core; // Здесь лежит класс Secrets

namespace TxtConverter.Services;

public class TelemetryService
{
    private static TelemetryService? _instance;
    public static TelemetryService Instance => _instance ??= new TelemetryService();

    private readonly HttpClient _httpClient;
    private readonly string _gaEndpoint;
    private readonly string _osVersion;
    private readonly bool _isConfigured;

    private TelemetryService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(5);
        _osVersion = Environment.OSVersion.ToString();

        // Проверяем, прописал ли разработчик ключи в Secrets.cs
        // Если там остались заглушки или пустота - отключаем телеметрию
        string mId = Secrets.GaMeasurementId;
        string secret = Secrets.GaApiSecret;

        if (string.IsNullOrWhiteSpace(mId) || mId.Contains("CHANGE_ME") ||
            string.IsNullOrWhiteSpace(secret) || secret.Contains("CHANGE_ME"))
        {
            _isConfigured = false;
            _gaEndpoint = "";
        }
        else
        {
            _isConfigured = true;
            _gaEndpoint = $"https://www.google-analytics.com/mp/collect?measurement_id={mId}&api_secret={secret}";
        }
    }

    public void TrackEvent(string eventName, Dictionary<string, object>? parameters = null)
    {
        if (!_isConfigured) return; // Нет ключей - выходим
        if (!PreferenceManager.Instance.GetTelemetryEnabled()) return; // Пользователь запретил - выходим

        // ... далее код без изменений ...
        string clientId = PreferenceManager.Instance.GetInstallationId();
        string language = PreferenceManager.Instance.GetLanguage();

        Task.Run(async () => {
            try
            {
                var payload = new JsonObject();
                payload["client_id"] = clientId;

                var userProps = new JsonObject();
                userProps["app_language"] = new JsonObject { ["value"] = language };
                userProps["os_version"] = new JsonObject { ["value"] = _osVersion };
                payload["user_properties"] = userProps;

                var eventObj = new JsonObject();
                eventObj["name"] = eventName;

                var paramsObj = new JsonObject();
                paramsObj["engagement_time_msec"] = 100;
                paramsObj["session_id"] = clientId;

                if (parameters != null)
                {
                    foreach (var kvp in parameters)
                    {
                        if (kvp.Value is int || kvp.Value is long || kvp.Value is double || kvp.Value is float)
                        {
                            paramsObj[kvp.Key] = JsonValue.Create(kvp.Value);
                        }
                        else
                        {
                            paramsObj[kvp.Key] = kvp.Value.ToString();
                        }
                    }
                }
                eventObj["params"] = paramsObj;

                var eventsArr = new JsonArray();
                eventsArr.Add(eventObj);
                payload["events"] = eventsArr;

                string jsonString = payload.ToJsonString();
                var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

                await _httpClient.PostAsync(_gaEndpoint, content);
            }
            catch { /* Ignore */ }
        });
    }
}