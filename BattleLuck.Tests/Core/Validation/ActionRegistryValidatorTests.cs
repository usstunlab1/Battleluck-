using BattleLuck.Core.Validation;
using BattleLuck.Models;
using FluentAssertions;
using Xunit;
using System.Text.Json;

namespace BattleLuck.Tests.Core.Validation;

public class ActionRegistryValidatorTests : IDisposable
{
    private readonly string _tempConfigDir;

    public ActionRegistryValidatorTests()
    {
        _tempConfigDir = Path.Combine(Path.GetTempPath(), "BattleLuckTests_" + Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempConfigDir);
        ConfigLoader.ConfigRoot = _tempConfigDir;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempConfigDir))
        {
            Directory.Delete(_tempConfigDir, true);
        }
    }

    [Fact]
    public void Validate_ReturnsIssues_WhenActionIsUnknown()
    {
        // Arrange
        var catalog = new
        {
            registered = new[] { "spawn_unit", "give_item" }
        };
        File.WriteAllText(Path.Combine(_tempConfigDir, "actions_catalog.json"), JsonSerializer.Serialize(catalog));

        var config = new ModeConfig
        {
            FlowEnter = new FlowConfig
            {
                Flows = new Dictionary<string, FlowDefinition>
                {
                    ["start"] = new FlowDefinition
                    {
                        Actions = new List<string> { "unknown_action", "spawn_unit" }
                    }
                }
            }
        };

        // Act
        var validator = new ActionRegistryValidator();
        var issues = validator.Validate("test_mode", config);

        // Assert
        issues.Should().ContainSingle().Which.Should().Contain("Unknown action 'unknown_action'");
    }

    [Fact]
    public void Validate_ReturnsNoIssues_WhenAllActionsAreRegistered()
    {
        // Arrange
        var catalog = new
        {
            registered = new[] { "spawn_unit", "give_item" }
        };
        File.WriteAllText(Path.Combine(_tempConfigDir, "actions_catalog.json"), JsonSerializer.Serialize(catalog));

        var config = new ModeConfig
        {
            Session = new SessionConfig
            {
                Flow = new SessionFlowConfig
                {
                    Start = new FlowConfig
                    {
                        Flows = new Dictionary<string, FlowDefinition>
                        {
                            ["phase1"] = new FlowDefinition
                            {
                                Actions = new List<string> { "spawn_unit:Archer", "give_item" }
                            }
                        }
                    }
                }
            }
        };

        // Act
        var validator = new ActionRegistryValidator();
        var issues = validator.Validate("test_mode", config);

        // Assert
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ReturnsEmpty_WhenCatalogMissing()
    {
        // Arrange
        // No catalog file created
        var config = new ModeConfig();

        // Act
        var validator = new ActionRegistryValidator();
        var issues = validator.Validate("test_mode", config);

        // Assert
        issues.Should().BeEmpty();
    }
}
