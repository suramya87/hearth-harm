using System;

/// <summary>
/// Snapshot of a player's lobby state. Shared between NetworkGameManager,
/// MainMenuController, and PlayerSlotUI.
///
/// Kept in its own file so it is visible across Assembly Definition boundaries.
/// </summary>
[Serializable]
public struct SessionPlayerInfo
{
    public string PlayerUgsId;
    public string DisplayName;
    public int    CharacterIndex;
    public bool   IsReady;
    public bool   IsLocalPlayer;
    public bool   IsHost;

    public SessionPlayerInfo(string id, string name, int charIdx, bool ready, bool isLocal, bool isHost)
    {
        PlayerUgsId    = id;
        DisplayName    = name;
        CharacterIndex = charIdx;
        IsReady        = ready;
        IsLocalPlayer  = isLocal;
        IsHost         = isHost;
    }
}