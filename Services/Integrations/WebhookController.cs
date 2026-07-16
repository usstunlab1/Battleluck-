using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// HTTP webhook endpoint for external system integration.
/// Runs HttpListener on a configurable port with HMAC-SHA256 signature verification,
/// IP whitelist, and rate limiting. Dispatches actions to the main thread via a queue.
/// </summary>
public sealed class WebhookController : IDisposable
{
    HttpListener? _listener;
    WebhookConfig? _config;
    bool _running;
    byte[]? _secretKey;
    string? _expectedBasicAuthHeader;
    readonly ConcurrentQueue<Action> _mainThreadQueue = new();
    readonly ConcurrentDictionary<string, int> _rateLimits = new();
    DateTime _lastRateLimitReset = DateTime.UtcNow;
    readonly HttpClient _discordHttp = new();

    public void Configure(WebhookConfig? config)
    {
        if (config == null || !config.Enabled)
        {
            _config = null;
            return;
        }
        _config = config;
        _secretKey = string.IsNullOrWhiteSpace(config.Secret) ? null : Encoding.UTF8.GetBytes(config.Secret);

        if (!string.IsNullOrWhiteSpace(config.User) || !string.IsNullOrWhiteSpace(config.Password))
        {
            var plain = $"{config.User}:{config.Password}";
            _expectedBasicAuthHeader = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
        }
        else
        {
            _expectedBasicAuthHeader = null;
        }
    }

    public void Start()
    {
        if (_config == null || _running) return;

        _running = true;

        if (!_config.Enabled)
        {
            BattleLuckPlugin.LogInfo("[Webhook] Inbound endpoint disabled.");
            return;
        }

        try
        {
            _listener = new HttpListener();
            var bindAddress = NormalizeHttpListenerBindAddress(_config.BindAddress);
            _listener.Prefixes.Add($"http://{bindAddress}:{_config.Port}/webhook/");
            _listener.Start();

            Task.Run(ListenLoop);
            BattleLuckPlugin.LogInfo($"[Webhook] Listening on {bindAddress}:{_config.Port}");
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[Webhook] Failed to start: {ex.Message}");
            _running = false;
        }
    }

    static string NormalizeHttpListenerBindAddress(string? bindAddress)
    {
        if (string.IsNullOrWhiteSpace(bindAddress))
            return "+";

        var trimmed = bindAddress.Trim();
        return trimmed is "0.0.0.0" or "*" ? "+" : trimmed;
    }

