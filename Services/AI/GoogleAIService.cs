using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace BattleLuck.Core
{
    public class GoogleAIService : BaseAiService
    {
        private readonly string _apiKey;
        private readonly string _model;

        public override bool IsEnabled => !_disposed && !_authFailed && HasUsableApiKey(_apiKey);

        public GoogleAIService(string apiKey, string model, int maxRequestsPerSecond = 10, int timeoutSeconds = 90)
            : base(maxRequestsPerSecond, timeoutSeconds)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _model = model ?? throw new ArgumentNullException(nameof(model));
        }

        public async Task<string?> GetChatCompletionAsync(List<ChatMessage> messages, float temperature = 0.8f, int maxTokens = 300)
        {
            if (!IsEnabled)
                return null;

            await ApplyRateLimitAsync();
            try
            {
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
                
                var systemPrompt = string.Join("\n\n", messages
                    .Where(m => string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
                    .Select(m => m.Content)
                    .Where(c => !string.IsNullOrWhiteSpace(c)));

                var contents = messages
                    .Where(m => !string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
                    .Select(m => new
                    {
                        role = NormalizeRole(m.Role),
                        parts = new[] { new { text = m.Content } }
                    })
                    .ToList();

                if (contents.Count == 0 && !string.IsNullOrWhiteSpace(systemPrompt))
                {
                    contents.Add(new
                    {
                        role = "user",
                        parts = new[] { new { text = systemPrompt } }
                    });
                    systemPrompt = string.Empty;
                }

                var requestBody = new
                {
                    contents,
                    systemInstruction = string.IsNullOrWhiteSpace(systemPrompt) ? null : new
                    {
                        parts = new[] { new { text = systemPrompt } }
                    },
                    generationConfig = new
                    {
                        temperature = temperature,
                        maxOutputTokens = maxTokens
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                var responseJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    if (IsAuthFailure(responseJson))
                    {
                        HandleAuthFailure("Google AI", $"{response.StatusCode}: {responseJson}");
                        return null;
                    }

                    HandleHttpError("Google AI", response.StatusCode, responseJson);
                    return null;
                }

                using var doc = JsonDocument.Parse(responseJson);
                var text = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                RecordSuccess();
                return text;
            }
            catch (TaskCanceledException ex)
            {
                HandleTimeout("Google AI", ex);
                return null;
            }
            catch (Exception ex)
            {
                HandleException("Google AI", ex);
                return null;
            }
            finally
            {
                ReleaseRateLimit();
            }
        }

        private static string NormalizeRole(string? role)
        {
            return role?.Trim().ToLowerInvariant() switch
            {
                "model" => "model",
                "assistant" => "model",
                "user" => "user",
                _ => "user"
            };
        }

        public static bool HasUsableApiKey(string? apiKey) =>
            IsUsableCredential(apiKey, minLength: 20);

        static bool IsAuthFailure(string responseJson) =>
            responseJson.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase) ||
            responseJson.Contains("API key not valid", StringComparison.OrdinalIgnoreCase) ||
            responseJson.Contains("PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase) ||
            responseJson.Contains("UNAUTHENTICATED", StringComparison.OrdinalIgnoreCase);
    }

    public class ChatMessage
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";

        public static ChatMessage System(string content) => new() { Role = "system", Content = content };
        public static ChatMessage User(string content) => new() { Role = "user", Content = content };
        public static ChatMessage Assistant(string content) => new() { Role = "model", Content = content };
    }
}
