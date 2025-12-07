using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HealthCoachServer.Infra;

namespace HealthCoachServer.Services;

public class PromptBuilder : IPromptBuilder
{
    private readonly PromptLoader _promptLoader;
    private readonly ILogger<PromptBuilder> _log;

    public PromptBuilder(PromptLoader promptLoader, ILogger<PromptBuilder> log)
    {
        _promptLoader = promptLoader;
        _log = log;
    }

    public async Task<OperationResult<string>> BuildAnalyzePromptAsync(HttpRequest request)
    {
        if (!request.HasFormContentType)
        {
            return OperationResult<string>.CreateCancelled("multipart/form-data required.");
        }

        var form = await request.ReadFormAsync();
        var files = form.Files;
        if (files.Count == 0)
        {
            return OperationResult<string>.CreateCancelled("There aren't any files in attachment.");
        }

        var comment = form["comment"].FirstOrDefault() ?? "";
        var hint = form["hint"].FirstOrDefault() ?? "";
        var extraParamsRaw = form["extraParams"].FirstOrDefault() ?? "";
        var extraParams = string.IsNullOrWhiteSpace(extraParamsRaw)
            ? []
            : extraParamsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var images = new List<byte[]>();
        foreach (var file in files)
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            images.Add(ms.ToArray());
        }

        var imageId = Guid.NewGuid().ToString();
        var loaded = _promptLoader.GetPrompt("analyze");
        var template = loaded.Text;

        var paramListText = extraParams.Length > 0
            ? $"Also include these additional fields in totals and per-ingredient: {string.Join(", ", extraParams)}."
            : string.Empty;

        var rendered = RenderTemplate(template, new Dictionary<string, string>
        {
            ["imageId"] = imageId,
            ["hint"] = Utilities.EscapeForPrompt(hint ?? ""),
            ["comment"] = Utilities.EscapeForPrompt(comment ?? ""),
            ["extraParamsText"] = paramListText
        });

        var sb = new StringBuilder();
        sb.AppendLine(rendered);
        var idx = 0;
        foreach (var img in images)
        {
            var base64 = Convert.ToBase64String(img);
            sb.AppendLine($"---IMAGE_{idx}_START---");
            sb.AppendLine($"IMAGE_BASE64:{base64}");
            sb.AppendLine($"---IMAGE_{idx}_END---");
            idx++;
        }

        var final = sb.ToString();
        _log.LogDebug("Built analyze prompt (imageId={ImageId}, images={Count}, promptHash={Hash})",
            imageId, idx, ComputeSha256(final));
        return OperationResult<string>.CreateCompleted(final);
    }

    public async Task<OperationResult<string>> BuildCorrectionPromptAsync(HttpRequest request)
    {
        // Expecting JSON body like:
        // {
        //   "requestId": "uuid",
        //   "userId": "user-123",
        //   "userComment": "string",
        //   "previousAnalysis": { ... } 
        // }

        // Read body
        string body;
        using (var sr = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true))
        {
            body = await sr.ReadToEndAsync();
            // rewind stream so upstream can still read if necessary
            try
            {
                request.Body.Position = 0;
            }
            catch
            {
                /* ignore if not seekable */
            }
        }

        if (string.IsNullOrWhiteSpace(body))
            return OperationResult<string>.CreateCancelled("Request body is empty.");

        JsonElement previousAnalysisElement;
        string userComment = "";

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("previousAnalysis", out var prev))
            {
                previousAnalysisElement = prev;
            }
            else
            {
                return OperationResult<string>.CreateCancelled("Missing required field: previousAnalysis.");
            }

            if (root.TryGetProperty("userComment", out var uc) && uc.ValueKind != JsonValueKind.Null)
                userComment = uc.GetString() ?? "";
            else if (root.TryGetProperty("usercomment", out var uc2) &&
                     uc2.ValueKind != JsonValueKind.Null) // tolerate lowercase
                userComment = uc2.GetString() ?? "";
            else
                userComment = "";
        }
        catch (JsonException jex)
        {
            _log.LogWarning(jex, "Failed to parse correction request body");
            return OperationResult<string>.CreateCancelled("Invalid JSON in request body.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unexpected error while reading correction request");
            return OperationResult<string>.CreateCancelled("Failed to read request body.");
        }

        string prevJson;
        try
        {
            prevJson = JsonSerializer.Serialize(previousAnalysisElement,
                new JsonSerializerOptions { WriteIndented = false });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to serialize previousAnalysis for correction prompt; using empty object");
            prevJson = "{}";
        }

        // Escape and prepare
        var safePrev = EscapeForPrompt(prevJson);
        var safeComment = Utilities.EscapeForPrompt(userComment ?? "");

        // Load correction template
        LoadedPrompt loaded;
        try
        {
            loaded = _promptLoader.GetPrompt("correction");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load correction prompt template");
            return OperationResult<string>.CreateCancelled("Correction prompt template not found.");
        }

        var template = loaded.Text;

        var rendered = RenderTemplate(template, new Dictionary<string, string>
        {
            ["previousAnalysis"] = safePrev,
            ["comment"] = safeComment
        });

        _log.LogDebug("Built correction prompt (prevLen={Len}, promptHash={Hash})", safePrev.Length,
            ComputeSha256(rendered));
        return OperationResult<string>.CreateCompleted(rendered);
    }

    // Simple template renderer: replaces {{key}} with value.
    // NOTE: this is intentionally minimal (no loops/conditionals). Use a templating lib if needed.
    private static string RenderTemplate(string template, Dictionary<string, string> values)
    {
        var r = template;
        foreach (var kv in values)
        {
            r = r.Replace("{{" + kv.Key + "}}", kv.Value ?? "");
        }

        return r;
    }

    // Clean string from control characters and collapse whitespace
    private static string EscapeForPrompt(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch == '\r' || ch == '\n' || ch == '\t')
                sb.Append(' ');
            else
                sb.Append(ch);
        }
        // collapse multiple spaces
        var cleaned = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        return cleaned;
    }

    private static string ComputeSha256(string s)
    {
        using var sha = SHA256.Create();
        var b = Encoding.UTF8.GetBytes(s);
        return Convert.ToHexString(sha.ComputeHash(b)).ToLowerInvariant();
    }
}
