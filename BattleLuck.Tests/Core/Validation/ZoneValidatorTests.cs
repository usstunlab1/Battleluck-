using BattleLuck.Core.Validation;
using BattleLuck.Models;
using FluentAssertions;
using Xunit;

namespace BattleLuck.Tests.Core.Validation;

public class ZoneValidatorTests
{
    [Fact]
    public void Validate_ReturnsIssues_WhenRulesAreInvalid()
    {
        // Arrange
        var config = new ModeConfig
        {
            Rules = new RulesConfig
            {
                MaxDeathsPerParticipant = 0, // Invalid: < 1
                SafetyMode = "invalid",
                SpawnRateLimitPerSecond = 0 // Invalid: <= 0
            }
        };

        // Act
        var issues = ZoneValidator.Validate("event_test", config);

        // Assert
        issues.Should().Contain(i => i.Contains("MaxDeathsPerParticipant must be between 1 and 10"));
        issues.Should().Contain(i => i.Contains("is an event but SafetyMode is not 'event_tracked_zone_only'"));
        issues.Should().Contain(i => i.Contains("must have a SpawnRateLimitPerSecond > 0"));
    }

    [Fact]
    public void Validate_ReturnsIssues_WhenZonesAreInvalid()
    {
        // Arrange
        var config = new ModeConfig
        {
            Zones = new ZonesConfig
            {
                Zones = new List<ZoneDefinition>
                {
                    new ZoneDefinition { Name = "Zone1", Hash = 0, Radius = -1 },
                    new ZoneDefinition { Name = "Zone2", Hash = 1, Radius = 10, ExitRadius = 5 } // exitRadius < radius
                }
            }
        };

        // Act
        var issues = ZoneValidator.Validate("test_mode", config);

        // Assert
        issues.Should().Contain(i => i.Contains("Zone 'Zone1' has hash=0"));
        issues.Should().Contain(i => i.Contains("Zone 'Zone1' has non-positive radius"));
        issues.Should().Contain(i => i.Contains("Zone 'Zone2' has exitRadius < radius"));
    }

    [Fact]
    public void Validate_ReturnsIssues_WhenDuplicateHashesExist()
    {
        // Arrange
        var config = new ModeConfig
        {
            Zones = new ZonesConfig
            {
                Zones = new List<ZoneDefinition>
                {
                    new ZoneDefinition { Name = "Zone1", Hash = 123, Radius = 10 },
                    new ZoneDefinition { Name = "Zone2", Hash = 123, Radius = 10 }
                }
            }
        };

        // Act
        var issues = ZoneValidator.Validate("test_mode", config);

        // Assert
        issues.Should().Contain(i => i.Contains("Duplicate zone hash '123'"));
    }

    [Fact]
    public void Validate_ReturnsIssues_WhenAiRulesAreInvalid()
    {
        // Arrange
        var config = new ModeConfig
        {
            Zones = new ZonesConfig
            {
                Zones = new List<ZoneDefinition>
                {
                    new ZoneDefinition
                    {
                        Name = "AiZone",
                        Hash = 1,
                        Radius = 10,
                        AiRules = new ZoneAiRules
                        {
                            AllowAutonomousExecution = true,
                            AllowedActions = new List<string>() // Empty list
                        }
                    }
                }
            }
        };

        // Act
        var issues = ZoneValidator.Validate("test_mode", config);

        // Assert
        issues.Should().Contain(i => i.Contains("has AI rules with autonomous execution but no allowedActions list"));
    }

    [Fact]
    public void Validate_ReturnsNoIssues_WhenConfigIsValid()
    {
        // Arrange
        var config = new ModeConfig
        {
            Rules = new RulesConfig
            {
                MaxDeathsPerParticipant = 3,
                SafetyMode = "event_tracked_zone_only",
                SpawnRateLimitPerSecond = 10
            },
            Zones = new ZonesConfig
            {
                Zones = new List<ZoneDefinition>
                {
                    new ZoneDefinition
                    {
                        Name = "ValidZone",
                        Hash = 456,
                        Radius = 50,
                        ExitRadius = 60
                    }
                }
            }
        };

        // Act
        var issues = ZoneValidator.Validate("event_test", config);

        // Assert
        issues.Should().BeEmpty();
    }
}