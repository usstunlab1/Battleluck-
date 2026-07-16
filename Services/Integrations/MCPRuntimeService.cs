using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using BattleLuck.Models;
using BattleLuck.Services.Runtime;

namespace BattleLuck.Services
{
    /// <summary>
    /// Remote-only MCP (Model Context Protocol) runtime for BattleLuck.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This runtime communicates exclusively with remote HTTP/SSE MCP servers.
    /// Local stdio transport support was removed in a recent refactor; MCP servers must be reachable over HTTP.
    /// </para>
    /// <para>
    /// <strong>Breaking change:</strong> stdio-based MCP servers are no longer started by this runtime.
    /// Configure servers with <c>transport: "http"</c> or <c>transport: "sse"</c> and a valid <c>url</c>.
    /// </para>
    /// </remarks>
    public sealed class MCPRuntimeService : IDisposable
    {
        private readonly MCPRuntimeSettings _settings;
        private readonly IRuntimeServiceBootstrap? _runtimeServices;
        private readonly Dictionary<string, MCPServerProcess> _runningServers = new();
        private readonly Dictionary<string, HttpClient> _httpClients = new();
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>
        /// Gets a value indicating whether the MCP runtime is enabled in configuration.
        /// </summary>
        public bool IsEnabled => _settings.Enabled;

        /// <summary>
        /// Gets the currently registered/running MCP server processes.
        /// </summary>
        public IReadOnlyDictionary<string, MCPServerProcess> RunningServers => _runningServers;

        /// <summary>
        /// Gets the last recorded error message, if any.
        /// </summary>
        public string LastError { get; private set; } = "";

        /// <summary>
        /// Initializes a new instance of the <see cref="MCPRuntimeService"/> class.
        /// </summary>
        /// <param name="settings">The MCP runtime configuration settings.</param>
        /// <param name="runtimeServices">Optional runtime service bootstrap for shared infrastructure.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is <c>null</c>.</exception>
        public MCPRuntimeService(MCPRuntimeSettings settings, IRuntimeServiceBootstrap? runtimeServices = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _runtimeServices = runtimeServices;
        }

