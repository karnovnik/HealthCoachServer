using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HealthCoachServer.Models;


namespace HealthCoachServer.Services;

public class LlmService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _cfg;
    private readonly ILogger<LlmService> _log;


    public LlmService(IHttpClientFactory httpFactory, IConfiguration cfg, ILogger<LlmService> log)
    {
        _httpFactory = httpFactory;
        _cfg = cfg;
        _log = log;
    }


    public async Task<LlmImageAnalysisResult> AnalyzeImageAsync(byte[] imageBytes, string imageId, string hint,
        string[] extraParams, string userId)
    {
        var client = _httpFactory.CreateClient();
        var key = _cfg["OPENAI_API_KEY"];
        if (string.IsNullOrEmpty(key))
            return new LlmImageAnalysisResult { Success = false, Error = "OPENAI_API_KEY not configured" };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        client.Timeout = TimeSpan.FromSeconds(120);


        var model = _cfg["LLM_MODEL"] ?? "gpt-4o-mini-vision";
        var paramListText = (extraParams != null && extraParams.Length > 0)
            ? $"Also include these additional fields in totals and per-ingredient: {string.Join(", ", extraParams)}."
            : string.Empty;


        var systemMsg = "You are a professional nutrition analyst. Return STRICT JSON only (no prose).";
        var userMsg =
            $@"Image id: {imageId}. Hint: {Utilities.EscapeForPrompt(hint)}. {paramListText}\nTask: Return compact JSON with imageId, dishName, ingredients (name, grams, cal, p, f, c{(extraParams != null && extraParams.Length > 0 ? ", " + string.Join(", ", extraParams) : "")}), totals (cal,p,f,c{(extraParams != null && extraParams.Length > 0 ? ", " + string.Join(", ", extraParams) : "")}). If uncertain, set uncertain:true. Return only JSON.";


        var payload = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = systemMsg }, new { role = "user", content = userMsg },
                new { role = "user", content = $"IMAGE_BASE64:{Convert.ToBase64String(imageBytes)}" }
            },
            temperature = 0.0, max_tokens = 1000
        };
        var reqJson = JsonSerializer.Serialize(payload);


        HttpResponseMessage resp;
        try
        {
            resp = await client.PostAsync("https://api.openai.com/v1/chat/completions",
                new StringContent(reqJson, Encoding.UTF8, "application/json"));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LLM HTTP call failed");
            return new LlmImageAnalysisResult { Success = false, Error = ex.Message };
        }


        var respBody = await resp.Content.ReadAsStringAsync();


        int? totalTokens = null;
        try
        {
            using var doc = JsonDocument.Parse(respBody);
            if (doc.RootElement.TryGetProperty("usage", out var usage) &&
                usage.TryGetProperty("total_tokens", out var tt)) totalTokens = tt.GetInt32();
        }
        catch
        {
        }


        if (!resp.IsSuccessStatusCode)
            return new LlmImageAnalysisResult
            {
                Success = false, RawResponse = respBody, TotalTokens = totalTokens,
                Error = $"LLM returned {(int)resp.StatusCode}"
            };


        string assistantText = null;
        try
        {
            using var doc = JsonDocument.Parse(respBody);
            assistantText = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content")
                .GetString();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed parse assistant content");
            return new LlmImageAnalysisResult
            {
                Success = false, RawResponse = respBody, TotalTokens = totalTokens,
                Error = "Failed to parse assistant message"
            };
        }


        return new LlmImageAnalysisResult
            { Success = true, AssistantText = assistantText, RawResponse = respBody, TotalTokens = totalTokens };
    }
}