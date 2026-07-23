using BattleLuck.Core.Validation;
using BattleLuck.Models;
using FluentAssertions;
using Xunit;

namespace BattleLuck.Tests.Core.Validation;

public class KitValidatorTests
{
    [Fact]
    public void Validate_ReturnsIssues_WhenPrefabReferenceIsZero()
    {
        // Arrange
        var config = new ModeConfig
        {
            KitConfig = new KitConfig
            {
                Weapons = new List<WeaponConfig> { new WeaponConfig { Prefab = "0" } },
                Items = new List<ItemConfig> { new ItemConfig { Prefab = "0" } }
            }
        };

        // Act
        var issues = KitValidator.Validate("test_mode", config);

        // Assert
        issues.Should().Contain(i => i.Contains("Invalid weapon prefab '0'"));
        issues.Should().Contain(i => i.Contains("Invalid item prefab '0'"));
    }

    [Fact]
    public void Validate_ReturnsIssues_WhenArmorPrefabIsZero()
    {
        // Arrange
        var config = new ModeConfig
        {
            KitConfig = new KitConfig
            {
                Armors = new ArmorsConfig
                {
                    Chest = "0",
                    Legs = "0"
                }
            }
        };

        // Act
        var issues = KitValidator.Validate("test_mode", config);

        // Assert
        issues.Should().Contain(i => i.Contains("Invalid armor prefab '0' for slot 'chest'"));
        issues.Should().Contain(i => i.Contains("Invalid armor prefab '0' for slot 'legs'"));
    }

    [Fact]
    public void Validate_ReturnsNoIssues_WhenPrefabsAreIntegers()
    {
        // Arrange
        var config = new ModeConfig
        {
            KitConfig = new KitConfig
            {
                Weapons = new List<WeaponConfig> { new WeaponConfig { Prefab = "12345" } },
                Armors = new ArmorsConfig { Chest = "67890" }
            }
        };

        // Act
        var issues = KitValidator.Validate("test_mode", config);

        // Assert
        // This should pass the TryParse int check (guidHash != 0)
        issues.Should().NotContain(i => i.Contains("Invalid weapon prefab '12345'"));
        issues.Should().NotContain(i => i.Contains("Invalid armor prefab '67890'"));
    }

    [Fact]
    public void Validate_ReturnsIssues_WhenPrefabsAreEmpty()
    {
        // Arrange
        var config = new ModeConfig
        {
            KitConfig = new KitConfig
            {
                Weapons = new List<WeaponConfig> { new WeaponConfig { Prefab = "" } }
            }
        };

        // Act
        var issues = KitValidator.Validate("test_mode", config);

        // Assert
        issues.Should().Contain(i => i.Contains("Invalid weapon prefab ''"));
    }

    [Fact]
    public void Validate_DefersNamedPrefabs_UntilLiveWorldValidation()
    {
        var config = new ModeConfig
        {
            KitConfig = new KitConfig
            {
                Weapons = new List<WeaponConfig>
                {
                    new WeaponConfig { Prefab = "Item_Weapon_Sword_T08_Sanguine" }
                },
                Armors = new ArmorsConfig
                {
                    Chest = "Item_Chest_T08_DarkSilver_Rogue"
                }
            }
        };

        var issues = KitValidator.Validate("test_mode", config);

        issues.Should().BeEmpty();
    }
}
