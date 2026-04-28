using System;


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