using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BattleLuck.Core;

public sealed class LlamaAIService : BaseAiService
{
    readonly string _apiKey;
    readonly string _baseUrl;
    readonly string _apiBaseUrl;
    readonly string _model;
    private DateTime _lastHealthCheckUtc = DateTime.MinValue;
    private bool _healthCheckPending = true;
    private const int HealthCheckIntervalSeconds = 10;

    public override bool IsEnabled => !_disposed && !_authFailed && HasUsableConfiguration(_apiKey, _baseUrl);
    public string Model => _model;
    public string BaseUrl => _baseUrl;
    public string ApiBaseUrl => _apiBaseUrl;

    public LlamaAIService(string apiKey, string baseUrl, string model, int maxRequestsPerSecond = 10, int timeoutSeconds = 90)
        : base(maxRequestsPerSecond, timeoutSeconds)
    {
        _apiKey = apiKey ?? string.Empty;
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:11434" : baseUrl.TrimEnd('/');
        _apiBaseUrl = NormalizeApiBaseUrl(_baseUrl);
        _model = string.IsNullOrWhiteSpace(model) ? "llama2" : model.Trim();
        if (HasUsableApiKey(_apiKey))
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    /// <summary>Check if Ollama service is accessible and model is available.</summary>
    public async Task<bool> CheckHealthAsync(bool force = false)
    {
        if (!force && !_healthCheckPending && DateTime.UtcNow - _lastHealthCheckUtc < TimeSpan.FromSeconds(HealthCheckIntervalSeconds))
            return true;

        try
        {
            using var response = await _httpClient.GetAsync($"{_apiBaseUrl}/api/tags");
            if (!response.IsSuccessStatusCode)
            {
                _healthCheckPending = true;
                _lastHealthCheckUtc = DateTime.UtcNow;
                return false;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("models", out var models))
            {
                _healthCheckPending = true;
                _lastHealthCheckUtc = DateTime.UtcNow;
                return false;
            }

            var hasModel = models.EnumerateArray().Any(m =>
                m.TryGetProperty("name", out var name) &&
                string.Equals(name.GetString(), _model, StringComparison.OrdinalIgnoreCase));

            _healthCheckPending = !hasModel;
            _lastHealthCheckUtc = DateTime.UtcNow;
            return hasModel;
        }
        catch (Exception ex)
        {
            BattleLuckLogger.Warning($"[Llama] Health check failed: {ex.Message}");
            _healthCheckPending = true;
            _lastHealthCheckUtc = DateTime.UtcNow;
            return false;
        }
    }

    private async Task<bool> EnsureHealthyAsync()
    {
        if (_healthCheckPending || DateTime.UtcNow - _lastHealthCheckUtc > TimeSpan.FromSeconds(HealthCheckIntervalSeconds))
            return await CheckHealthAsync().ConfigureAwait(false);

        return true;
    }

    public async Task<string?> GetChatCompletionAsync(List<ChatMessage> messages, float temperature = 0.7f, int maxTokens = 300)
    {
        if (!IsEnabled)
            return null;

        if (!await EnsureHealthyAsync().ConfigureAwait(false))
            return null;

        maxTokens = Math.Min(maxTokens, GetMaxAllowedTokens(messages));
        await ApplyRateLimitAsync();
        try
        {
            var requestBody = new
            {
                model = _model,
                messages = messages.Select(m => new
                {
                    role = NormalizeRole(m.Role),
                    content = m.Content
                }),
                temperature,
                max_tokens = maxTokens,
                stream = false
            };

            var json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_apiBaseUrl}/api/chat", content);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    HandleAuthFailure("Llama API", $"HTTP {(int)response.StatusCode}: {responseJson}");
                    return null;
                }

                HandleHttpError("Llama API", response.StatusCode, responseJson);
                return null;
            }

