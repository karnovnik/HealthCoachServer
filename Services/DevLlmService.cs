public class DevLlmService : ILlmService
{
    public Task<LlmResponse> CallRawAsync(string prompt, CancellationToken ct = default)
    {
        // Very small canned JSON (for /analyze)
        var sample = @"{
          ""imageId"": ""abc123"",
          ""dishName"": ""Sample dish"",
          ""ingredients"": [
            {""name"":""rice"",""grams"":150,""cal"":200,""p"":4.0,""f"":1.0,""c"":45.0,""confidence"":0.9}
          ],
          ""totals"": {""cal"":200,""p"":4.0,""f"":1.0,""c"":45.0,""confidence"":0.9},
          ""overallConfidence"": 0.9
        }";
        return Task.FromResult(new LlmResponse(sample, 10));
    }
}