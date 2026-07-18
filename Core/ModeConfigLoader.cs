/// <summary>
/// Mode configuration loader for kit and mode config paths.
/// Provides centralized access to configuration directories.
/// </summary>
public static class ModeConfigLoader
{
    /// <summary>
    /// Root directory for event files.
    /// </summary>
    public static string EventsRoot => System.IO.Path.Combine(ConfigLoader.ConfigRoot, "events");

    /// <summary>
    /// Root directory for kit.json files.
    /// </summary>
    public static string KitsRoot => EventsRoot;

    /// <summary>
    /// Ensure the file watcher is set up for mode config changes.
    /// </summary>
    public static void EnsureWatcher()
    {
        // No-op: ConfigLoader.EnsureDefaultsDeployed handles initial deployment.
        // File watching is handled by individual services that need it.
    }
}