    /// <summary>Call from main game thread each tick to drain queued actions.</summary>
    public void DrainMainThreadQueue()
    {
        int processed = 0;
        while (processed < 20 && _mainThreadQueue.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogWarning($"[Webhook] Main-thread action failed: {ex.Message}");
            }
            processed++;
        }
    }

    async Task ListenLoop()
    {
        while (_running && _listener != null)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(ctx));
            }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogWarning($"[Webhook] Listen error: {ex.Message}");
            }
        }
    }

    void HandleRequest(HttpListenerContext ctx)
    {
        try
        {
            var req = ctx.Request;
            var resp = ctx.Response;

            // IP whitelist check
            var remoteIp = req.RemoteEndPoint?.Address?.ToString() ?? "";
            if (_config != null && _config.IpWhitelist.Count > 0)
            {
                // Reject wildcard "*" for security - require explicit IP addresses
                if (_config.IpWhitelist.Contains("*"))
                {
                    BattleLuckPlugin.LogWarning("[Webhook] IP whitelist contains wildcard '*' - rejecting for security");
                    SendResponse(resp, 403, new WebhookResponse { Success = false, Message = "Wildcard IP not allowed" });
                    return;
                }

                if (!_config.IpWhitelist.Contains(remoteIp))
                {
                    SendResponse(resp, 403, new WebhookResponse { Success = false, Message = "Forbidden" });
                    return;
                }
            }

            // Rate limit check
            ResetRateLimitsIfNeeded();
            var count = _rateLimits.AddOrUpdate(remoteIp, 1, (_, c) => c + 1);
            if (_config != null && count > _config.RateLimitPerSecond)
            {
                SendResponse(resp, 429, new WebhookResponse { Success = false, Message = "Rate limited" });
                return;
            }

            // POST only
            if (req.HttpMethod != "POST")
            {
                SendResponse(resp, 405, new WebhookResponse { Success = false, Message = "POST only" });
                return;
            }

            // Optional Basic auth check
            if (!VerifyBasicAuth(req))
            {
                resp.Headers["WWW-Authenticate"] = "Basic realm=\"BattleLuckWebhook\"";
                SendResponse(resp, 401, new WebhookResponse { Success = false, Message = "Unauthorized" });
                return;
            }

            // Read body
            string body;
            using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
                body = reader.ReadToEnd();

            // HMAC-SHA256 verification (skip if no secret configured)
            var signature = req.Headers["X-Signature-256"];
            if (_secretKey != null && !VerifySignature(body, signature))
            {
                SendResponse(resp, 401, new WebhookResponse { Success = false, Message = "Invalid signature" });
                return;
            }

            // Try MuleRouter event format first
            if (TryHandleMuleRouterEvent(body))
            {
                SendResponse(resp, 200, new WebhookResponse { Success = true, Message = "Forwarded" });
                return;
            }

            // Parse BattleLuck action request
            var webhookReq = JsonSerializer.Deserialize<WebhookRequest>(body);
            if (webhookReq == null || string.IsNullOrWhiteSpace(webhookReq.Action))
            {
                SendResponse(resp, 400, new WebhookResponse { Success = false, Message = "Invalid request" });
                return;
            }

            // Enqueue action for main thread
            _mainThreadQueue.Enqueue(() => DispatchAction(webhookReq));

            SendResponse(resp, 200, new WebhookResponse { Success = true, Message = "Queued" });
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[Webhook] Request error: {ex.Message}");
            try
            {
                SendResponse(ctx.Response, 500,
                    new WebhookResponse { Success = false, Message = "Internal error" });
            }
            catch { /* response already sent */ }
        }
    }

    bool VerifySignature(string body, string? signature)
    {
        if (_secretKey == null || string.IsNullOrWhiteSpace(signature)) return false;

        using var hmac = new HMACSHA256(_secretKey);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        var expected = "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
        return string.Equals(expected, signature, StringComparison.OrdinalIgnoreCase);
    }

    bool TryHandleMuleRouterEvent(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("event", out var eventProp)) return false;

            var eventType = eventProp.GetString() ?? "";
            var (emoji, title) = eventType switch
            {
                "task.created"   => ("🕐", "Task Created"),
                "task.succeeded" => ("✅", "Task Succeeded"),
                "task.failed"    => ("❌", "Task Failed"),
                "balance.low"    => ("⚠️", "Balance Low"),
                _                => ("📡", eventType)
            };

            var description = root.TryGetProperty("data", out var data)
                ? data.ToString()
                : body;

            _ = Task.Run(() => ForwardToDiscordAsync(emoji, title, description));
            BattleLuckPlugin.LogInfo($"[Webhook] MuleRouter event: {eventType}");
            return true;
        }
        catch { return false; }
    }

    async Task ForwardToDiscordAsync(string emoji, string title, string description)
    {
        var url = _config?.DiscordWebhookUrl;
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                embeds = new[]
                {
                    new
                    {
                        title = $"{emoji} MuleRouter — {title}",
                        description = description.Length > 2048 ? description[..2045] + "..." : description,
                        color = title.Contains("Failed") ? 0xFF0000 : title.Contains("Low") ? 0xFFA500 : 0x00C853,
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            await _discordHttp.PostAsync(url, content);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[Webhook] Discord forward failed: {ex.Message}");
        }
    }

    static bool containsIpAddressOrLocalhost(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var lower = url.ToLowerInvariant();
        if (lower.Contains("localhost") || lower.Contains("127.0.0.1") || lower.Contains("0.0.0.0"))
            return true;
        var ipv4Pattern = @"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b";
        return System.Text.RegularExpressions.Regex.IsMatch(url, ipv4Pattern);
    }

    bool VerifyBasicAuth(HttpListenerRequest request)
    {
        if (string.IsNullOrWhiteSpace(_expectedBasicAuthHeader))
            return true;

        var incoming = request.Headers["Authorization"];
        return string.Equals(incoming, _expectedBasicAuthHeader, StringComparison.Ordinal);
    }

    void DispatchAction(WebhookRequest req)
    {
        BattleLuckPlugin.LogInfo($"[Webhook] Dispatching action: {req.Action}");
        GameEvents.OnWebhookAction?.Invoke(new WebhookActionEvent
        {
            Action = req.Action,
            Parameters = req.Data ?? new Dictionary<string, object>()
        });
    }

    void ResetRateLimitsIfNeeded()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastRateLimitReset).TotalSeconds >= 1)
        {
            _rateLimits.Clear();
            _lastRateLimitReset = now;
        }
    }

    static void SendResponse(HttpListenerResponse resp, int status, WebhookResponse data)
    {
        resp.StatusCode = status;
        resp.ContentType = "application/json";
        var json = JsonSerializer.Serialize(data);
        var buffer = Encoding.UTF8.GetBytes(json);
        resp.ContentLength64 = buffer.Length;
        resp.OutputStream.Write(buffer, 0, buffer.Length);
        resp.Close();
    }

    public void Stop()
    {
        _running = false;
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
    }

    public void Dispose()
    {
        Stop();
        _discordHttp.Dispose();
    }
}
