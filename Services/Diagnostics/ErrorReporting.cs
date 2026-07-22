using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BattleLuck.Models;

namespace BattleLuck.Services.Diagnostics;

public interface IErrorReporter : IAsyncDisposable
{
    void Report(Exception exception, ErrorReportContext? context = null);
    ErrorReporterDiagnostics GetDiagnostics();
    Task FlushAsync(TimeSpan timeout);
}

public sealed class NoOpErrorReporter : IErrorReporter
{
    public static readonly NoOpErrorReporter Instance = new();
    public void Report(Exception exception, ErrorReportContext? context = null) { }
    public ErrorReporterDiagnostics GetDiagnostics() => new(false, 0, 0, 0, 0, 0, "disabled");
    public Task FlushAsync(TimeSpan timeout) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class BacktraceHttpErrorReporter : IErrorReporter
{
    sealed record Pending(byte[] Payload, string Fingerprint, bool Critical);
    static readonly Regex WebhookPattern = new(@"https?://[^\s]+/api/webhooks/[^\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex CredentialPattern = new(@"(?i)(bearer\s+|token[=:]\s*|api[_-]?key[=:]\s*)[^\s,;]+", RegexOptions.Compiled);
    readonly object _gate = new();
    readonly Queue<Pending> _critical = new();
    readonly Queue<Pending> _normal = new();
    readonly Dictionary<string, DateTimeOffset> _recent = new(StringComparer.Ordinal);
    readonly SemaphoreSlim _signal = new(0);
    readonly CancellationTokenSource _shutdown = new();
    readonly HttpClient _http;
    readonly Uri? _endpoint;
    readonly Task? _worker;
    bool _accepting;
    int _inFlight;
    long _submitted, _dropped, _deduplicated, _retried;
    string? _disabledReason;
    const int Capacity = 50;

    public BacktraceHttpErrorReporter(BacktraceSettings settings, string? submissionToken, HttpMessageHandler? handler = null)
    {
        _http = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _http.Timeout = TimeSpan.FromSeconds(10);
        if (!settings.Enabled) { _disabledReason = "disabled"; return; }
        if (!IsSafeSegment(settings.Subdomain)) { _disabledReason = "invalid subdomain"; return; }
        if (string.IsNullOrWhiteSpace(submissionToken)) { _disabledReason = "missing environment token"; return; }
        _endpoint = new Uri($"https://submit.backtrace.io/{Uri.EscapeDataString(settings.Subdomain)}/{Uri.EscapeDataString(submissionToken)}/json");
        _accepting = true;
        _worker = Task.Run(WorkerAsync);
    }

    public void Report(Exception exception, ErrorReportContext? context = null)
    {
        if (!_accepting || exception == null) return;
        var fingerprint = Fingerprint(exception);
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var stale in _recent.Where(pair => now - pair.Value > TimeSpan.FromMinutes(2)).Select(pair => pair.Key).ToArray())
                _recent.Remove(stale);
            if (_recent.TryGetValue(fingerprint, out var seen) && now - seen < TimeSpan.FromSeconds(60))
            { _deduplicated++; return; }
            _recent[fingerprint] = now;

            if (_critical.Count + _normal.Count >= Capacity)
            {
                if (context?.Critical == true && _normal.Count > 0) { _normal.Dequeue(); _dropped++; }
                else { _dropped++; return; }
            }

            var pending = new Pending(BuildPayload(exception, context, fingerprint), fingerprint, context?.Critical == true);
            if (pending.Critical) _critical.Enqueue(pending); else _normal.Enqueue(pending);
            _signal.Release();
        }
    }

    public ErrorReporterDiagnostics GetDiagnostics()
    {
        lock (_gate) return new(_accepting, _critical.Count + _normal.Count, _submitted, _dropped,
            _deduplicated, _retried, _disabledReason);
    }

    async Task WorkerAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try { await _signal.WaitAsync(_shutdown.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            Pending? report;
            lock (_gate)
            {
                report = _critical.Count > 0 ? _critical.Dequeue() : _normal.Count > 0 ? _normal.Dequeue() : null;
                if (report != null) _inFlight++;
            }
            if (report == null) continue;
            try { await SubmitAsync(report, _shutdown.Token).ConfigureAwait(false); }
            catch { lock (_gate) _dropped++; }
            finally { lock (_gate) _inFlight--; }
        }
    }

    async Task SubmitAsync(Pending report, CancellationToken cancellationToken)
    {
        var delays = new[] { TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30) };
        for (var attempt = 0; attempt < delays.Length; attempt++)
        {
            if (delays[attempt] > TimeSpan.Zero)
            {
                lock (_gate) _retried++;
                var jitter = TimeSpan.FromMilliseconds(System.Random.Shared.Next(0, 251));
                await Task.Delay(delays[attempt] + jitter, cancellationToken).ConfigureAwait(false);
            }
            HttpResponseMessage? response = null;
            try
            {
                using var content = new ByteArrayContent(report.Payload);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                response = await _http.PostAsync(_endpoint, content, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) { lock (_gate) _submitted++; return; }
                if (response.StatusCode == HttpStatusCode.BadRequest) { lock (_gate) _dropped++; return; }
                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    lock (_gate) { _accepting = false; _disabledReason = "forbidden until reload"; _dropped++; }
                    return;
                }
                var retry = response.StatusCode == (HttpStatusCode)429 || (int)response.StatusCode >= 500;
                if (!retry) { lock (_gate) _dropped++; return; }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            finally { response?.Dispose(); }
        }
        lock (_gate) _dropped++;
    }

    static byte[] BuildPayload(Exception exception, ErrorReportContext? context, string fingerprint)
    {
        var stack = Redact(exception.StackTrace ?? "");
        var payload = new
        {
            uuid = Guid.NewGuid().ToString(),
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            lang = "csharp",
            langVersion = Environment.Version.ToString(),
            agent = "BattleLuck",
            agentVersion = BattleLuckPluginInfo.PluginVersion,
            mainThread = "battleluck",
            threads = new[] { new { name = "battleluck", fault = true, stack } },
            exception = new { type = exception.GetType().FullName, message = Redact(exception.Message), stack },
            fingerprint,
            context = new
            {
                adminSteamId = context?.AdminSteamId,
                characterName = Redact(context?.CharacterName), command = Redact(context?.Command),
                action = Redact(context?.Action), modeId = context?.ModeId, eventRunId = context?.EventRunId,
                sessionId = context?.SessionId, requestId = context?.RequestId
            }
        };
        return JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    static string Fingerprint(Exception exception)
    {
        var firstFrame = (exception.StackTrace ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(exception.GetType().FullName + "|" + exception.Message + "|" + firstFrame));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    static string? Redact(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var redacted = WebhookPattern.Replace(value, "[redacted-webhook]");
        return CredentialPattern.Replace(redacted, "$1[redacted]");
    }

    static bool IsSafeSegment(string value) => !string.IsNullOrWhiteSpace(value) &&
        value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');

    public async Task FlushAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            lock (_gate) if (_critical.Count + _normal.Count + _inFlight == 0) return;
            await Task.Delay(25).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _accepting = false;
        await FlushAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        _shutdown.Cancel();
        if (_worker != null) try { await _worker.ConfigureAwait(false); } catch { }
        _http.Dispose(); _signal.Dispose(); _shutdown.Dispose();
    }
}
