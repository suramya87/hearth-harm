using UnityEngine;

/// <summary>
/// Tracks the player Unit that enemies should target.
///
/// In singleplayer this is the only Unit in the scene.
/// In multiplayer each peer has ONE local owned Unit — enemies running on
/// the SERVER target the nearest unit in the room, so this singleton is
/// used only for local queries (camera follow, UI, etc.).
///
/// Place this component on a persistent GameObject (e.g. GameManager) or
/// on each player prefab; in the latter case the most recently spawned
/// owned unit wins.
/// </summary>
public class PlayerTarget : MonoBehaviour
{
    public static PlayerTarget Instance { get; private set; }

    private Unit trackedUnit;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Allow the newest instance to win (e.g. after level reload).
            Destroy(Instance.gameObject.GetComponent<PlayerTarget>());
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Register a unit as the local player target.
    /// Called by the Unit itself (or NetworkedPlayerBridge) after spawn.
    /// </summary>
    public void Register(Unit unit) => trackedUnit = unit;

    /// <summary>Returns the tracked local unit, falling back to a scene search.</summary>
    public Unit GetUnit()
    {
        if (trackedUnit != null) return trackedUnit;

        if (!GameManager.IsMultiplayer)
        {
            trackedUnit = FindAnyObjectByType<Unit>();
            return trackedUnit;
        }

        // Multiplayer: find the owned unit.
        foreach (var u in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            var netObj = u.GetComponent<Unity.Netcode.NetworkObject>();
            if (netObj != null && netObj.IsOwner)
            {
                trackedUnit = u;
                return trackedUnit;
            }
        }
        return null;
    }

    /// <summary>True if the tracked unit is currently in the given room.</summary>
    public bool IsInRoom(RoomGrid room)
    {
        var u = GetUnit();
        if (u == null || room == null) return false;
        return u.GetCurrentRoomGrid() == room ||
               u.GetCurrentRoomGrid()?.gameObject.name == room.gameObject.name;
    }
}