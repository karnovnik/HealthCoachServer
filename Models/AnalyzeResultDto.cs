namespace HealthCoachServer.Models;
public class AnalyzeResultDto
{
    public string ImageId { get; set; }
    public string Status { get; set; }
    public object Result { get; set; }
    public string Error { get; set; }
    public string BlobPath { get; set; }
    public long? ReceivedBytes { get; set; }
    public long? LatencyMs { get; set; }
    public int? TotalTokens { get; set; }
}