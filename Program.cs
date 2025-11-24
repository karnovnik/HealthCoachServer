using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Ok(new { status = "ok" }));

// Simple API key middleware (read from env API_KEY)
app.Use(async (ctx, next) =>
{
    var configured = builder.Configuration["API_KEY"];
    if (string.IsNullOrEmpty(configured))
    {
        await next(); // no API key configured => open (dev)
        return;
    }
    var header = ctx.Request.Headers["X-Api-Key"].FirstOrDefault();
    if (!string.IsNullOrEmpty(header) && header == configured)
        await next();
    else
    {
        ctx.Response.StatusCode = 401;
        await ctx.Response.WriteAsync("Unauthorized");
    }
});

// POST /analyze : accept 1 file (multipart), return mock analysis
app.MapPost("/analyze", async (HttpRequest req) =>
{
    if (!req.HasFormContentType) return Results.BadRequest(new { error = "multipart/form-data required" });
    var form = await req.ReadFormAsync();
    var file = form.Files.FirstOrDefault();
    if (file == null || file.Length == 0) return Results.BadRequest(new { error = "file required" });

    // compute imageId as sha256 of bytes
    string imageId;
    await using (var ms = new MemoryStream())
    {
        await file.CopyToAsync(ms);
        ms.Position = 0;
        using var sha = SHA256.Create();
        imageId = Convert.ToHexString(sha.ComputeHash(ms)).ToLowerInvariant();
    }

    var response = new
    {
        imageId,
        dishName = "Курица с рисом (mock)",
        ingredients = new[]
        {
            new { name = "куриное филе", grams = 150, cal = 240, p = 40, f = 6, c = 0 },
            new { name = "рис (варёный)", grams = 120, cal = 160, p = 3, f = 1, c = 36 }
        },
        totals = new { cal = 400, p = 43, f = 7, c = 36 },
        confidence = 0.8
    };

    return Results.Ok(response);
});

// POST /feedback : accept JSON with analysis + comment, proxy to LLM later (mock = echo)
app.MapPost("/feedback", async ([FromBody] JsonElement body) =>
{
    // we don't store anything; just echo processed mock response for now
    // In production: build prompt from body and call LLM, then return LLM JSON
    return Results.Ok(new { ok = true, received = body });
});

app.Run();
