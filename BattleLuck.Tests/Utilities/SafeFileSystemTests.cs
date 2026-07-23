using BattleLuck.Utilities;

namespace BattleLuck.Tests.Utilities;

public sealed class SafeFileSystemTests
{
    [Theory]
    [InlineData("bloodbath")]
    [InlineData("event_01")]
    [InlineData("arena-test")]
    public void SafeIdentifiers_AreAccepted(string value)
    {
        Assert.Equal(value, SafeFileSystem.RequireSafeIdentifier(value, "modeId"));
    }

    [Theory]
    [InlineData("../secrets")]
    [InlineData("..\\secrets")]
    [InlineData("event.json")]
    [InlineData("")]
    public void UnsafeIdentifiers_AreRejected(string value)
    {
        Assert.Throws<ArgumentException>(
            () => SafeFileSystem.RequireSafeIdentifier(value, "modeId"));
    }

    [Fact]
    public void AtomicWrite_ReplacesCompleteContent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"battleluck-atomic-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "state.json");
        try
        {
            SafeFileSystem.WriteAllTextAtomic(path, "first");
            SafeFileSystem.WriteAllTextAtomic(path, "second");

            Assert.Equal("second", File.ReadAllText(path));
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
