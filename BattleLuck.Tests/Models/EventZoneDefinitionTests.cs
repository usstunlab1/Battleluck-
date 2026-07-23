using BattleLuck.Models;

namespace BattleLuck.Tests.Models;

public sealed class EventZoneDefinitionTests
{
    [Fact]
    public void Unified_zone_center_populates_both_runtime_center_fields()
    {
        var source = new EventZoneDefinition
        {
            Name = "Arena",
            Hash = 42,
            Center = new Vec3Config { X = 10, Y = 2, Z = -5 }
        };

        var projected = source.ToZoneDefinition();

        Assert.Equal(10, projected.Position.X);
        Assert.Equal(2, projected.Position.Y);
        Assert.Equal(-5, projected.Position.Z);
        Assert.Equal(projected.Position.X, projected.Center.X);
        Assert.Equal(projected.Position.Y, projected.Center.Y);
        Assert.Equal(projected.Position.Z, projected.Center.Z);
    }
}
