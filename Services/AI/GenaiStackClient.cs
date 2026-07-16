using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace BattleLuck.Services.AI;

/// <summary>
/// HTTP client adapter for genai-stack FastAPI backend.
/// Routes natural-language queries and RAG requests to the remote genai-stack instance.
/// </summary>
public class GenaiStackClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly bool _enabled;

    public GenaiStackClient(string baseUrl = "http://localhost:8504", bool enabled = true)
    {
        _baseUrl = baseUrl?.TrimEnd('/') ?? "http://localhost:8504";
        _enabled = enabled;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// Send a query to genai-stack and get a response (LLM-only mode).
    /// </summary>
    public async Task<GenaiResponse?> QueryAsync(string question)
    {
        if (!_enabled)
            return null;

        BattleLuckLogger.Info($"[GenaiStack] QueryAsync -> {_baseUrl}/query rag=false question='{Truncate(question, 120)}'");
        try
        {
            var url = $"{_baseUrl}/query?text={Uri.EscapeDataString(question)}&rag=false";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                BattleLuckLogger.Warning($"[GenaiStack] QueryAsync failed: {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var parsed = JsonSerializer.Deserialize<GenaiResponse>(json);
            BattleLuckLogger.Info($"[GenaiStack] QueryAsync OK model={parsed?.Model} result='{Truncate(parsed?.Result, 200)}'");
            return parsed;
        }
        catch (Exception ex)
        {
            BattleLuckLogger.Error($"[GenaiStack] QueryAsync exception: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Send a query to genai-stack with RAG (Retrieval Augmented Generation) enabled.
    /// Uses the Neo4j knowledge graph for context.
    /// </summary>
    public async Task<GenaiResponse?> QueryWithRagAsync(string question)
    {
        if (!_enabled)
            return null;

        BattleLuckLogger.Info($"[GenaiStack] QueryWithRagAsync -> {_baseUrl}/query rag=true question='{Truncate(question, 120)}'");
        try
        {
            var url = $"{_baseUrl}/query?text={Uri.EscapeDataString(question)}&rag=true";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                BattleLuckLogger.Warning($"[GenaiStack] RAG query failed: {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var parsed = JsonSerializer.Deserialize<GenaiResponse>(json);
            BattleLuckLogger.Info($"[GenaiStack] QueryWithRagAsync OK model={parsed?.Model} result='{Truncate(parsed?.Result, 200)}'");
            return parsed;
        }
        catch (Exception ex)
        {
            BattleLuckLogger.Error($"[GenaiStack] QueryWithRagAsync exception: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Stream query results from genai-stack (SSE endpoint).
    /// Yields tokens as they arrive from the LLM.
    /// </summary>
    public async IAsyncEnumerable<string> StreamQueryAsync(string question, bool useRag = false)
    {
        if (!_enabled)
            yield break;

        BattleLuckLogger.Info($"[GenaiStack] StreamQueryAsync -> {_baseUrl}/query-stream rag={useRag} question='{Truncate(question, 120)}'");
        var url = $"{_baseUrl}/query-stream?text={Uri.EscapeDataString(question)}&rag={useRag}";
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        }
        catch (Exception ex)
        {
            BattleLuckLogger.Error($"[GenaiStack] StreamQueryAsync exception: {ex.Message}");
            yield break;
        }

        if (!response.IsSuccessStatusCode)
        {
            BattleLuckLogger.Warning($"[GenaiStack] Stream failed: {response.StatusCode}");
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        string? line;
        var tokenCount = 0;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (!line.StartsWith("data: "))
                continue;

            var data = line.Substring(6);
            if (string.IsNullOrWhiteSpace(data))
                continue;

            string? token = null;
            try
            {
                var json = JsonSerializer.Deserialize<JsonElement>(data);
                if (json.TryGetProperty("token", out var tokenElem) && tokenElem.ValueKind == JsonValueKind.String)
                {
                    token = tokenElem.GetString();
                }
            }
            catch
            {
                // Silently skip malformed tokens
            }

            if (!string.IsNullOrEmpty(token))
            {
                tokenCount++;
                yield return token;
            }
        }

        BattleLuckLogger.Info($"[GenaiStack] StreamQueryAsync complete: tokens={tokenCount}");
    }

    /// <summary>
    /// Generate a support ticket with AI-suggested title and question refinement.
    /// </summary>
    public async Task<TicketResponse?> GenerateTicketAsync(string text)
    {
        if (!_enabled)
            return null;

        BattleLuckLogger.Info($"[GenaiStack] GenerateTicketAsync -> {_baseUrl}/generate-ticket text='{Truncate(text, 120)}'");
        try
        {
            var url = $"{_baseUrl}/generate-ticket?text={Uri.EscapeDataString(text)}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                BattleLuckLogger.Warning($"[GenaiStack] Ticket generation failed: {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var parsed = JsonSerializer.Deserialize<TicketResponse>(json);
            BattleLuckLogger.Info($"[GenaiStack] GenerateTicketAsync OK title='{Truncate(parsed?.Result?.Title, 120)}'");
            return parsed;
        }
        catch (Exception ex)
        {
            BattleLuckLogger.Error($"[GenaiStack] GenerateTicketAsync exception: {ex.Message}");
            return null;
        }
    }

    /// <summary>Health check: ping the genai-stack API endpoint.</summary>
    public async Task<bool> HealthCheckAsync()
    {
        if (!_enabled)
            return false;

        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/");
            BattleLuckLogger.Info($"[GenaiStack] HealthCheck -> {_baseUrl}/ status={response.StatusCode}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            BattleLuckLogger.Warning($"[GenaiStack] HealthCheck failed: {ex.Message}");
            return false;
        }
    }

    static string Truncate(string? value, int max)
    {
        if (value == null)
            return "(null)";
        return value.Length <= max ? value : value[..max] + "...";
    }

    public record GenaiResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("result")] string Result,
        [property: System.Text.Json.Serialization.JsonPropertyName("model")] string Model
    );

    public record TicketResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("result")] TicketData Result,
        [property: System.Text.Json.Serialization.JsonPropertyName("model")] string Model
    );

    public record TicketData(
        [property: System.Text.Json.Serialization.JsonPropertyName("title")] string Title,
        [property: System.Text.Json.Serialization.JsonPropertyName("text")] string Text
    );
}
