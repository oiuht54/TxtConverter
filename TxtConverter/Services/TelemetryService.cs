using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Generic;
using System.Threading.Tasks;
using TxtConverter.Core; // Secrets

namespace TxtConverter.Services;

public class TelemetryService {
    private static TelemetryService? _instance;
    public static TelemetryService Instance => _instance ??= new TelemetryService();
    
    private readonly HttpClient _httpClient;
    private readonly string _gaEndpoint;
    private readonly string _osVersion;
    private readonly bool _isConfigured;
    private readonly long _sessionId;
    private bool _isSessionStartSent = false;
    private readonly object _lock = new object();

    private TelemetryService() {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(5);
        _osVersion = Environment.OSVersion.ToString();
        _sessionId = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
        string mId = Secrets.GaMeasurementId;
        string secret = Secrets.GaApiSecret;
        
        if (string.IsNullOrWhiteSpace(mId) || mId.Contains("CHANGE_ME") ||
            string.IsNullOrWhiteSpace(secret) || secret.Contains("CHANGE_ME")) {
            _isConfigured = false;
            _gaEndpoint = "";
        }
        else {
            _isConfigured = true;
            _gaEndpoint = $"https://www.google-analytics.com/mp/collect?measurement_id={mId}&api_secret={secret}";
        }
    }

    public void TrackEvent(string eventName, Dictionary<string, object>? parameters = null) {
        if (!_isConfigured) return;
        if (!PreferenceManager.Instance.GetTelemetryEnabled()) return;
        
        string clientId = PreferenceManager.Instance.GetInstallationId();
        string language = PreferenceManager.Instance.GetLanguage();
        
        _ = Task.Run(async () => {
            try {
                var payload = new JsonObject();
                payload["client_id"] = clientId;
                
                var userProps = new JsonObject();
                userProps["app_language"] = new JsonObject { ["value"] = language };
                userProps["os_version"] = new JsonObject { ["value"] = _osVersion };
                payload["user_properties"] = userProps;
                
                var eventsArr = new JsonArray();
                bool sendSessionStart = false;
                
                lock (_lock) {
                    if (!_isSessionStartSent) {
                        _isSessionStartSent = true;
                        sendSessionStart = true;
                    }
                }
                
                // Внедряем атомарное событие session_start для правильной склейки визитов и атрибуции в GA4
                if (sendSessionStart) {
                    var sessionStartObj = new JsonObject();
                    sessionStartObj["name"] = "session_start";
                    
                    var ssParams = new JsonObject();
                    ssParams["session_id"] = _sessionId;
                    ssParams["engagement_time_msec"] = 100;
                    
                    // Системные параметры атрибуции (избегаем Unassigned за счет использования referral)
                    ssParams["campaign"] = "TxtConverter_Desktop";
                    ssParams["source"] = "TxtConverter_App";
                    ssParams["medium"] = "referral";
                    
                    sessionStartObj["params"] = ssParams;
                    eventsArr.Add(sessionStartObj);
                }
                
                var eventObj = new JsonObject();
                eventObj["name"] = eventName;
                
                var paramsObj = new JsonObject();
                paramsObj["session_id"] = _sessionId;
                paramsObj["engagement_time_msec"] = 100;
                
                // Дублируем параметры атрибуции в основное событие для консистентной связи
                paramsObj["campaign"] = "TxtConverter_Desktop";
                paramsObj["source"] = "TxtConverter_App";
                paramsObj["medium"] = "referral";
                
                if (parameters != null) {
                    foreach (var kvp in parameters) {
                        if (kvp.Value is int i) paramsObj[kvp.Key] = i;
                        else if (kvp.Value is long l) paramsObj[kvp.Key] = l;
                        else if (kvp.Value is double d) paramsObj[kvp.Key] = d;
                        else if (kvp.Value is float f) paramsObj[kvp.Key] = f;
                        else if (kvp.Value is bool b) paramsObj[kvp.Key] = b;
                        else paramsObj[kvp.Key] = kvp.Value?.ToString() ?? "";
                    }
                }
                
                eventObj["params"] = paramsObj;
                eventsArr.Add(eventObj);
                
                payload["events"] = eventsArr;
                
                string jsonString = payload.ToJsonString();
                var content = new StringContent(jsonString, Encoding.UTF8, "application/json");
                await _httpClient.PostAsync(_gaEndpoint, content);
            }
            catch {
                // Ошибки отправки телеметрии должны гаситься, чтобы не влиять на работу основного GUI
            }
        });
    }
}