        /// <summary>
        /// Initializes all configured remote MCP servers.
        /// </summary>
        /// <remarks>
        /// Only servers with <c>transport</c> of <c>http</c> or <c>sse</c> are supported.
        /// Other transports are logged as warnings and skipped.
        /// </remarks>
        public Task InitializeAsync()
        {
            if (!IsEnabled)
            {
                BattleLuckLogger.Warning("MCP runtime is disabled in configuration");
                return Task.CompletedTask;
            }

            try
            {
                foreach (var (serverId, config) in _settings.Servers)
                {
                    if (!config.Enabled)
                    {
                        BattleLuckLogger.Log($"MCP server '{serverId}' is disabled, skipping");
                        continue;
                    }

                    try
                    {
                        var transport = config.Transport?.ToLowerInvariant() ?? "stdio";

                        if (transport == "http" || transport == "sse")
                        {
                            if (string.IsNullOrWhiteSpace(config.Url))
                            {
                                BattleLuckLogger.Warning($"MCP server '{serverId}' has transport '{transport}' but no url configured");
                                continue;
                            }

                            var httpClient = new HttpClient
                            {
                                BaseAddress = new Uri(config.Url.TrimEnd('/') + "/"),
                                Timeout = TimeSpan.FromSeconds(Math.Max(5, config.TimeoutSeconds))
                            };
                            _httpClients[serverId] = httpClient;

                            _runningServers[serverId] = new MCPServerProcess(
                                serverId,
                                config,
                                DateTime.UtcNow,
                                true,
                                config.Url);

                            var label = config.Label ?? config.Url;
                            BattleLuckLogger.Log($"✓ MCP remote server '{serverId}' registered ({label})");
                        }
                        else
                        {
                            BattleLuckLogger.Warning($"MCP server '{serverId}' uses unsupported transport '{transport}'. Only http/sse are supported.");
                        }
                    }
                    catch (Exception ex)
                    {
                        LastError = $"Failed to start MCP server '{serverId}': {ex.Message}";
                        BattleLuckLogger.Warning(LastError);
                    }
                }

                if (_runningServers.Count > 0)
                {
                    BattleLuckLogger.Log($"MCP runtime initialized with {_runningServers.Count} server(s)");
                }
            }
            catch (Exception ex)
            {
                LastError = $"MCP runtime initialization failed: {ex.Message}";
                BattleLuckLogger.Error(LastError);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Calls a tool on a remote MCP server over HTTP.
        /// </summary>
        /// <param name="serverId">The identifier of the registered MCP server.</param>
        /// <param name="toolName">The name of the tool to invoke.</param>
        /// <param name="args">Optional arguments to pass to the tool.</param>
        /// <returns>The tool result text, or <c>null</c> on failure.</returns>
        public async Task<MCPToolResult?> CallToolAsync(string serverId, string toolName, Dictionary<string, object>? args = null)
        {
            if (!_runningServers.TryGetValue(serverId, out var serverProcess))
            {
                LastError = $"MCP server '{serverId}' is not running";
                return null;
            }

            if (serverProcess.IsRemote && _httpClients.TryGetValue(serverId, out var httpClient))
                return await CallToolHttpAsync(httpClient, toolName, args, serverProcess.Config.TimeoutSeconds);

            LastError = $"MCP server '{serverId}' is not a remote HTTP server";
            return null;
        }

        private async Task<MCPToolResult?> CallToolHttpAsync(HttpClient client, string toolName, Dictionary<string, object>? args, int timeoutSeconds)
        {
            const int maxAttempts = 3;
            var attempt = 0;
            while (true)
            {
                attempt++;
                try
                {
                    var request = new MCPToolRequest
                    {
                        JsonRpc = "2.0",
                        Id = Guid.NewGuid().ToString(),
                        Method = "tools/call",
                        Params = new MCPToolCallParams { Name = toolName, Arguments = args ?? new() }
                    };
                    var json = JsonSerializer.Serialize(request, _jsonOptions);
                    using var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                    var response = await client.PostAsync("tools/call", content, cts.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        var msg = $"HTTP tool call failed: {(int)response.StatusCode} {response.ReasonPhrase}";
                        if (IsTransientFailure(response, attempt, maxAttempts, msg, client.BaseAddress))
                            continue;
                        return null;
                    }
                    var body = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<MCPToolResponse>(body, _jsonOptions);
                    var text = result?.Result?.Content?
                        .Where(c => c.Type == "text")
                        .Select(c => c.Text)
                        .FirstOrDefault();
                    return text != null ? new MCPToolResult { Text = text } : null;
                }
                catch (Exception ex)
                {
                    var msg = $"HTTP tool call exception: {ex.Message}";
                    if (IsTransientFailure(ex, attempt, maxAttempts, msg, client.BaseAddress))
                        continue;
                    return null;
                }
            }
        }

        private bool IsTransientFailure(HttpResponseMessage response, int attempt, int maxAttempts, string message, Uri? baseAddress)
        {
            var status = (int)response.StatusCode;
            if (status == 408 || status == 429 || status >= 500)
            {
                if (attempt < maxAttempts)
                {
                    BattleLuckLogger.Warning($"[MCP] {message} (server={baseAddress}); retrying ({attempt}/{maxAttempts})");
                    return true;
                }
            }
            LastError = message;
            BattleLuckLogger.Warning($"[MCP] {message} (server={baseAddress})");
            return false;
        }

        private bool IsTransientFailure(Exception ex, int attempt, int maxAttempts, string message, Uri? baseAddress)
        {
            if (ex is TaskCanceledException || ex is System.Net.Http.HttpRequestException)
            {
                if (attempt < maxAttempts)
                {
                    BattleLuckLogger.Warning($"[MCP] {message} (server={baseAddress}); retrying ({attempt}/{maxAttempts})");
                    Task.Delay(TimeSpan.FromMilliseconds(500 * attempt)).GetAwaiter().GetResult();
                    return true;
                }
            }
            LastError = message;
            BattleLuckLogger.Warning($"[MCP] {message} (server={baseAddress})");
            return false;
        }

        /// <summary>
        /// Lists the available tools from a remote MCP server.
        /// </summary>
        /// <param name="serverId">The identifier of the registered MCP server.</param>
        /// <returns>A list of tool descriptions, or <c>null</c> if the server is not available.</returns>
        public async Task<List<MCPToolInfo>?> ListToolsAsync(string serverId)
        {
            if (!_runningServers.TryGetValue(serverId, out var serverProcess))
            {
                return null;
            }

            if (serverProcess.IsRemote && _httpClients.TryGetValue(serverId, out var httpClient))
                return await ListToolsHttpAsync(httpClient, serverProcess.Config.TimeoutSeconds);

            return null;
        }

        private async Task<List<MCPToolInfo>?> ListToolsHttpAsync(HttpClient client, int timeoutSeconds)
        {
            const int maxAttempts = 3;
            var attempt = 0;
            while (true)
            {
                attempt++;
                try
                {
                    var request = new MCPListToolsRequest
                    {
                        JsonRpc = "2.0",
                        Id = Guid.NewGuid().ToString(),
                        Method = "tools/list",
                    };
                    var json = JsonSerializer.Serialize(request, _jsonOptions);
                    using var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                    var response = await client.PostAsync("tools/list", content, cts.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        var msg = $"HTTP tools/list failed: {(int)response.StatusCode} {response.ReasonPhrase}";
                        if (IsTransientFailure(response, attempt, maxAttempts, msg, client.BaseAddress))
                            continue;
                        return null;
                    }
                    var body = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<MCPListToolsResponse>(body, _jsonOptions);
                    return result?.Result?.Tools ?? new();
                }
                catch (Exception ex)
                {
                    var msg = $"HTTP tools/list exception: {ex.Message}";
                    if (IsTransientFailure(ex, attempt, maxAttempts, msg, client.BaseAddress))
                        continue;
                    return null;
                }
            }
        }

        /// <summary>
        /// Releases all managed and unmanaged resources held by the runtime.
        /// </summary>
        public void Dispose()
        {
            foreach (var httpClient in _httpClients.Values)
            {
                try { httpClient?.Dispose(); } catch { }
            }
            _runningServers.Clear();
            _httpClients.Clear();
        }
    }

    /// <summary>
    /// Represents a registered MCP server process or remote endpoint.
    /// </summary>
    public sealed class MCPServerProcess
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MCPServerProcess"/> class.
        /// </summary>
        /// <param name="serverId">The unique identifier for this server.</param>
        /// <param name="config">The MCP server configuration.</param>
        /// <param name="startedUtc">The UTC timestamp when the server was registered.</param>
        /// <param name="isRemote">Whether this server is a remote HTTP/SSE endpoint.</param>
        /// <param name="remoteUrl">The remote URL, if applicable.</param>
        public MCPServerProcess(string serverId, MCPServerConfig config, DateTime startedUtc, bool isRemote, string? remoteUrl)
        {
            ServerId = serverId;
            Config = config;
            StartedUtc = startedUtc;
            IsRemote = isRemote;
            RemoteUrl = remoteUrl;
        }

        /// <summary>
        /// Gets the unique identifier for this MCP server.
        /// </summary>
        public string ServerId { get; }

        /// <summary>
        /// Gets the server configuration settings.
        /// </summary>
        public MCPServerConfig Config { get; }

        /// <summary>
        /// Gets the UTC timestamp when this server was registered or started.
        /// </summary>
        public DateTime StartedUtc { get; }

        /// <summary>
        /// Gets a value indicating whether this server is a remote HTTP/SSE endpoint.
        /// </summary>
        public bool IsRemote { get; }

        /// <summary>
        /// Gets the remote URL, if this is a remote server.
        /// </summary>
        public string? RemoteUrl { get; }
    }

