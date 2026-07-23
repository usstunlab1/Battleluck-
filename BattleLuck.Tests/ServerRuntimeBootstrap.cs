using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Xunit.Sdk;

namespace BattleLuck.Tests;

internal static class ServerRuntimeBootstrap
{
    static string? _interopPath;

    [ModuleInitializer]
    internal static void Initialize()
    {
        _interopPath = FindInteropPath();
        if (_interopPath == null)
            return;

        AssemblyLoadContext.Default.Resolving += ResolveServerAssembly;
    }

    internal static bool IsAvailable => _interopPath != null;

    internal static void RequireServerRuntime()
    {
        if (!IsAvailable)
            throw new SkipException(
                "A real V Rising BepInEx/interop directory is required for this integration test.");
    }

    static Assembly? ResolveServerAssembly(AssemblyLoadContext context, AssemblyName name)
    {
        if (_interopPath == null || string.IsNullOrWhiteSpace(name.Name))
            return null;

        var candidate = Path.Combine(_interopPath, name.Name + ".dll");
        return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
    }

    static string? FindInteropPath()
    {
        var directPath = Environment.GetEnvironmentVariable("BATTLELUCK_TEST_INTEROP_PATH");
        var configuredRoot = Environment.GetEnvironmentVariable("BATTLELUCK_SERVER_ROOT");
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var repositoryRoot = FindRepositoryRoot();
        var candidates = new List<string?>
        {
            directPath,
            string.IsNullOrWhiteSpace(configuredRoot) ? null : Path.Combine(configuredRoot, "BepInEx", "interop"),
            string.IsNullOrWhiteSpace(desktop) ? null : Path.Combine(desktop, "DedicatedServerLauncher", "VRisingServer", "BepInEx", "interop"),
            string.IsNullOrWhiteSpace(repositoryRoot) ? null : Path.Combine(
                Directory.GetParent(repositoryRoot)?.FullName ?? string.Empty,
                "DedicatedServerLauncher",
                "VRisingServer",
                "BepInEx",
                "interop")
        };

        return candidates.FirstOrDefault(path =>
            !string.IsNullOrWhiteSpace(path) &&
            File.Exists(Path.Combine(path, "Unity.Entities.dll")) &&
            File.Exists(Path.Combine(path, "ProjectM.dll")));
    }

    static string? FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "packages.lock.json")) &&
                File.Exists(Path.Combine(current.FullName, "BattleLuck.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        return null;
    }
}
