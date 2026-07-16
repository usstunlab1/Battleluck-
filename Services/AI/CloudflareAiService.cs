using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace BattleLuck.Core
{
    public sealed class CloudflareAiService : BaseAiService
    {
        private readonly string _accountId;
        private readonly string _apiToken;
        private readonly string? _gatewayId;
        private readonly string _model;

        public override bool IsEnabled => !_disposed && !_authFailed && HasUsableCredentials(_accountId, _apiToken) && !string.IsNullOrWhiteSpace(_gatewayId);

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
                LastError = "Cloudflare AI provider is not implemented in this build.";
                BattleLuckLogger.Warning(LastError);
                return null;
            }
            finally
            {
                ReleaseRateLimit();
            }
        }
    }
}
