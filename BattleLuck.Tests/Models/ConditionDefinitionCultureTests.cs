using BattleLuck.Models;
using FluentAssertions;
using System.Globalization;

namespace BattleLuck.Tests.Models;

public class ConditionDefinitionCultureTests
{
    [Fact]
    public void Evaluate_UsesInvariantNumbersUnderCommaDecimalCulture()
    {
        using var culture = new TemporaryCulture("fr-FR");
        var condition = new ConditionDefinition
        {
            Left = "score",
            Operator = "greaterThan",
            Right = "1.5"
        };

        condition.Evaluate(new Dictionary<string, object> { ["score"] = 2.0 })
            .Should().BeTrue();
    }

    private sealed class TemporaryCulture : IDisposable
    {
        private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

        public TemporaryCulture(string name)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _originalCulture;
            CultureInfo.CurrentUICulture = _originalUiCulture;
        }
    }
}
