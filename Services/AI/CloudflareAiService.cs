using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BattleLuck.Core
{
    public sealed class CloudflareAiService : BaseAiService
    {
        private readonly string _accountId;
        private readonly string _apiToken;
        private readonly string? _gatewayId;
        private readonly string _model;

        // Service is usable with direct API if credentials are set; gateway is optional.
        public override bool IsEnabled => !_disposed && !_authFailed && HasUsableCredentials(_accountId, _apiToken);

        public CloudflareAiService(string accountId, string apiToken, string? gatewayId, string model, int maxRequestsPerSecond = 10, int timeoutSeconds = 90)
            : base(maxRequestsPerSecond, timeoutSeconds)
        {
            _accountId = accountId ?? string.Empty;
            _apiToken = apiToken ?? string.Empty;
            _gatewayId = string.IsNullOrWhiteSpace(gatewayId) ? null : gatewayId;
            _model = model ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(_apiToken))
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiToken);
        }

        public static bool HasUsableCredentials(string? accountId, string? apiToken)
        {
            return IsUsableCredential(accountId, minLength: 5) && IsUsableCredential(apiToken, minLength: 10);
        }

        public async Task<string?> GetChatCompletionAsync(List<ChatMessage> messages, float temperature = 0.7f, int maxTokens = 300)
        {
            if (!IsEnabled)
                return null;

            await ApplyRateLimitAsync();
            try
            {
                var url = string.IsNullOrWhiteSpace(_gatewayId)
                    ? $"https://api.cloudflare.com/client/v4/accounts/{_accountId}/ai/run/{_model}"
                    : $"https://gateway.ai.cloudflare.com/v1/{_accountId}/{_gatewayId}/workers-ai/{_model}";

                var requestBody = new
                {
                    messages = messages.Select(m => new
                    {
                        role = NormalizeRole(m.Role),
                        content = m.Content
                    })
                };

                var json = JsonSerializer.Serialize(requestBody);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                var responseJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        HandleAuthFailure("Cloudflare AI", $"HTTP {(int)response.StatusCode}: {responseJson}");
                        return null;
                    }

                    HandleHttpError("Cloudflare AI", response.StatusCode, responseJson);
                    return null;
                }

                using var doc = JsonDocument.Parse(responseJson);
                if (doc.RootElement.TryGetProperty("result", out var resultElement) &&
                    resultElement.TryGetProperty("response", out var responseElement))
                {
                    var text = responseElement.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        RecordSuccess();
                        return text;
                    }
                }

                LastError = "Cloudflare AI response did not contain a valid result.";
                BattleLuckLogger.Warning($"{LastError} Response: {responseJson}");
                return null;
            }
            catch (TaskCanceledException ex)
            {
                HandleTimeout("Cloudflare AI", ex);
                return null;
            }
            catch (Exception ex)
            {
                HandleException("Cloudflare AI", ex);
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
                "system" => "system",
                "user" => "user",
                "model" => "assistant", // Google uses "model"
                "assistant" => "assistant",
                _ => "user"
            };
        }
    }
}
