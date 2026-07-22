using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ProjectM;
using ProjectM.Network;
using Unity.Entities;

namespace BattleLuck.Commands;

public sealed class BattleLuckCommandDispatcher
{
    static readonly Dictionary<string, CommandRegistration> _commands = new(StringComparer.OrdinalIgnoreCase);
    static readonly List<string> _aliases = new();
    static bool _scanned;

    public static void EnsureScanned()
    {
        if (_scanned) return;
        _scanned = true;

        var pluginType = typeof(BattleLuckPlugin);
        var assemblies = new[]
        {
            pluginType.Assembly,
            typeof(BattleLuckCommandAttribute).Assembly
        }.Distinct();

        foreach (var assembly in assemblies)
        {
            foreach (var type in GetLoadableTypes(assembly))
            {
                // Static command containers are abstract+sealed in CLR metadata.
                // Exclude only true abstract base classes, not static classes.
                if (!CommandDiscoveryRules.IsCommandContainer(type))
                    continue;

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    var attr = method.GetCustomAttribute<BattleLuckCommandAttribute>();
                    if (attr == null) continue;
                    // BattleLuck exposes one native command: .ai <request>.
                    // Legacy .bl and .ai.dev routes are deliberately not registered.
                    if (!attr.Name.Equals("ai", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (method.ReturnType != typeof(void) &&
                        method.ReturnType != typeof(OperationResult) &&
                        !typeof(Task).IsAssignableFrom(method.ReturnType))
                    {
                        BattleLuckPlugin.LogWarning($"[CmdDispatcher] Skipping {type.Name}.{method.Name}: unsupported return type {method.ReturnType}");
                        continue;
                    }

                    var parameters = method.GetParameters();
                    if (parameters.Length == 0 || parameters[0].ParameterType != typeof(BattleLuckCommandContext))
                    {
                        BattleLuckPlugin.LogWarning($"[CmdDispatcher] Skipping {type.Name}.{method.Name}: first parameter must be BattleLuckCommandContext");
                        continue;
                    }

                    var cmd = new CommandRegistration
                    {
                        Name = attr.Name,
                        Description = attr.Description,
                        AdminOnly = attr.AdminOnly,
                        Method = method,
                        DeclaringType = type,
                        ParameterTypes = parameters.Skip(1).Select(p => p.ParameterType).ToArray(),
                        ParameterNames = parameters.Skip(1).Select(p => p.Name ?? string.Empty).ToArray()
                    };

                    _commands[attr.Name] = cmd;
                    if (!_aliases.Contains(attr.Name))
                        _aliases.Add(attr.Name);
                }
            }
        }

        BattleLuckPlugin.LogInfo($"[CmdDispatcher] Registered {_commands.Count} BattleLuck commands: {string.Join(", ", _aliases.OrderBy(s => s))}");
    }

    static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            BattleLuckPlugin.LogWarning(
                $"[CmdDispatcher] Skipped {ex.Types.Count(type => type == null)} type(s) with unavailable optional interop dependencies.");
            return ex.Types.OfType<Type>();
        }
    }

    public static bool TryDispatch(string rawInput, Entity senderEntity, ulong steamId, bool isAdmin, bool isConsole)
    {
        EnsureScanned();

        var trimmed = (rawInput ?? "").Trim();
        if (trimmed.Length == 0) return false;
        if (HasCommandPrefix(trimmed))
            trimmed = trimmed[1..];

        if (!TryResolveCommand(trimmed, out var cmdName, out var cmd, out var args))
            return false;

        if (cmd.AdminOnly && !isAdmin)
        {
            Notify(senderEntity, steamId, "🚫 This command requires admin privileges.", isConsole);
            return true;
        }

        var ctx = new BattleLuckCommandContext(senderEntity, steamId, rawInput ?? "", args, isAdmin, isConsole);
        var parsedArgs = ParseArgs(args, cmd.ParameterTypes);

        try
        {
            var result = cmd.Method.Invoke(null, new object[] { ctx }.Concat(parsedArgs.Cast<object?>().ToArray()).ToArray());
            if (result is Task task)
            {
                _ = ObserveCommandTaskAsync(task, cmdName, senderEntity, steamId, isConsole);
            }
            else if (result is OperationResult opResult && !opResult.Success)
            {
                Notify(senderEntity, steamId, opResult.UserMessage, isConsole);
            }
        }
        catch (TargetInvocationException tie)
        {
            BattleLuckPlugin.ErrorReporter.Report(tie.InnerException ?? tie,
                new BattleLuck.Models.ErrorReportContext { AdminSteamId = steamId, Command = cmdName });
            BattleLuckPlugin.LogWarning($"[CmdDispatcher] {cmdName} failed: {tie.InnerException?.Message ?? tie.Message}");
            Notify(senderEntity, steamId, $"❌ Command failed: {tie.InnerException?.Message ?? tie.Message}", isConsole);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.ErrorReporter.Report(ex,
                new BattleLuck.Models.ErrorReportContext { AdminSteamId = steamId, Command = cmdName });
            BattleLuckPlugin.LogWarning($"[CmdDispatcher] {cmdName} failed: {ex.Message}");
            Notify(senderEntity, steamId, $"❌ Command failed: {ex.Message}", isConsole);
        }

        return true;
    }

    public static bool TryDispatchFromChatEvent(Entity eventEntity, ChatMessageEvent chatEvent)
    {
        if (!VRisingCore.IsReady)
            return false;

        var message = chatEvent.MessageText.ToString();
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var trimmed = message.Trim();
        if (!HasCommandPrefix(trimmed))
            return false;

        if (!eventEntity.TryGetComponent(out FromCharacter fromCharacter) ||
            !fromCharacter.User.TryGetComponent(out User user))
            return false;

        var handled = TryDispatch(message, fromCharacter.Character, user.PlatformId, user.IsAdmin, isConsole: false);
        if (handled && eventEntity.Has<ChatMessageEvent>())
        {
            // Consume the chat event so VCF / the game chat system does not
            // also react to the dot-command (avoids "command not found" noise).
            VRisingCore.EntityManager.RemoveComponent<ChatMessageEvent>(eventEntity);
        }

        return handled;
    }

    public static IReadOnlyList<string> RegisteredCommands => _aliases;

    static async Task ObserveCommandTaskAsync(Task task, string commandName, Entity senderEntity, ulong steamId, bool isConsole)
    {
        try
        {
            await task.ConfigureAwait(false);

            var resultProperty = task.GetType().GetProperty("Result");
            if (resultProperty?.GetValue(task) is OperationResult result && !result.Success)
                Notify(senderEntity, steamId, result.UserMessage, isConsole);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.ErrorReporter.Report(ex,
                new BattleLuck.Models.ErrorReportContext { AdminSteamId = steamId, Command = commandName });
            BattleLuckPlugin.LogWarning($"[CmdDispatcher] {commandName} failed: {ex.Message}");
            Notify(senderEntity, steamId, $"Command failed: {ex.Message}", isConsole);
        }
    }

    static bool HasCommandPrefix(string value) =>
        !string.IsNullOrEmpty(value) && value[0] == '.';

    static bool TryResolveCommand(
        string input,
        out string commandName,
        out CommandRegistration command,
        out string[] args)
    {
        foreach (var name in _commands.Keys.OrderByDescending(k => k.Length))
        {
            if (!input.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                !input.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            commandName = name;
            command = _commands[name];
            var rest = input.Length == name.Length ? string.Empty : input[name.Length..].Trim();
            args = string.IsNullOrEmpty(rest)
                ? Array.Empty<string>()
                : rest.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return true;
        }

        commandName = string.Empty;
        command = null!;
        args = Array.Empty<string>();
        return false;
    }

    static List<object?> ParseArgs(string[] args, Type[] parameterTypes)
    {
        var result = new List<object?>();
        for (var i = 0; i < parameterTypes.Length; i++)
        {
            var type = parameterTypes[i];
            var raw = i < args.Length ? args[i] : "";

            if (type == typeof(string))
            {
                result.Add(i == parameterTypes.Length - 1 && args.Length > i
                    ? string.Join(" ", args.Skip(i))
                    : raw);
                continue;
            }

            if (type == typeof(int) || type == typeof(int?))
            {
                if (int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var iVal))
                    result.Add(iVal);
                else
                    result.Add(type.IsValueType ? Activator.CreateInstance(type) : null);
                continue;
            }

            if (type == typeof(float) || type == typeof(float?))
            {
                if (float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fVal))
                    result.Add(fVal);
                else
                    result.Add(type.IsValueType ? Activator.CreateInstance(type) : null);
                continue;
            }

            if (type == typeof(bool) || type == typeof(bool?))
            {
                if (bool.TryParse(raw, out var bVal))
                    result.Add(bVal);
                else
                    result.Add(type.IsValueType ? Activator.CreateInstance(type) : null);
                continue;
            }

            if (type == typeof(ulong) || type == typeof(ulong?))
            {
                if (ulong.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var ulVal))
                    result.Add(ulVal);
                else
                    result.Add(type.IsValueType ? Activator.CreateInstance(type) : null);
                continue;
            }

            result.Add(null);
        }

        return result;
    }

    static void Notify(Entity entity, ulong steamId, string message, bool isConsole)
    {
        if (isConsole)
        {
            BattleLuckPlugin.LogInfo($"[Cmd] {message}");
            return;
        }

        try
        {
            if (!entity.Exists()) return;
            var userEntity = entity.Has<PlayerCharacter>()
                ? entity.Read<PlayerCharacter>().UserEntity
                : entity;
            if (userEntity.Exists())
            {
                var user = userEntity.Read<User>();
                NotificationHelper.NotifyPlayerRaw(user, message);
            }
        }
        catch { }
    }

    sealed class CommandRegistration
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public bool AdminOnly { get; set; }
        public MethodInfo Method { get; set; } = null!;
        public Type DeclaringType { get; set; } = null!;
        public Type[] ParameterTypes { get; set; } = Array.Empty<Type>();
        public string[] ParameterNames { get; set; } = Array.Empty<string>();
    }
}