    public class MCPToolRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("method")]
        public string Method { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("params")]
        public MCPToolCallParams? Params { get; set; }
    }

    public class MCPToolCallParams
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("arguments")]
        public Dictionary<string, object> Arguments { get; set; } = new();
    }

    public class MCPToolResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("result")]
        public MCPToolResultWrapper? Result { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("error")]
        public MCPError? Error { get; set; }
    }

    public class MCPToolResultWrapper
    {
        [System.Text.Json.Serialization.JsonPropertyName("content")]
        public List<MCPContentItem> Content { get; set; } = new();
    }

    public class MCPContentItem
    {
        [System.Text.Json.Serialization.JsonPropertyName("type")]
        public string Type { get; set; } = "text";

        [System.Text.Json.Serialization.JsonPropertyName("text")]
        public string Text { get; set; } = "";
    }

    public class MCPToolResult
    {
        public string Text { get; set; } = "";
    }

    public class MCPError
    {
        [System.Text.Json.Serialization.JsonPropertyName("code")]
        public int Code { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string Message { get; set; } = "";
    }

    public class MCPListToolsRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("method")]
        public string Method { get; set; } = "";
    }

    public class MCPListToolsResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("result")]
        public MCPToolsList? Result { get; set; }
    }

    public class MCPToolsList
    {
        [System.Text.Json.Serialization.JsonPropertyName("tools")]
        public List<MCPToolInfo> Tools { get; set; } = new();
    }

    public class MCPToolInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("inputSchema")]
        public Dictionary<string, object> InputSchema { get; set; } = new();
    }
}
