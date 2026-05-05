using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows which directions have connected rooms.
///
/// CHANGE: Travel() has been removed. With seamless hallway tilemaps the
/// player walks between rooms using MoveAction — no teleportation needed.
/// The buttons now show connection info only (or can be removed from the UI
/// entirely). They no longer move the player.
///
/// If you want to keep the buttons for fast-travel or accessibility, re-add
/// Travel() but have it call unit.PlaceInRoom on the TARGET room's grid using
/// the spawn point reader — the key is to always use the target room's own
/// RoomGrid, not a hallway grid.
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

        // Buttons are display-only now — no travel listeners
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
        if (room == null || room.IsHallway) return;

        bool enemies = EnemyManager.Instance != null &&
                       EnemyManager.Instance.GetEnemiesInRoom(room.roomGrid).Count > 0;

        foreach (LevelGenerator.Direction dir in
            System.Enum.GetValues(typeof(LevelGenerator.Direction)))
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
                btn.interactable = false; // display only — player walks there
                if (lbl) lbl.text = $"{dir}\n({connected.prefabData?.roomType})";
            }
            else
            {
                btn.interactable = false;
                if (lbl) lbl.text = dir.ToString();
            }
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

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