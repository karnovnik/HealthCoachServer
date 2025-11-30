using System.Diagnostics;
using HealthCoachServer;
using HealthCoachServer.Services;
using HealthCoachServer.Models;


var builder = WebApplication.CreateBuilder(args);


// Configuration and services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<LlmService>();
builder.Services.AddSingleton<BlobLogger>();


var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();


// Simple API key middleware
app.Use(async (context, next) =>
{
    var configured = builder.Configuration["API_KEY"];
    if (string.IsNullOrEmpty(configured))
    {
        await next();
        return;
    }

    var header = context.Request.Headers["X-Api-Key"].FirstOrDefault();
    if (!string.IsNullOrEmpty(header) && header == configured)
        await next();
    else
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsync("Unauthorized");
    }
});


// Configurable limits
var maxFiles = int.TryParse(builder.Configuration["MAX_FILES_PER_REQUEST"], out var mf) ? mf : 5;
var maxBytes = int.TryParse(builder.Configuration["MAX_BYTES_PER_FILE"], out var mb) ? mb : 700 * 1024;
var maxParallel = int.TryParse(builder.Configuration["MAX_CONCURRENT_LLM_CALLS"], out var mp) ? mp : 3;


app.MapGet("/", () => Results.Ok(new { status = "ok" }));

app.MapPost("/analyze", async (HttpRequest req, LlmService llm, BlobLogger blobLogger, ILogger<Program> log) =>
{
    if (!req.HasFormContentType) return Results.BadRequest(new { error = "multipart/form-data required" });
    var form = await req.ReadFormAsync();
    var files = form.Files;
    if (files == null || files.Count == 0) return Results.BadRequest(new { error = "no files" });
    if (files.Count > maxFiles) return Results.BadRequest(new { error = $"max {maxFiles} files" });


    var userIdRaw = form["userId"].FirstOrDefault() ?? "anon";
    var userId = Utilities.HashIfNeeded(userIdRaw);
    var hint = form["hint"].FirstOrDefault() ?? string.Empty;
    var extraParams = Utilities.ParseExtraParams(form["extraParams"].FirstOrDefault() ?? string.Empty);


    var requestId = Guid.NewGuid().ToString();
    var semaphore = new SemaphoreSlim(maxParallel);
    var tasks = new List<Task<AnalyzeResultDto>>();


    foreach (var file in files)
    {
        tasks.Add(Task.Run(async () =>
        {
            var dto = new AnalyzeResultDto();
            if (file == null || file.Length == 0)
            {
                dto.ImageId = null;
                dto.Status = "error";
                dto.Error = "empty file";
                return dto;
            }

            if (file.Length > maxBytes)
            {
                dto.ImageId = null;
                dto.Status = "error";
                dto.Error = $"file too large (max {maxBytes} bytes)";
                dto.ReceivedBytes = file.Length;
                return dto;
            }


// Read bytes and compute imageId
            byte[] bytes;
            string imageId;
            await using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                bytes = ms.ToArray();
                ms.Position = 0;
                using var sha = System.Security.Cryptography.SHA256.Create();
                imageId = Convert.ToHexString(sha.ComputeHash(ms)).ToLowerInvariant();
            }


            await semaphore.WaitAsync();
            try
            {
                var sw = Stopwatch.StartNew();
                var analysis = await llm.AnalyzeImageAsync(bytes, imageId, hint, extraParams, userId);
                sw.Stop();


// persist raw response if present
                string blobPath = null;
                if (!string.IsNullOrEmpty(analysis.RawResponse) && blobLogger.IsConfigured)
                {
                    try
                    {
                        blobPath = await blobLogger.SaveRawAsync(requestId, userId, imageId, bytes.Length,
                            analysis.RawResponse);
                    }
                    catch (Exception ex)
                    {
                        log.LogWarning(ex, "blob save failed");
                    }
                }


                if (analysis.Success)
                {
                    dto.ImageId = imageId;
                    dto.Status = "ok";
                    dto.Result = Utilities.TryParseJsonOrString(analysis.AssistantText);
                    dto.BlobPath = blobPath;
                    dto.LatencyMs = sw.ElapsedMilliseconds;
                    dto.TotalTokens = analysis.TotalTokens;
                }
                else
                {
                    dto.ImageId = imageId;
                    dto.Status = "error";
                    dto.Error = analysis.Error ?? "llm error";
                    dto.BlobPath = blobPath;
                    dto.LatencyMs = sw.ElapsedMilliseconds;
                }


                log.LogInformation(
                    "analyze: req={Req} user={User} image={Image} status={Status} ms={Ms} tokens={Tokens} blob={Blob}",
                    requestId, userId, imageId, dto.Status, dto.LatencyMs, dto.TotalTokens ?? -1, blobPath);
                return dto;
            }
            finally
            {
                semaphore.Release();
            }
        }));
    }


    var results = await Task.WhenAll(tasks);
    return Results.Ok(new { requestId, results });
});


app.Run();