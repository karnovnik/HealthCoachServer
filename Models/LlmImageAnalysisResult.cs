namespace HealthCoachServer.Models;
public class LlmImageAnalysisResult
{
    public bool Success { get; set; }
    public string AssistantText { get; set; }
    public string RawResponse { get; set; }
    public int? TotalTokens { get; set; }
    public string Error { get; set; }
}