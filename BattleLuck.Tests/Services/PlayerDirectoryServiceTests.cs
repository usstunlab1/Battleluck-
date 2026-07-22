using BattleLuck.Services.Runtime;

namespace BattleLuck.Tests.Services;

public sealed class PlayerDirectoryServiceTests
{
    [Theory]
    [InlineData("  Ahmad   Tallal ", "ahmad tallal")]
    [InlineData("PLAYER", "player")]
    public void Normalizes_names_deterministically(string input, string expected) =>
        Assert.Equal(expected, PlayerDirectoryService.NormalizeName(input));

    [Fact]
    public void Rejects_rich_text_and_reserved_renames()
    {
        Assert.False(PlayerDirectoryService.IsValidRename("<color=red>x", Array.Empty<string>(), out _));
        Assert.False(PlayerDirectoryService.IsValidRename("Admin", new[] { "admin" }, out _));
        Assert.True(PlayerDirectoryService.IsValidRename("Raven", new[] { "admin" }, out _));
    }
}
