namespace TxtConverter.Services.Ai;

public class AiAnalysisResult {
    public List<string> SelectedFiles { get; set; } = new();
    
    // Debug info
    public string RequestJson { get; set; } = "";
    public string CleanRequestText { get; set; } = "";
    public string RawResponseJson { get; set; } = "";
    public string RawContentText { get; set; } = "";
    public string ProviderName { get; set; } = "";
}