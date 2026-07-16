using BattleLuck.Models;
using System.Collections.Generic;

namespace BattleLuck.Core.Validation;

public sealed class TechValidator
{
    readonly Services.Runtime.TechCatalog _catalog;

    public TechValidator(Services.Runtime.TechCatalog catalog)
    {
        _catalog = catalog;
    }

    public (bool Valid, List<string> Errors) Validate(List<string> techIds)
    {
        var errors = new List<string>();

        if (techIds == null || techIds.Count == 0)
            return (true, errors);

        var resolver = new Services.Runtime.TechResolver(_catalog);
        var (success, state, error) = resolver.Resolve(techIds);

        if (!success)
            errors.Add(error ?? "Tech resolution failed.");

        return (errors.Count == 0, errors);
    }

    public static IReadOnlyList<string> Validate(ModeConfig config, Services.Runtime.TechCatalog catalog)
    {
        var issues = new List<string>();

        var techIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (config.Rules?.TechIds != null)
            foreach (var id in config.Rules.TechIds)
                if (!string.IsNullOrWhiteSpace(id))
                    techIds.Add(id);

        if (config.Session?.Rules?.TechIds != null)
            foreach (var id in config.Session.Rules.TechIds)
                if (!string.IsNullOrWhiteSpace(id))
                    techIds.Add(id);

        if (techIds.Count == 0)
            return issues;

        var validator = new TechValidator(catalog);
        var (valid, errors) = validator.Validate(techIds.ToList());
        issues.AddRange(errors);

        return issues;
    }
}