using HealthCoachServer.Infra;

namespace HealthCoachServer.Services;

public interface IPromptBuilder
{
    Task<OperationResult<string>> BuildAnalyzePromptAsync(HttpRequest request);
    Task<OperationResult<string>> BuildCorrectionPromptAsync(HttpRequest request);
}