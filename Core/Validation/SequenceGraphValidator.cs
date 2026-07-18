namespace BattleLuck.Core.Validation;

/// <summary>
/// Validates action catalog for missing references, duplicate IDs, and circular dependencies.
/// </summary>
public sealed class SequenceGraphValidator
{
    public static SequenceGraphValidator Instance { get; } = new();

    public EventValidationResult Validate(ActionCatalog catalog)
    {
        var result = new EventValidationResult();

        ValidateActions(catalog, result);
        ValidateSequences(catalog, result);
        ValidateCrossReferences(catalog, result);
        CheckCircularReferences(catalog, result);

        return result;
    }

    void ValidateActions(ActionCatalog catalog, EventValidationResult result)
    {
        if (catalog.Actions == null) return;

        var actionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in catalog.Actions)
        {
            if (string.IsNullOrWhiteSpace(action.ActionId) && string.IsNullOrWhiteSpace(action.Action))
                result.Errors.Add($"Action entry has no actionId or action.");
            
            if (!string.IsNullOrWhiteSpace(action.ActionId) && !actionIds.Add(action.ActionId))
                result.Errors.Add($"Duplicate actionId '{action.ActionId}'.");
        }
    }

    void ValidateSequences(ActionCatalog catalog, EventValidationResult result)
    {
        if (catalog.Sequences == null) return;

        var sequenceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sequence in catalog.Sequences)
        {
            if (string.IsNullOrWhiteSpace(sequence.SequenceId))
            {
                result.Errors.Add($"Sequence has empty sequenceId.");
                continue;
            }

            if (!sequenceIds.Add(sequence.SequenceId))
                result.Errors.Add($"Duplicate sequenceId '{sequence.SequenceId}'.");

            // Validate step IDs within each sequence
            if (sequence.Steps != null)
            {
                var stepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var step in sequence.Steps)
                {
                    if (string.IsNullOrWhiteSpace(step.Id))
                    {
                        result.Errors.Add($"Step in sequence '{sequence.SequenceId}' has empty id.");
                        continue;
                    }

                    if (!stepIds.Add(step.Id))
                        result.Errors.Add($"Sequence '{sequence.SequenceId}' has duplicate stepId '{step.Id}'.");

                    if (string.IsNullOrWhiteSpace(step.Action) && string.IsNullOrWhiteSpace(step.ActionId))
                        result.Errors.Add($"Step '{step.Id}' in sequence '{sequence.SequenceId}' is missing both actionId and action.");
                }
            }
        }
    }

    void ValidateCrossReferences(ActionCatalog catalog, EventValidationResult result)
    {
        if (catalog.Sequences == null) return;

        foreach (var sequence in catalog.Sequences)
        {
            if (sequence.Steps == null) continue;

            foreach (var step in sequence.Steps)
            {
                // Check if actionId references another sequence (for sequence.step.run actions)
                if (!string.IsNullOrWhiteSpace(step.ActionId))
                {
                    bool isSequenceReference = step.Action?.Equals("sequence.step.run", StringComparison.OrdinalIgnoreCase) == true;
                    if (isSequenceReference && catalog.Sequences != null)
                    {
                        bool exists = catalog.Sequences.Any(s => 
                            s.SequenceId.Equals(step.ActionId, StringComparison.OrdinalIgnoreCase));
                        if (!exists)
                            result.Warnings.Add($"Step '{step.Id}' references unknown sequence '{step.ActionId}'.");
                    }
                }
            }
        }
    }

    void CheckCircularReferences(ActionCatalog catalog, EventValidationResult result)
    {
        if (catalog.Sequences == null) return;

        var visited = new HashSet<string>();
        var inStack = new HashSet<string>();

        foreach (var sequence in catalog.Sequences)
        {
            if (string.IsNullOrWhiteSpace(sequence.SequenceId)) continue;

            visited.Clear();
            inStack.Clear();
            CheckSequenceRecursive(catalog, sequence.SequenceId, visited, inStack, result);
        }
    }

    void CheckSequenceRecursive(ActionCatalog catalog, string sequenceId, HashSet<string> visited, HashSet<string> inStack, EventValidationResult result)
    {
        if (inStack.Contains(sequenceId))
        {
            result.Errors.Add($"Circular reference detected involving sequence '{sequenceId}'.");
            return;
        }

        if (visited.Contains(sequenceId)) return;

        visited.Add(sequenceId);
        inStack.Add(sequenceId);

        var sequence = catalog.Sequences.FirstOrDefault(s => s.SequenceId.Equals(sequenceId, StringComparison.OrdinalIgnoreCase));
        if (sequence?.Steps == null)
        {
            inStack.Remove(sequenceId);
            return;
        }

        foreach (var step in sequence.Steps)
        {
            if (step.Action?.Equals("sequence.step.run", StringComparison.OrdinalIgnoreCase) == true &&
                !string.IsNullOrWhiteSpace(step.ActionId))
            {
                CheckSequenceRecursive(catalog, step.ActionId, visited, inStack, result);
            }
        }

        inStack.Remove(sequenceId);
    }
}