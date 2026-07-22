using System.Net.Http.Json;
using System.Text.Json;

namespace BattleLuck.Services.Assistant;

public sealed class LocalModelAdminService : IDisposable
{
    static readonly HashSet<string> InstallAllowlist = new(StringComparer.OrdinalIgnoreCase)
    { "qwen2.5:0.5b" };
    readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    readonly SemaphoreSlim _installGate = new(1, 1);

    public async Task<string> StatusAsync(Uri endpoint, string model)
    {
        EnsureLoopback(endpoint);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await _http.GetAsync(new Uri(endpoint, "/api/tags"), timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return $"Local LLM unavailable (HTTP {(int)response.StatusCode}); AI-lite is active.";
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            var installed = document.RootElement.TryGetProperty("models", out var models) && models.EnumerateArray().Any(item =>
                item.TryGetProperty("name", out var name) && string.Equals(name.GetString(), model, StringComparison.OrdinalIgnoreCase));
            return installed ? $"Local LLM ready: {model}." : $"Ollama is reachable, but {model} is not installed; AI-lite is active.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        { return "Local LLM unavailable; AI-lite is active."; }
    }

    public async Task<string> InstallAsync(Uri endpoint, string model)
    {
        EnsureLoopback(endpoint);
        if (!InstallAllowlist.Contains(model)) return "Model install rejected: only qwen2.5:0.5b is allowlisted.";
        if (!await _installGate.WaitAsync(0).ConfigureAwait(false)) return "A local model installation is already running.";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "/api/pull"))
            { Content = JsonContent.Create(new { name = model, stream = false }) };
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? $"Local model {model} installed and ready."
                : $"Ollama rejected the install (HTTP {(int)response.StatusCode}); AI-lite remains active.";
        }
        catch (OperationCanceledException) { return "Local model installation timed out; AI-lite remains active."; }
        catch (HttpRequestException) { return "Ollama is unavailable; AI-lite remains active."; }
        finally { _installGate.Release(); }
    }

    static void EnsureLoopback(Uri endpoint)
    {
        if (endpoint.Scheme is not ("http" or "https") || !endpoint.IsLoopback)
            throw new InvalidOperationException("The local LLM endpoint must be loopback-only.");
    }

    public void Dispose() { _http.Dispose(); _installGate.Dispose(); }
}
