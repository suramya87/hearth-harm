using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MinimapUI : MonoBehaviour
{
    [Header("Minimap")]
    [SerializeField] private RectTransform minimapContainer;
    [SerializeField] private float dotSize           = 14f;
    [SerializeField] private float dotSpacing        = 4f;
    [SerializeField] private Vector2 minimapDisplaySize = new(200f, 200f);

    [Header("Room Sprites")]
    [SerializeField] private Sprite spriteDefault;
    [SerializeField] private Sprite spriteStart;
    [SerializeField] private Sprite spriteEnd;
    [SerializeField] private Sprite spriteBoss;
    [SerializeField] private Sprite spriteSpecial;

    [Header("Tints")]
    [SerializeField] private Color tintUndiscovered = new(0.3f, 0.3f, 0.3f, 1f);
    [SerializeField] private Color tintVisited      = new(1f,   1f,   1f,   1f);
    [SerializeField] private Color tintCurrent      = new(0.6f, 1f,   0.6f, 1f);
    [SerializeField] private Color tintCleared      = new(0.6f, 0.8f, 1f,   1f);
    [SerializeField] private Color tintFog          = new(0.1f, 0.1f, 0.1f, 0.4f);

    [Header("Fog of War")]
    [SerializeField] private int viewRadius = 2;

    [Header("Navigation Buttons")]
    [SerializeField] private Button          northButton, southButton, eastButton, westButton;
    [SerializeField] private TextMeshProUGUI northLabel,  southLabel,  eastLabel,  westLabel;
    [SerializeField] private string          enemyBlockMsg = "!";
    [SerializeField] private string          noRoomMsg     = "—";

    // ── Runtime ────────────────────────────────────────────────────────────

    private LevelGenerator                                        gen;
    private readonly HashSet<LevelGenerator.PlacedRoom>           discovered = new();
    private readonly HashSet<LevelGenerator.PlacedRoom>           cleared    = new();
    private readonly HashSet<LevelGenerator.PlacedRoom>           glimpsed   = new();
    private readonly Dictionary<LevelGenerator.PlacedRoom, Image> dots       = new();
    private bool _revealAll = false;

    // ── Lifecycle ──────────────────────────────────────────────────────────

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
        RoomManager.OnRoomCleared    += OnRoomClearedCallback;

        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyListChanged += Refresh;
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged += OnTurnChanged;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady  -= OnLevelReady;
        RoomManager.OnAnyRoomChanged -= OnRoomChanged;
        RoomManager.OnRoomCleared    -= OnRoomClearedCallback;

        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyListChanged -= Refresh;
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged -= OnTurnChanged;
    }

    private void OnTurnChanged(object sender, EventArgs e) => Refresh();

    // ── Level ready ────────────────────────────────────────────────────────

    private void OnLevelReady()
    {
        gen = FindAnyObjectByType<LevelGenerator>();
        discovered.Clear();
        cleared.Clear();
        glimpsed.Clear();
        _revealAll = false;
        BuildMinimap();

        var startRoom = gen?.GetAllRooms()
            ?.Find(r => r.prefabData.roomType == LevelGenerator.RoomType.Start);
        if (startRoom != null)
        {
            discovered.Add(startRoom);
            cleared.Add(startRoom);
        }

        Refresh();
    }

    // ── Room changed ───────────────────────────────────────────────────────

    private void OnRoomChanged(LevelGenerator.PlacedRoom room)
    {
        if (room != null)
        {
            discovered.Add(room);
        }

        Refresh();
    }

    // ── Room cleared callback ──────────────────────────────────────────────

    /// <summary>
    /// Called by RoomManager.NotifyRoomCleared, which is triggered from
    /// HallwayEntryTrigger.HandleRoomCleared after the last enemy dies.
    /// </summary>
    private void OnRoomClearedCallback(LevelGenerator.PlacedRoom room)
    {
        if (room == null) return;
        discovered.Add(room);  
        cleared.Add(room);
        Refresh();
    }

    // ── Public fog controls ────────────────────────────────────────────────

    public void SetViewRadius(int radius) { viewRadius = Mathf.Max(0, radius); Refresh(); }
    public void RevealAll()  { _revealAll = true;  Refresh(); }
    public void HideFog()    { _revealAll = false; Refresh(); }

    // ── Build minimap ──────────────────────────────────────────────────────

    private void BuildMinimap()
    {
        foreach (Transform c in minimapContainer) Destroy(c.gameObject);
        dots.Clear();

        if (gen == null || minimapContainer == null) return;

        var rooms = gen.GetAllRooms();
        if (rooms == null || rooms.Count == 0) return;

        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = int.MaxValue, maxY = int.MinValue;
        foreach (var r in rooms)
        {
            minX = Mathf.Min(minX, r.gridPosition.x);
            maxX = Mathf.Max(maxX, r.gridPosition.x);
            minY = Mathf.Min(minY, r.gridPosition.y);
            maxY = Mathf.Max(maxY, r.gridPosition.y);
        }

        minimapContainer.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Horizontal, minimapDisplaySize.x);
        minimapContainer.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical, minimapDisplaySize.y);

        float gridW         = maxX - minX + 1;
        float gridH         = maxY - minY + 1;
        float availableStep = Mathf.Min(
            minimapDisplaySize.x / gridW, minimapDisplaySize.y / gridH);
        float usedStep    = Mathf.Min(dotSize + dotSpacing, availableStep);
        float usedDotSize = usedStep - dotSpacing;

        foreach (var room in rooms)
        {
            var dot = new GameObject("RoomDot", typeof(RectTransform), typeof(Image));
            dot.transform.SetParent(minimapContainer, false);

            var rt = dot.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot     = Vector2.zero;
            rt.anchoredPosition = new Vector2(
                (room.gridPosition.x - minX) * usedStep,
                (room.gridPosition.y - minY) * usedStep);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, usedDotSize);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,   usedDotSize);

            var img = dot.GetComponent<Image>();
            img.sprite = room.prefabData.roomType switch
            {
                LevelGenerator.RoomType.Start   => spriteStart,
                LevelGenerator.RoomType.End     => spriteEnd,
                LevelGenerator.RoomType.Boss    => spriteBoss,
                LevelGenerator.RoomType.Special => spriteSpecial,
                _                               => spriteDefault
            };
            img.type           = Image.Type.Simple;
            img.preserveAspect = true;
            img.color          = tintFog;

            dots[room] = img;
        }
    }

    // ── Refresh ────────────────────────────────────────────────────────────

    public void Refresh()
    {
        if (gen == null) return;

        var currentRoom = RoomManager.Instance?.GetCurrentRoom();

        // Expand glimpse radius around current room.
        if (!_revealAll && currentRoom != null)
        {
            foreach (var (room, _) in dots)
                if (IsInRange(room, currentRoom))
                    glimpsed.Add(room);
        }

        foreach (var (room, img) in dots)
        {
            if (img == null) continue;

            bool inRange     = _revealAll || IsInRange(room, currentRoom);
            bool wasGlimpsed = glimpsed.Contains(room);

            if (!inRange && !wasGlimpsed)
            {
                img.color = tintFog;
                continue;
            }

            if (room == currentRoom)
                img.color = tintCurrent;
            else if (cleared.Contains(room))
                img.color = tintCleared;
            else if (discovered.Contains(room))
                img.color = tintVisited;
            else
                img.color = tintUndiscovered;
        }

        UpdateButtons(currentRoom);
    }

    // ── Fog range ─────────────────────────────────────────────────────────

    private bool IsInRange(
        LevelGenerator.PlacedRoom room,
        LevelGenerator.PlacedRoom currentRoom)
    {
        if (currentRoom == null) return false;
        int dx = Mathf.Abs(room.gridPosition.x - currentRoom.gridPosition.x);
        int dy = Mathf.Abs(room.gridPosition.y - currentRoom.gridPosition.y);
        return (dx + dy) <= viewRadius;
    }

    // ── Navigation buttons ─────────────────────────────────────────────────

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

            btn.interactable = true;
            if (lbl)
            {
                lbl.text = cleared.Contains(neighbour)
                    ? $"{dir}\n{neighbour.prefabData.roomType}"
                    : discovered.Contains(neighbour)
                    ? $"{dir}\n?"
                    : $"{dir}\n{neighbour.prefabData.roomType}";
            }
        }
    }

    private (Button btn, TextMeshProUGUI lbl) GetButtonAndLabel(
        LevelGenerator.Direction dir) => dir switch
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

        if (EnemyManager.Instance?.GetEnemiesInRoom(current.roomGrid).Count > 0)
        {
            Debug.Log("[MinimapUI] Travel blocked — enemies in current room.");
            return;
        }

        var target = gen?.GetConnectedRoom(current, dir);
        if (target == null) return;

        if (GameManager.IsMultiplayer)
        {
            foreach (var bridge in
                FindObjectsByType<NetworkedPlayerBridge>(FindObjectsSortMode.None))
            {
                if (!bridge.IsOwner) continue;
                bridge.TransitionToRoom(
                    target.roomGrid,
                    GetSpawnPos(target, gen.GetOppositeDirection(dir)));
                break;
            }
        }
        else
        {
            var player = FindAnyObjectByType<Unit>();
            if (player == null) return;

            SpawnEnemiesViaButton(target);

            RoomManager.Instance.SetCurrentRoom(target);
            player.PlaceInRoom(
                target.roomGrid,
                GetSpawnPos(target, gen.GetOppositeDirection(dir)));
            CameraController2D.Instance?.SnapToTarget();

            player.GetMoveAction()?.InvalidateCache();
        }

        Refresh();
    }

    private static void SpawnEnemiesViaButton(LevelGenerator.PlacedRoom room)
    {
        if (room == null) return;
        if (room.prefabData.roomType == LevelGenerator.RoomType.Start) return;
        if (room.roomGrid.HasBeenCleared) return;
        if (EnemyManager.Instance == null) return;
        if (EnemyManager.Instance.GetEnemiesInRoom(room.roomGrid).Count > 0) return;

        var spawner = FindAnyObjectByType<EnemySpawner>();
        spawner?.SpawnForRoom(room);

        bool hasEnemies =
            EnemyManager.Instance.GetEnemiesInRoom(room.roomGrid).Count > 0;
        if (hasEnemies)
            room.connector?.CloseAllDoors();
    }

    private static GridPosition GetSpawnPos(
        LevelGenerator.PlacedRoom room,
        LevelGenerator.Direction  entry)
    {
        var reader = room.roomInstance.GetComponent<RoomSpawnPointReader>();
        if (reader != null && reader.HasSpawnPoint(entry))
            return reader.GetSpawnPosition(entry, room.roomGrid);

        return new GridPosition(
            room.roomGrid.GetWidth()  / 2,
            room.roomGrid.GetHeight() / 2);
    }
}