namespace BattleLuck.Models.Chat;

/// <summary>
/// Represents the BattleLuck-managed chat channel state.
/// Native means the player uses their client's selected Global/Local/Clan/Whisper channel.
/// AI means the player is in the BattleLuck AI channel.
/// </summary>
public enum BattleLuckChatChannel
{
    Native,
    AI
}