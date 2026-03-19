using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SysBot.Base;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon.WebAPI;

public static class WebApiServer
{
    private static IWebTradeHub? _hub;

    public static void Start(IWebTradeHub hub, int port = 5000, CancellationToken token = default)
    {
        _hub = hub;
        Task.Run(() => RunAsync(port, token), token);
    }

    private static async Task RunAsync(int port, CancellationToken token)
    {
        try
        {
            var opts = new WebApplicationOptions { Args = [$"--urls=http://localhost:{port}"] };
            var builder = WebApplication.CreateBuilder(opts);
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Services.AddCors(options =>
                options.AddDefaultPolicy(p =>
                    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

            var app = builder.Build();
            app.UseCors();

            // POST /api/validate-key
            app.MapPost("/api/validate-key", async (HttpRequest request) =>
            {
                string body;
                using (var reader = new StreamReader(request.Body))
                    body = await reader.ReadToEndAsync();

                JsonElement root;
                try { root = JsonDocument.Parse(body).RootElement; }
                catch { return Results.Json(new { valid = false }); }

                var key = root.TryGetProperty("key", out var k) ? k.GetString()?.Trim() : null;
                if (string.IsNullOrWhiteSpace(key))
                    return Results.Json(new { valid = false });

                var keysFile = Path.Combine(AppContext.BaseDirectory, "member-keys.txt");
                if (!File.Exists(keysFile))
                    return Results.Json(new { valid = false });

                var keys = await File.ReadAllLinesAsync(keysFile);
                var valid = keys.Any(l => l.Trim().Equals(key, StringComparison.OrdinalIgnoreCase));
                return Results.Json(new { valid });
            });

            // GET /api/catalog
            app.MapGet("/api/catalog", () =>
            {
                if (_hub is null) return Results.Json(Array.Empty<object>());
                return Results.Json(_hub.GetCatalog());
            });

            // GET /api/status
            app.MapGet("/api/status", () =>
            {
                if (_hub is null)
                    return Results.Json(new { online = false, canQueue = false, queueCount = 0, botCount = 0 });
                return Results.Json(new
                {
                    online = true,
                    canQueue = _hub.CanQueue,
                    queueCount = _hub.TradeQueueCount,
                    botCount = _hub.BotCount,
                    game = _hub.GameVersion,
                });
            });

            // POST /api/trade  { trainerName, showdownSet }
            app.MapPost("/api/trade", async (HttpRequest request) =>
            {
                if (_hub is null)
                    return Results.Json(new { success = false, message = "Bot server not ready." }, statusCode: 503);

                if (!_hub.CanQueue)
                    return Results.Json(new { success = false, message = "Queue is currently closed." }, statusCode: 503);

                string body;
                using (var reader = new StreamReader(request.Body))
                    body = await reader.ReadToEndAsync();

                JsonElement root;
                try { root = JsonDocument.Parse(body).RootElement; }
                catch { return Results.Json(new { success = false, message = "Invalid JSON." }, statusCode: 400); }

                var trainerName = root.TryGetProperty("trainerName", out var tn) ? tn.GetString()?.Trim() : null;
                if (string.IsNullOrWhiteSpace(trainerName))
                    trainerName = "WebUser";

                // Support both catalog file trades and custom showdown set trades
                var fileName = root.TryGetProperty("fileName", out var fn) ? fn.GetString() : null;
                var showdownSet = root.TryGetProperty("showdownSet", out var el) ? el.GetString() : null;

                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    var result2 = _hub.SubmitFileTrade(trainerName, fileName.Trim());
                    return Results.Json(new
                    {
                        success = result2.Success,
                        sessionId = result2.SessionId,
                        tradeCode = result2.TradeCode,
                        position = result2.Position,
                        message = result2.Message,
                    });
                }

                if (string.IsNullOrWhiteSpace(showdownSet))
                    return Results.Json(new { success = false, message = "Either fileName or showdownSet is required." }, statusCode: 400);

                var result = _hub.SubmitShowdownTrade(trainerName, showdownSet.Trim());
                return Results.Json(new
                {
                    success = result.Success,
                    sessionId = result.SessionId,
                    tradeCode = result.TradeCode,
                    position = result.Position,
                    message = result.Message,
                });
            });

            // GET /api/trade/{sessionId}
            app.MapGet("/api/trade/{sessionId}", (string sessionId) =>
            {
                if (_hub is null)
                    return Results.Json(new { status = "Error", message = "Bot server not ready.", position = 0, tradeCode = 0 });
                var s = _hub.GetStatus(sessionId);
                return Results.Json(new
                {
                    status = s.Status,
                    message = s.Message,
                    position = s.Position,
                    tradeCode = s.TradeCode,
                });
            });

            // DELETE /api/trade/{sessionId}
            app.MapDelete("/api/trade/{sessionId}", (string sessionId) =>
            {
                if (_hub is null)
                    return Results.Json(new { success = false, message = "Bot server not ready." });
                var ok = _hub.RemoveTrade(sessionId, out var msg);
                return Results.Json(new { success = ok, message = msg });
            });

            // GET /api/queue
            app.MapGet("/api/queue", () =>
            {
                if (_hub is null) return Results.Json(Array.Empty<string>());
                return Results.Json(_hub.GetQueueList());
            });

            LogUtil.LogInfo($"Web API available at http://localhost:{port}", "WebAPI");
            await app.StartAsync(token);
            await Task.Delay(Timeout.Infinite, token).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnCanceled);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, "WebAPI");
        }
    }
}
