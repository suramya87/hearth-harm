using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Lives on the player prefab. The static Instance always points to the
/// LOCAL client's own copy — non-owned instances never register.
/// </summary>
public class PlayerTarget : MonoBehaviour
{
    public static PlayerTarget Instance { get; private set; }

    private Unit trackedUnit;

    private void Start()
    {
        // Only register if this is the local player's object.
        var netObj = GetComponent<NetworkObject>();
        bool isLocal = netObj == null || netObj.IsOwner;
        if (!isLocal) return;

        Instance    = this;
        trackedUnit = GetComponent<Unit>();
        Debug.Log($"[PlayerTarget] Registered local player: {gameObject.name}");
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Called by Unit.RegisterWithLocalSystems() to force registration
    /// immediately after placement (before Start() may run on late-spawned objects).
    /// </summary>
    public static void ForceRegister(PlayerTarget pt, Unit unit)
    {
        Instance = pt;
        pt.trackedUnit = unit;
        Debug.Log($"[PlayerTarget] Force-registered: {unit.gameObject.name}");
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public Unit      GetUnit()      => trackedUnit;
    public Transform GetTransform() => transform;

    public bool IsInRoom(RoomGrid room)
    {
        if (trackedUnit == null || room == null) return false;
        var current = trackedUnit.GetCurrentRoomGrid();
        return current == room ||
               current?.gameObject.name == room.gameObject.name;
    }
}