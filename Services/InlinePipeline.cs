/// <summary>
/// Minimal inline pipeline replacing MutationPipeline.
/// Executes steps sequentially; stops and records failure on first exception.
/// </summary>
public static class InlinePipeline
{
    public static InlinePipelineResult Run(string name, Action<InlinePipelineBuilder> configure)
    {
        var builder = new InlinePipelineBuilder();
        configure(builder);
        return builder.Execute();
    }
}

public sealed class InlinePipelineBuilder
{
    readonly List<(string Name, Action Action)> _steps = new();

    public void Step(string name, Action action) => _steps.Add((name, action));

    public InlinePipelineResult Execute()
    {
        var completed = new List<string>();
        foreach (var (name, action) in _steps)
        {
            try
            {
                action();
                completed.Add(name);
            }
            catch (Exception ex)
            {
                return new InlinePipelineResult(false, name, ex.Message, completed);
            }
        }
        return new InlinePipelineResult(true, null, null, completed);
    }
}

public sealed class InlinePipelineResult
{
    public bool Success { get; }
    public string? FailedStep { get; }
    public string? Error { get; }
    public IReadOnlyList<string> Steps { get; }

    public InlinePipelineResult(bool success, string? failedStep, string? error, List<string> steps)
    {
        Success = success;
        FailedStep = failedStep;
        Error = error;
        Steps = steps;
    }
}
