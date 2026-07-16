using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BattleLuck.Models;

namespace BattleLuck.Core
{
    public class BattleAiSidecarService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private BattleAiSidecarSettings _settings;
        private bool _disposed;

        public string BaseUrl => _settings?.BaseUrl ?? "";
        public bool IsEnabled => _settings?.Enabled == true && !string.IsNullOrWhiteSpace(_settings.BaseUrl);
        public string? LastError { get; private set; }
        public DateTime? LastSuccessfulCallUtc { get; private set; }

        public BattleAiSidecarService(BattleAiSidecarSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds > 0 ? settings.TimeoutSeconds : 10)
            };
        }

        public async Task<BattleAiQueryEnrichmentResult?> EnrichDirectQueryAsync(BattleAiQueryEnrichmentRequest request)
        {
            if (_disposed || !IsEnabled)
                return null;

            try
            {
                var url = $"{BaseUrl.TrimEnd('/')}/api/enrich";
                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = content
                };

                if (!string.IsNullOrWhiteSpace(_settings.AuthKey))
                {
                    httpRequest.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.AuthKey);
                }

                var response = await _httpClient.SendAsync(httpRequest);
                var responseJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    LastError = $"HTTP {response.StatusCode}: {responseJson}";
                    BattleLuckLogger.Warning($"Sidecar enrichment failed: {LastError}");
                    return null;
                }

                var result = JsonSerializer.Deserialize<BattleAiQueryEnrichmentResult>(responseJson);
                LastSuccessfulCallUtc = DateTime.UtcNow;
                LastError = null;
                return result;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                BattleLuckLogger.Warning($"Sidecar enrichment error: {ex.Message}");
                return null;
            }
        }

        public async Task<BattleAiHealthResponse?> GetHealthAsync()
        {
            if (_disposed || !IsEnabled)
                return null;

            try
            {
                var url = $"{BaseUrl.TrimEnd('/')}/health";
                var response = await _httpClient.GetAsync(url);
                var responseJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    LastError = $"HTTP {response.StatusCode}: {responseJson}";
                    return null;
                }

                var result = JsonSerializer.Deserialize<BattleAiHealthResponse>(responseJson);
                LastSuccessfulCallUtc = DateTime.UtcNow;
                LastError = null;
                return result;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                BattleLuckLogger.Warning($"Sidecar health check error: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _httpClient?.Dispose();
        }
    }
}
