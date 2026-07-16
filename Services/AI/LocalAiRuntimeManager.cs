using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using BattleLuck.Models;

namespace BattleLuck.Services.AI;

/// <summary>
/// Owns the optional local Ollama runtime used by the plugin. The bootstrap is
/// deliberately asynchronous so model downloads never stall the game thread.
/// </summary>
public sealed class LocalAiRuntimeManager : IDisposable
{
    readonly CancellationTokenSource _shutdown = new();
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    Process? _ownedServer;
    Task? _bootstrapTask;

    public void Start(AIConfig config)
    {
        if (!config.Enabled || !config.LlamaAPI.Enabled || !IsLocalOllama(config.LlamaAPI.BaseUrl))
            return;

        _bootstrapTask = Task.Run(() => BootstrapAsync(config.LlamaAPI, _shutdown.Token));
    }

    async Task BootstrapAsync(LlamaAPISettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var origin = GetOrigin(settings.BaseUrl);
            var executable = FindOllamaExecutable();

            if (!await IsHealthyAsync(origin, cancellationToken))
            {
                if (executable == null)
                {
                    BattleLuckLogger.Warning("[LocalAI] Ollama is not running and ollama.exe was not found. Install Ollama on the server or bundle it under BepInEx/plugins/BattleLuck/AI.");
                    return;
                }

                _ownedServer = Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "serve",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Environment = { ["OLLAMA_HOST"] = $"{new Uri(origin).Host}:{new Uri(origin).Port}" }
                });
                BattleLuckLogger.Info($"[LocalAI] Started Ollama automatically (pid={_ownedServer?.Id}).");

                for (var attempt = 0; attempt < 30 && !cancellationToken.IsCancellationRequested; attempt++)
                {
                    if (await IsHealthyAsync(origin, cancellationToken))
                        break;
                    await Task.Delay(500, cancellationToken);
                }
            }

            if (!await IsHealthyAsync(origin, cancellationToken))
            {
                BattleLuckLogger.Warning($"[LocalAI] Ollama did not become ready at {origin}.");
                return;
            }

            if (await HasModelAsync(origin, settings.Model, cancellationToken))
            {
                BattleLuckLogger.Info($"[LocalAI] Ollama ready with model '{settings.Model}'.");
                return;
            }

            if (executable == null)
            {
                BattleLuckLogger.Warning($"[LocalAI] Model '{settings.Model}' is missing and ollama.exe was not found to pull it.");
                return;
            }

            BattleLuckLogger.Info($"[LocalAI] Pulling missing model '{settings.Model}' in the background.");
            using var pull = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"pull {QuoteArgument(settings.Model)}",
                UseShellExecute = false,
                CreateNoWindow = true,
                Environment = { ["OLLAMA_HOST"] = origin }
            });
            if (pull == null)
                throw new InvalidOperationException("Failed to start the Ollama model pull process.");

            await pull.WaitForExitAsync(cancellationToken);
            if (pull.ExitCode == 0)
                BattleLuckLogger.Info($"[LocalAI] Model '{settings.Model}' is ready.");
            else
                BattleLuckLogger.Warning($"[LocalAI] Ollama model pull failed with exit code {pull.ExitCode}.");
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex)
        {
            BattleLuckLogger.Warning($"[LocalAI] Automatic runtime bootstrap failed: {ex.Message}");
        }
    }

    async Task<bool> IsHealthyAsync(string origin, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync($"{origin}/api/tags", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    async Task<bool> HasModelAsync(string origin, string model, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{origin}/api/tags", cancellationToken);
        if (!response.IsSuccessStatusCode)
            return false;
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!json.RootElement.TryGetProperty("models", out var models))
            return false;
        return models.EnumerateArray().Any(entry =>
            entry.TryGetProperty("name", out var name) &&
            ModelNamesMatch(name.GetString(), model));
    }

    static bool ModelNamesMatch(string? available, string requested)
    {
        static string Normalize(string value)
        {
            var normalized = value.Trim();
            return normalized.Contains(':', StringComparison.Ordinal)
                ? normalized
                : $"{normalized}:latest";
        }

        return !string.IsNullOrWhiteSpace(available) &&
               !string.IsNullOrWhiteSpace(requested) &&
               string.Equals(Normalize(available), Normalize(requested), StringComparison.OrdinalIgnoreCase);
    }

    static bool IsLocalOllama(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return false;
        return uri.Port == 11434 && (uri.IsLoopback || uri.Host.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase));
    }

    static string GetOrigin(string baseUrl)
    {
        var uri = new Uri(baseUrl);
        return $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }

    static string? FindOllamaExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "AI", "ollama.exe"),
            Path.Combine(BepInEx.Paths.PluginPath, "BattleLuck", "AI", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "ollama.exe")
        };
        var local = candidates.FirstOrDefault(File.Exists);
        if (local != null)
            return local;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return path.Split(Path.PathSeparator)
            .Select(folder => Path.Combine(folder.Trim(), "ollama.exe"))
            .FirstOrDefault(File.Exists);
    }

    static string QuoteArgument(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    public void Dispose()
    {
        _shutdown.Cancel();
        _http.Dispose();
        if (_ownedServer is { HasExited: false })
        {
            try { _ownedServer.Kill(entireProcessTree: true); }
            catch { }
        }
        _ownedServer?.Dispose();
        _shutdown.Dispose();
    }
}
