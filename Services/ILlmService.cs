public record LlmResponse(string Content, int TotalTokens);

public interface ILlmService
{
    /// <summary>
    /// Send the final prompt (string) to LLM and get raw content.
    /// Implement according to your provider (chat messages vs single prompt).
    /// </summary>
    Task<LlmResponse> CallRawAsync(string prompt, CancellationToken ct = default);
}