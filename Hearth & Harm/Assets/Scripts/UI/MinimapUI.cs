using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


public class MinimapUI : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────

    [Header("Minimap")]
    [Tooltip("RectTransform that will contain the room dot squares.")]
    [SerializeField] private RectTransform minimapContainer;

    [Tooltip("Prefab: a UI Image with an optional TMP child for icons.")]
    [SerializeField] private GameObject roomDotPrefab;

    [Tooltip("Size of each room square in pixels.")]
    [SerializeField] private float dotSize = 14f;

    [Tooltip("Gap between squares in pixels.")]
    [SerializeField] private float dotSpacing = 4f;

    [Header("Colours")]
    [SerializeField] private Color colourUndiscovered = new(0.25f, 0.25f, 0.25f, 1f);
    [SerializeField] private Color colourCleared      = new(0.85f, 0.85f, 0.85f, 1f);
    [SerializeField] private Color colourEnemies      = new(1f,    0.85f, 0.1f,  1f);
    [SerializeField] private Color colourCurrent      = new(0.2f,  0.9f,  0.9f,  1f);
    [SerializeField] private Color colourBoss         = new(0.8f,  0.1f,  0.1f,  1f);
    [SerializeField] private Color colourStart        = new(0.2f,  0.8f,  0.2f,  1f);
    [SerializeField] private Color colourEnd          = new(0.9f,  0.6f,  0.1f,  1f);

    [Header("Navigation Buttons")]
    [SerializeField] private Button          northButton, southButton, eastButton, westButton;
    [SerializeField] private TextMeshProUGUI northLabel,  southLabel,  eastLabel,  westLabel;
    [SerializeField] private string          enemyBlockMsg = "!";
    [SerializeField] private string          noRoomMsg     = "—";

    // ── Runtime ────────────────────────────────────────────────────────────

    private LevelGenerator                           gen;
    private readonly HashSet<LevelGenerator.PlacedRoom> discovered = new();
    private readonly HashSet<LevelGenerator.PlacedRoom> cleared    = new();

    // Maps each PlacedRoom to its dot Image on the minimap
    private readonly Dictionary<LevelGenerator.PlacedRoom, Image> dots = new();

    // ── Unity lifecycle ────────────────────────────────────────────────────

    private void Awake()
    {
        northButton?.onClick.AddListener(() => Travel(LevelGenerator.Direction.North));
        southButton?.onClick.AddListener(() => Travel(LevelGenerator.Direction.South));
        eastButton?.onClick.AddListener(()  => Travel(LevelGenerator.Direction.East));
        westButton?.onClick.AddListener(()  => Travel(LevelGenerator.Direction.West));
    }

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady  += OnLevelReady;
        RoomManager.OnAnyRoomChanged += OnRoomChanged;

        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyListChanged += Refresh;
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged += (_, __) => Refresh();
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady  -= OnLevelReady;
        RoomManager.OnAnyRoomChanged -= OnRoomChanged;

        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyListChanged -= Refresh;
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged -= (_, __) => Refresh();
    }

    // ── Level ready ────────────────────────────────────────────────────────

    private void OnLevelReady()
    {
        gen = FindAnyObjectByType<LevelGenerator>();
        discovered.Clear();
        cleared.Clear();
        BuildMinimap();
        Refresh();
    }

    // ── Room changed ───────────────────────────────────────────────────────

    private void OnRoomChanged(LevelGenerator.PlacedRoom room)
    {
        if (room != null)
        {
            discovered.Add(room);

            // Mark cleared if no enemies remain
            if (EnemyManager.Instance == null ||
                EnemyManager.Instance.GetEnemiesInRoom(room.roomGrid).Count == 0)
                cleared.Add(room);
        }

        Refresh();
    }

    // ── Build minimap grid ─────────────────────────────────────────────────

    private void BuildMinimap()
    {
        // Destroy old dots
        foreach (Transform c in minimapContainer) Destroy(c.gameObject);
        dots.Clear();

        if (gen == null || minimapContainer == null || roomDotPrefab == null) return;

        var rooms = gen.GetAllRooms();
        if (rooms == null || rooms.Count == 0) return;

        // Find grid extent
        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = int.MaxValue, maxY = int.MinValue;
        foreach (var r in rooms)
        {
            minX = Mathf.Min(minX, r.gridPosition.x);
            maxX = Mathf.Max(maxX, r.gridPosition.x);
            minY = Mathf.Min(minY, r.gridPosition.y);
            maxY = Mathf.Max(maxY, r.gridPosition.y);
        }

        float step = dotSize + dotSpacing;

        // Resize container to fit the grid
        float w = (maxX - minX + 1) * step;
        float h = (maxY - minY + 1) * step;
        minimapContainer.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
        minimapContainer.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,   h);

        foreach (var room in rooms)
        {
            var dot = Instantiate(roomDotPrefab, minimapContainer);
            var rt  = dot.GetComponent<RectTransform>();

            // Position: bottom-left origin, y grows up
            float px = (room.gridPosition.x - minX) * step;
            float py = (room.gridPosition.y - minY) * step;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
            rt.pivot     = new Vector2(0, 0);
            rt.anchoredPosition = new Vector2(px, py);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, dotSize);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,   dotSize);

            // Icon label
            var lbl = dot.GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null)
            {
                lbl.text = room.prefabData.roomType switch
                {
                    LevelGenerator.RoomType.Start   => "★",
                    LevelGenerator.RoomType.End     => "⚑",
                    LevelGenerator.RoomType.Boss    => "☠",
                    LevelGenerator.RoomType.Special => "?",
                    _                               => ""
                };
            }

            dots[room] = dot.GetComponent<Image>();
        }
    }

    // ── Refresh colours + buttons ──────────────────────────────────────────

    public void Refresh()
    {
        if (gen == null) return;

        var currentRoom = RoomManager.Instance?.GetCurrentRoom();

        // Update each dot's colour
        foreach (var (room, img) in dots)
        {
            if (img == null) continue;

            bool isCurrent    = room == currentRoom;
            bool isDiscovered = discovered.Contains(room);
            bool hasEnemies   = EnemyManager.Instance != null &&
                                EnemyManager.Instance.GetEnemiesInRoom(room.roomGrid).Count > 0;

            img.color = room.prefabData.roomType switch
            {
                LevelGenerator.RoomType.Start when isCurrent   => colourCurrent,
                LevelGenerator.RoomType.Start                   => colourStart,
                LevelGenerator.RoomType.End   when isCurrent   => colourCurrent,
                LevelGenerator.RoomType.End   when isDiscovered => colourEnd,
                LevelGenerator.RoomType.Boss  when isCurrent   => colourCurrent,
                LevelGenerator.RoomType.Boss  when isDiscovered && !hasEnemies => colourBoss * 0.5f,
                LevelGenerator.RoomType.Boss  when isDiscovered => colourBoss,
                _ when isCurrent                                => colourCurrent,
                _ when !isDiscovered                            => colourUndiscovered,
                _ when hasEnemies                               => colourEnemies,
                _                                               => colourCleared
            };
        }

        // Update travel buttons
        UpdateButtons(currentRoom);
    }

    // ── Travel buttons ─────────────────────────────────────────────────────

    private void UpdateButtons(LevelGenerator.PlacedRoom currentRoom)
    {
        foreach (LevelGenerator.Direction dir in
            System.Enum.GetValues(typeof(LevelGenerator.Direction)))
        {
            var (btn, lbl) = GetButtonAndLabel(dir);
            if (btn == null) continue;

            if (currentRoom == null)
            {
                btn.interactable = false;
                if (lbl) lbl.text = noRoomMsg;
                continue;
            }

            // Block if current room has enemies
            bool currentHasEnemies = EnemyManager.Instance != null &&
                EnemyManager.Instance.GetEnemiesInRoom(currentRoom.roomGrid).Count > 0;
            if (currentHasEnemies)
            {
                btn.interactable = false;
                if (lbl) lbl.text = enemyBlockMsg;
                continue;
            }

            var neighbour = gen?.GetConnectedRoom(currentRoom, dir);
            if (neighbour == null)
            {
                btn.interactable = false;
                if (lbl) lbl.text = noRoomMsg;
                continue;
            }

            // Only allow fast-travel to discovered + cleared rooms
            bool neighbourCleared = cleared.Contains(neighbour);
            btn.interactable = neighbourCleared;
            if (lbl)
            {
                lbl.text = neighbourCleared
                    ? $"{dir}\n{neighbour.prefabData.roomType}"
                    : $"{dir}\n?";
            }
        }
    }

    private (Button btn, TextMeshProUGUI lbl) GetButtonAndLabel(LevelGenerator.Direction dir) =>
        dir switch
        {
            LevelGenerator.Direction.North => (northButton, northLabel),
            LevelGenerator.Direction.South => (southButton, southLabel),
            LevelGenerator.Direction.East  => (eastButton,  eastLabel),
            LevelGenerator.Direction.West  => (westButton,  westLabel),
            _                              => (null, null)
        };

    // ── Travel ─────────────────────────────────────────────────────────────

    private void Travel(LevelGenerator.Direction dir)
    {
        var current = RoomManager.Instance?.GetCurrentRoom();
        if (current == null) return;

        // Guard: current room must be clear
        if (EnemyManager.Instance?.GetEnemiesInRoom(current.roomGrid).Count > 0)
        {
            Debug.Log("[MinimapUI] Travel blocked — enemies in current room.");
            return;
        }

        var target = gen?.GetConnectedRoom(current, dir);
        if (target == null) return;

        // Guard: destination must be cleared (discovered + no enemies)
        if (!cleared.Contains(target)) return;

        if (GameManager.IsMultiplayer)
        {
            foreach (var bridge in FindObjectsByType<NetworkedPlayerBridge>(FindObjectsSortMode.None))
            {
                if (!bridge.IsOwner) continue;
                var entryDir = gen.GetOppositeDirection(dir);
                bridge.TransitionToRoom(target.roomGrid, GetSpawnPos(target, entryDir));
                break;
            }
        }
        else
        {
            var player = FindAnyObjectByType<Unit>();
            if (player == null) return;

            var entry    = gen.GetOppositeDirection(dir);
            var spawnPos = GetSpawnPos(target, entry);

            RoomManager.Instance.SetCurrentRoom(target);
            player.PlaceInRoom(target.roomGrid, spawnPos);
            CameraController2D.Instance?.SnapToTarget();
        }

        Refresh();
    }

    private static GridPosition GetSpawnPos(
        LevelGenerator.PlacedRoom room,
        LevelGenerator.Direction  entry)
    {
        var reader = room.roomInstance.GetComponent<RoomSpawnPointReader>();
        if (reader != null && reader.HasSpawnPoint(entry))
            return reader.GetSpawnPosition(entry, room.roomGrid);

        return new GridPosition(room.roomGrid.GetWidth() / 2,
                                room.roomGrid.GetHeight() / 2);
    }
}