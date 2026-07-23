using System.Text;
using System.Text.Json;
using BattleLuck.Services.Chat;

namespace BattleLuck.Tests.Services;

public sealed class ZuiPacketPresenterTests
{
    [Fact]
    public void TrySend_RequiresOptIn()
    {
        var packets = new List<string>();
        var presenter = new ZuiPacketPresenter((_, packet) => packets.Add(packet));

        var sent = presenter.TrySend(7, BattleLuckZuiDashboard.BuildHome());

        Assert.False(sent);
        Assert.Empty(packets);
    }

    [Fact]
    public void TrySend_EmitsAValidBoundedWindowPacket()
    {
        var packets = new List<string>();
        var presenter = new ZuiPacketPresenter((_, packet) => packets.Add(packet));
        presenter.Enable(7);

        var sent = presenter.TrySend(7, BattleLuckZuiDashboard.BuildHome());

        Assert.True(sent);
        var packet = Assert.Single(packets);
        Assert.StartsWith("[[ZUI]]", packet);
        Assert.InRange(Encoding.UTF8.GetByteCount(packet), 1, NotificationHelper.MaxSystemMessageUtf8Bytes);
        using var payload = JsonDocument.Parse(packet["[[ZUI]]".Length..]);
        Assert.Equal("battleluck.home", payload.RootElement.GetProperty("id").GetString());
        Assert.Equal(5, payload.RootElement.GetProperty("buttons").GetArrayLength());
    }

    [Fact]
    public void BuildSection_UsesOnlyServerCommands()
    {
        var window = BattleLuckZuiDashboard.BuildSection("audit");

        Assert.All(window.Buttons, button => Assert.StartsWith(".ai", button.Command, StringComparison.OrdinalIgnoreCase));
    }
}
