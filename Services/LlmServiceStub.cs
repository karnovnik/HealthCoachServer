public class LlmServiceStub : ILlmService
{
    private readonly ILogger<LlmServiceStub> _log;

    public LlmServiceStub(ILogger<LlmServiceStub> log) => _log = log;

    public Task<LlmResponse> CallRawAsync(string prompt, CancellationToken ct = default)
    {
        _log.LogInformation("LlmServiceStub called (length={L})", prompt?.Length ?? 0);
        // For development, throw so you replace with real LLM quickly
        throw new NotImplementedException("Replace LlmServiceStub with a real LLM integration.");
    }
}