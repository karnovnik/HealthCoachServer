using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public class PromptLoaderOptions
{
    public bool AutoRefreshFromGitHub { get; set; } = false;
    public int RefreshIntervalSeconds { get; set; } = 300;
    public Dictionary<string, PromptSource> PromptSets { get; set; } = new();
}

public class PromptSource
{
    public string LocalPath { get; set; }
    public string GithubRawUrl { get; set; }
}

public class LoadedPrompt
{
    public string Text { get; set; }
    public string Hash { get; set; }
    public DateTime FetchedAtUtc { get; set; }
    public string Source { get; set; } // "github" or "local"
}

public class PromptLoader : IDisposable
{
    private readonly ILogger<PromptLoader> _log;
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _baseDir;
    private readonly PromptLoaderOptions _opts;
    private readonly Dictionary<string, LoadedPrompt> _cache = new();
    private Timer _timer;

    public PromptLoader(IConfiguration cfg, IHttpClientFactory httpFactory, ILogger<PromptLoader> log)
    {
        _log = log;
        _httpFactory = httpFactory;
        _baseDir = AppContext.BaseDirectory;

        // load prompts.json
        var configPath = Path.Combine(_baseDir, "prompts.json");
        if (!File.Exists(configPath))
            throw new FileNotFoundException("prompts.json missing", configPath);

        var json = File.ReadAllText(configPath);
        _opts = JsonSerializer.Deserialize<PromptLoaderOptions>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new PromptLoaderOptions();

        // initial load
        foreach (var kv in _opts.PromptSets)
        {
            try { LoadPromptAsync(kv.Key).GetAwaiter().GetResult(); }
            catch (Exception ex) { _log.LogWarning(ex, "Initial load failed for prompt {Key}", kv.Key); }
        }

        if (_opts.AutoRefreshFromGitHub && _opts.RefreshIntervalSeconds > 0)
        {
            _timer = new Timer(async _ => await RefreshAllAsync(), null, _opts.RefreshIntervalSeconds * 1000, _opts.RefreshIntervalSeconds * 1000);
            _log.LogInformation("PromptLoader auto-refresh enabled, interval {s}s", _opts.RefreshIntervalSeconds);
        }
    }

    public LoadedPrompt GetPrompt(string key)
    {
        if (_cache.TryGetValue(key, out var p)) return p;
        throw new KeyNotFoundException($"Prompt set not found: {key}");
    }

    private async Task RefreshAllAsync()
    {
        foreach (var kv in _opts.PromptSets)
        {
            try { await LoadPromptAsync(kv.Key); }
            catch (Exception ex) { _log.LogWarning(ex, "Failed refresh prompt {Key}", kv.Key); }
        }
    }

    public async Task LoadPromptAsync(string key)
    {
        if (!_opts.PromptSets.TryGetValue(key, out var src)) throw new KeyNotFoundException(key);
        string text = null;
        string source = null;

        // try GitHub raw first if configured
        if (!string.IsNullOrEmpty(src.GithubRawUrl))
        {
            try
            {
                var client = _httpFactory.CreateClient("PromptLoader");
                // Optionally set GitHub token from env var: GITHUB_RAW_TOKEN
                var token = Environment.GetEnvironmentVariable("GITHUB_RAW_TOKEN");
                if (!string.IsNullOrEmpty(token))
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var resp = await client.GetAsync(src.GithubRawUrl);
                if (resp.IsSuccessStatusCode)
                {
                    text = await resp.Content.ReadAsStringAsync();
                    source = "github";
                    _log.LogInformation("Loaded prompt {Key} from GitHub {Url}", key, src.GithubRawUrl);
                }
                else
                {
                    _log.LogWarning("GitHub raw request for {Key} returned {Status}", key, resp.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed load prompt from GitHub for {Key}", key);
            }
        }

        // fallback to local file
        if (text == null && !string.IsNullOrEmpty(src.LocalPath))
        {
            var local = Path.Combine(_baseDir, src.LocalPath);
            if (File.Exists(local))
            {
                text = File.ReadAllText(local);
                source = "local";
                _log.LogInformation("Loaded prompt {Key} from local file {Path}", key, local);
            }
            else
            {
                _log.LogWarning("Local prompt file not found: {Path}", local);
            }
        }

        if (text == null) throw new Exception($"Prompt not found for key {key}");

        var hash = ComputeSha256(text);
        _cache[key] = new LoadedPrompt { Text = text, Hash = hash, FetchedAtUtc = DateTime.UtcNow, Source = source };
        _log.LogInformation("Prompt {Key} loaded (hash={Hash}, src={Src})", key, hash, source);
    }

    private static string ComputeSha256(string s)
    {
        using var sha = SHA256.Create();
        var b = Encoding.UTF8.GetBytes(s);
        return Convert.ToHexString(sha.ComputeHash(b)).ToLowerInvariant();
    }

    public void Dispose() => _timer?.Dispose();
}