            using var doc = JsonDocument.Parse(responseJson);
            var text = doc.RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(text))
            {
                LastError = "Llama API returned empty content.";
                BattleLuckLogger.Warning(LastError);
                return null;
            }

            RecordSuccess();
            return text;
        }
        catch (TaskCanceledException ex)
        {
            HandleTimeout("Llama API", ex);
            return null;
        }
        catch (HttpRequestException ex)
        {
            HandleException("Llama API", ex);
            _healthCheckPending = true;
            return null;
        }
        catch (Exception ex)
        {
            HandleException("Llama API", ex);
            return null;
        }
        finally
        {
            ReleaseRateLimit();
        }
    }

    public async IAsyncEnumerable<string> GetChatCompletionStreamAsync(List<ChatMessage> messages, float temperature = 0.7f, int maxTokens = 300)
    {
        if (!IsEnabled)
            yield break;

        if (!await EnsureHealthyAsync().ConfigureAwait(false))
            yield break;

        maxTokens = Math.Min(maxTokens, GetMaxAllowedTokens(messages));
        await ApplyRateLimitAsync();

        var requestBody = new
        {
            model = _model,
            messages = messages.Select(m => new
            {
                role = NormalizeRole(m.Role),
                content = m.Content
            }),
            temperature,
            max_tokens = maxTokens,
            stream = true
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync($"{_apiBaseUrl}/api/chat", content);

        if (!response.IsSuccessStatusCode)
        {
            var responseJson = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                HandleAuthFailure("Llama API (stream)", $"HTTP {(int)response.StatusCode}: {responseJson}");
            }
            else
            {
                HandleHttpError("Llama API (stream)", response.StatusCode, responseJson);
            }
            ReleaseRateLimit();
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                line = line[5..].Trim();

            if (string.Equals(line, "[DONE]", StringComparison.OrdinalIgnoreCase))
                break;

            if (TryExtractStreamContent(line, out var extractedContent))
                yield return extractedContent;
        }

        RecordSuccess();
        ReleaseRateLimit();
    }

    private static bool TryExtractStreamContent(string line, out string content)
    {
        content = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(line);
            content = ExtractContentFromStreamEvent(doc.RootElement);
            return !string.IsNullOrWhiteSpace(content);
        }
        catch
        {
            return false;
        }
    }

    private static string ExtractContentFromStreamEvent(JsonElement root)
    {
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
        {
            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
                {
                    if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                        return content.GetString() ?? string.Empty;
                }

                if (choice.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
                {
                    if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                        return content.GetString() ?? string.Empty;
                }
            }
        }

        if (root.TryGetProperty("message", out var messageNode) && messageNode.ValueKind == JsonValueKind.Object)
        {
            if (messageNode.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                return content.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private int GetMaxAllowedTokens(List<ChatMessage> messages)
    {
        var modelLimit = GetModelTokenLimit();
        var promptTokens = EstimateTokens(messages);
        const int safetyMargin = 64;
        var available = Math.Max(1, modelLimit - promptTokens - safetyMargin);
        return Math.Max(1, Math.Min(available, GetDefaultMaxTokens()));
    }

    private int GetDefaultMaxTokens() => _model.Contains("3.2", StringComparison.OrdinalIgnoreCase) ? 4096 : 2048;

    private int GetModelTokenLimit()
    {
        if (_model.Contains("3.2", StringComparison.OrdinalIgnoreCase) || _model.Contains("3b", StringComparison.OrdinalIgnoreCase))
            return 8192;
        if (_model.Contains("llama2", StringComparison.OrdinalIgnoreCase))
            return 4096;
        return 4096;
    }

    private static int EstimateTokens(List<ChatMessage> messages)
    {
        var text = string.Join(" ", messages.Select(m => m.Content));
        var wordCount = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Max(1, (int)(wordCount * 1.3));
    }

    static string NormalizeRole(string? role)
    {
        return role?.Trim().ToLowerInvariant() switch
        {
            "model" => "assistant",
            "assistant" => "assistant",
            "system" => "system",
            "tool" => "tool",
            "function" => "function",
            _ => "user"
        };
    }

    public static bool HasUsableApiKey(string? apiKey) =>
        IsUsableCredential(apiKey, minLength: 20);

    public static bool HasUsableConfiguration(string? apiKey, string? baseUrl) =>
        HasUsableApiKey(apiKey) || IsLocalBaseUrl(baseUrl);

    static bool IsLocalBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return false;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return false;

        return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Equals("::1", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase);
    }

    static string NormalizeApiBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return baseUrl;

        return $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }
}
