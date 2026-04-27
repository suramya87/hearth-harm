using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows directional travel buttons for the local player's current room.
/// Buttons are hidden when enemies are present (combat lock).
///
/// MULTIPLAYER FIX:
///   Travel() now routes through NetworkedPlayerBridge.TransitionToRoom()
///   instead of calling unit.PlaceInRoom() directly. This ensures the room
///   change is broadcast to all peers.
///   FindLocalPlayerUnit() finds the locally-owned Unit in MP instead of
///   using FindAnyObjectByType which could return another player's unit.
/// </summary>
public class RoomNavigationUI : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button          northButton, southButton, eastButton, westButton;
    [SerializeField] private TextMeshProUGUI northText,  southText,  eastText,  westText;

    [Header("Enemy Lock")]
    [SerializeField] private string enemyBlockMsg = "Enemies!";

    private LevelGenerator gen;

    private Dictionary<LevelGenerator.Direction, Button>          buttons;
    private Dictionary<LevelGenerator.Direction, TextMeshProUGUI> labels;

    private void Awake()
    {
        buttons = new()
        {
            { LevelGenerator.Direction.North, northButton },
            { LevelGenerator.Direction.South, southButton },
            { LevelGenerator.Direction.East,  eastButton  },
            { LevelGenerator.Direction.West,  westButton  }
        };
        labels = new()
        {
            { LevelGenerator.Direction.North, northText },
            { LevelGenerator.Direction.South, southText },
            { LevelGenerator.Direction.East,  eastText  },
            { LevelGenerator.Direction.West,  westText  }
        };

        northButton?.onClick.AddListener(() => Travel(LevelGenerator.Direction.North));
        southButton?.onClick.AddListener(() => Travel(LevelGenerator.Direction.South));
        eastButton?.onClick.AddListener(()  => Travel(LevelGenerator.Direction.East));
        westButton?.onClick.AddListener(()  => Travel(LevelGenerator.Direction.West));
    }

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady  += OnLevelReady;
        RoomManager.OnAnyRoomChanged += _ => UpdateButtons();
        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyListChanged += UpdateButtons;
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged += (_,__) => UpdateButtons();
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady  -= OnLevelReady;
        RoomManager.OnAnyRoomChanged -= _ => UpdateButtons();
        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyListChanged -= UpdateButtons;
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged -= (_,__) => UpdateButtons();
    }

    private void OnLevelReady()
    {
        gen = FindAnyObjectByType<LevelGenerator>();
        UpdateButtons();
    }

    // ── Update buttons ─────────────────────────────────────────────────────

    public void ForceUpdateButtons() => UpdateButtons();

    private void UpdateButtons()
    {
        var room = RoomManager.Instance?.GetCurrentRoom();
        if (room == null) return;

        bool enemies = EnemyManager.Instance != null &&
                       EnemyManager.Instance.GetEnemiesInRoom(room.roomGrid).Count > 0;

        foreach (LevelGenerator.Direction dir in System.Enum.GetValues(typeof(LevelGenerator.Direction)))
        {
            var btn = buttons[dir];
            var lbl = labels[dir];
            if (btn == null) continue;

            if (enemies)
            {
                btn.interactable = false;
                if (lbl) lbl.text = enemyBlockMsg;
                continue;
            }

            var connected = gen?.GetConnectedRoom(room, dir);
            if (connected != null)
            {
                btn.interactable = true;
                if (lbl) lbl.text = $"{dir}\n({connected.prefabData.roomType})";
            }
            else
            {
                btn.interactable = false;
                if (lbl) lbl.text = dir.ToString();
            }
        }
    }

    // ── Travel ─────────────────────────────────────────────────────────────

    private void Travel(LevelGenerator.Direction dir)
    {
        var room = RoomManager.Instance?.GetCurrentRoom();
        if (room == null) return;

        if (EnemyManager.Instance?.GetEnemiesInRoom(room.roomGrid).Count > 0)
        { Debug.Log("[RoomNav] Blocked by enemies."); return; }

        var target = gen?.GetConnectedRoom(room, dir);
        if (target == null) return;

        // ── MULTIPLAYER FIX ───────────────────────────────────────────────
        // In multiplayer, route through NetworkedPlayerBridge so the room
        // transition is synced to all peers. In single-player, fall through
        // to the original direct path.
        if (GameManager.IsMultiplayer)
        {
            var bridge = FindLocalPlayerBridge();
            if (bridge == null)
            {
                Debug.LogWarning("[RoomNav] Could not find local player bridge for room transition.");
                return;
            }

            var entryDir = gen.GetOppositeDirection(dir);
            var spawnPos = GetSpawnPosition(target, entryDir);
            bridge.TransitionToRoom(target.roomGrid, spawnPos);
            UpdateButtons();
            return;
        }
        // ─────────────────────────────────────────────────────────────────

        // Single-player path (unchanged)
        var player = FindAnyObjectByType<Unit>();
        if (player == null) return;

        var spEntry  = gen.GetOppositeDirection(dir);
        var spPos    = GetSpawnPosition(target, spEntry);

        RoomManager.Instance.SetCurrentRoom(target);
        player.PlaceInRoom(target.roomGrid, spPos);
        CameraController2D.Instance?.SnapToTarget();
        UpdateButtons();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds the NetworkedPlayerBridge that belongs to this local machine.
    /// Avoids FindAnyObjectByType which could return another player's unit
    /// when multiple players are connected.
    /// </summary>
    private NetworkedPlayerBridge FindLocalPlayerBridge()
    {
        foreach (var bridge in FindObjectsByType<NetworkedPlayerBridge>(FindObjectsSortMode.None))
            if (bridge.IsOwner) return bridge;
        return null;
    }

    private GridPosition GetSpawnPosition(LevelGenerator.PlacedRoom room,
                                          LevelGenerator.Direction  entry)
    {
        var reader = room.roomInstance.GetComponent<RoomSpawnPointReader>();
        if (reader != null && reader.HasSpawnPoint(entry))
            return reader.GetSpawnPosition(entry, room.roomGrid);

        return new GridPosition(room.roomGrid.GetWidth() / 2,
                                room.roomGrid.GetHeight() / 2);
    }
}