using System.Text.Json;
using HealthCoachServer.Services;

var builder = WebApplication.CreateBuilder(args);

// Register services
builder.Services.AddHttpClient("PromptLoader");
builder.Services.AddSingleton<PromptLoader>();
builder.Services.AddSingleton<ILlmService, LlmServiceStub>(); // replace stub with real implementation
builder.Services.AddSingleton<IPromptBuilder, PromptBuilder>();
builder.Services.AddLogging();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// var promptLoader = app.Services.GetRequiredService<PromptLoader>();
var llm = app.Services.GetRequiredService<ILlmService>();
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Program");

app.MapGet("/", () => Results.Ok(new { status = "ok" }));

app.MapPost("/analyze", async (HttpRequest request, PromptBuilder promptBuilder) =>
{
    var getPromptOperation = await promptBuilder.BuildAnalyzePromptAsync(request);
    if (getPromptOperation.IsCancelled)
    {
        return Results.BadRequest(new { error = getPromptOperation.Error });
    }

    var prompt = getPromptOperation.Result;
    logger.LogInformation("Calling LLM for /analyze prompt = {Prompt}.", prompt);

    LlmResponse llmResp;
    try
    {
        llmResp = await llm.CallRawAsync(prompt, CancellationToken.None);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "LLM call failed");
        return Results.StatusCode(502);
    }

    try
    {
        using var doc = JsonDocument.Parse(llmResp.Content);
        var json = JsonSerializer.Deserialize<JsonElement>(llmResp.Content);
        var response = new
        {
            requestId = Guid.NewGuid().ToString(),
            analysis = json,
            // promptHash = loaded.Hash, TODO probably add it
            totalTokens = llmResp.TotalTokens
        };
        return Results.Ok(response);
    }
    catch (JsonException jex)
    {
        logger.LogWarning(jex, "LLM returned non-JSON for /analyze");
        return Results.BadRequest(new { error = "LLM returned invalid JSON", raw = llmResp.Content });
    }
});

app.MapPost("/correction", async (HttpRequest request, PromptBuilder promptBuilder) =>
{
    var getPromptOperation = await promptBuilder.BuildCorrectionPromptAsync(request);
    if (getPromptOperation.IsCancelled)
    {
        return Results.BadRequest(new { error = getPromptOperation.Error });
    }

    var prompt = getPromptOperation.Result;
    logger.LogInformation("Calling LLM for /correction prompt = {Prompt}.", prompt);

    LlmResponse llmResp;
    try
    {
        llmResp = await llm.CallRawAsync(prompt, CancellationToken.None);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "LLM call failed (correction)");
        return Results.StatusCode(502);
    }

    try
    {
        using var doc = JsonDocument.Parse(llmResp.Content);
        var json = JsonSerializer.Deserialize<JsonElement>(llmResp.Content);
        var response = new
        {
            // requestId = body.RequestId,
            correctedAnalysis = json.GetProperty("correctedAnalysis"),
            needsReanalysis = json.TryGetProperty("needsReanalysis", out var nr) ? nr.GetBoolean() : false,
            reanalysisReason = json.TryGetProperty("reanalysisReason", out var rr) ? rr.GetString() : null,
            confidenceNote = json.TryGetProperty("confidenceNote", out var cn) ? cn.GetString() : null,
            // promptHash = loaded.Hash,
            totalTokens = llmResp.TotalTokens
        };
        return Results.Ok(response);
    }
    catch (JsonException jex)
    {
        logger.LogWarning(jex, "LLM returned non-JSON for /correction");
        return Results.BadRequest(new { error = "LLM returned invalid JSON", raw = llmResp.Content });
    }
});

app.Run();