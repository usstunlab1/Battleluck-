public class AiLoggerController : IDisposable
{
    private AiLoggerConfig? _config;
    private readonly HttpClient _httpClient;
    private readonly List<GameEventEntry> _eventBuffer;
    private DateTime _lastFlushTime;
    private bool _disposed;

    public AiLoggerController()
    {
        _httpClient = new HttpClient();
        _eventBuffer = new List<GameEventEntry>();
        _lastFlushTime = DateTime.UtcNow;
    }

    public void Configure(AiLoggerConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public void Tick()
    {
        if (_disposed || _config == null || !_config.Enabled)
            return;

        var now = DateTime.UtcNow;
        var flushInterval = TimeSpan.FromSeconds(_config.Buffer.FlushIntervalSec);

        if (now - _lastFlushTime >= flushInterval || _eventBuffer.Count >= _config.Buffer.MaxSize)
        {
            FlushBuffer();
            _lastFlushTime = now;
        }
    }

    public void LogEvent(string type, string details)
    {
        if (_disposed || _config == null || !_config.Enabled)
            return;

        _eventBuffer.Add(new GameEventEntry
        {
            Timestamp = DateTime.UtcNow,
            Type = type,
            Details = details
        });
    }

    private void FlushBuffer()
    {
        if (_eventBuffer.Count == 0 || _config == null)
            return;

        try
        {
            if (_config.Providers.Gemini.Enabled)
            {
                _ = FlushToGeminiAsync();
            }
            else if (_config.Providers.SuperuserSidecar.Enabled)
            {
                _ = FlushToSidecarAsync();
            }

            _eventBuffer.Clear();
        }
        catch (Exception ex)
        {
            BattleLuckLogger.Warning($"AI Logger flush error: {ex.Message}");
        }
    }

    private async Task FlushToGeminiAsync()
    {
        if (_config == null || string.IsNullOrWhiteSpace(_config.Providers.Gemini.ApiKey))
            return;

        try
        {
            var eventsText = string.Join("\n", _eventBuffer.Select(e => $"[{e.Timestamp:HH:mm:ss}] {e.Type}: {e.Details}"));
            var prompt = $"{_config.Prompts.System}\n\nEvents:\n{eventsText}";

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_config.Providers.Gemini.Model}:generateContent?key={_config.Providers.Gemini.ApiKey}";
            
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = prompt } }
                    }
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(responseJson);
                var summary = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                if (!string.IsNullOrWhiteSpace(summary) && _config.Discord.IsConfigured)
                {
                    _ = SendToDiscordAsync(summary);
                }
            }
        }
        catch (Exception ex)
        {
            BattleLuckLogger.Warning($"AI Logger Gemini flush error: {ex.Message}");
        }
    }

    private async Task FlushToSidecarAsync()
    {
        if (_config == null || string.IsNullOrWhiteSpace(_config.Providers.SuperuserSidecar.Url))
            return;

        try
        {
            var url = $"{_config.Providers.SuperuserSidecar.Url.TrimEnd('/')}/api/summarize";
            
            var requestBody = new
            {
                events = _eventBuffer,
                systemPrompt = _config.Prompts.System
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            if (!string.IsNullOrWhiteSpace(_config.Providers.SuperuserSidecar.ApiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.Providers.SuperuserSidecar.ApiKey);
            }

            var response = await _httpClient.PostAsync(url, content);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<JsonElement>(responseJson);
                var summary = result.GetProperty("summary").GetString();

                if (!string.IsNullOrWhiteSpace(summary) && _config.Discord.IsConfigured)
                {
                    _ = SendToDiscordAsync(summary);
                }
            }
        }
        catch (Exception ex)
        {
            BattleLuckLogger.Warning($"AI Logger sidecar flush error: {ex.Message}");
        }
    }

        private async Task SendToDiscordAsync(string summary)
        {
            if (_config == null || string.IsNullOrWhiteSpace(_config.Discord.WebhookUrl))
                return;

        try
        {
            var payload = new DiscordWebhookPayload
            {
                Embeds = new List<DiscordEmbed>
                {
                    new DiscordEmbed
                    {
                        Title = "🎮 BattleLuck Event Summary",
                        Description = summary,
                        Color = 0x5865F2,
                        Timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            await _httpClient.PostAsync(_config.Discord.WebhookUrl, content);
        }
        catch (Exception ex)
        {
            BattleLuckLogger.Warning($"AI Logger Discord error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        FlushBuffer();
        _httpClient?.Dispose();
    }
}
