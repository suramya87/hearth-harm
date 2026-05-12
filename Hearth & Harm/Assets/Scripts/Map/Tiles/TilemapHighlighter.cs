using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Paints move/range/AoE highlights onto the active floor tilemap.
///
/// KEY FIXES vs original:
///   1. RefreshTilemap() and Update() both resolve the active tilemap from
///      unit.GetCurrentRoomGrid() first — this means the highlighter
///      automatically follows the unit into hallways without any extra
///      event wiring.
///   2. OnRoomChanged handles a null room gracefully (happens when the
///      player is in transit through a hallway and RoomManager has no
///      current room set).
///   3. activeGrid is resolved from the unit's current grid so mouse→tile
///      conversion works correctly on hallway tiles too.
/// </summary>
public class TilemapHighlighter : MonoBehaviour
{
    public static TilemapHighlighter Instance { get; private set; }

    [Header("Tile asset — plain white TileBase")]
    [SerializeField] private TileBase solidWhiteTile;

    [Header("Default colors")]
    [SerializeField] private Color moveColor  = new(0.2f, 0.6f, 1f,   1f);
    [SerializeField] private Color rangeColor = new(1f,  0.85f, 0f,   1f);
    [SerializeField] private Color aoeColor   = new(1f,  0.15f, 0.15f, 1f);
    [SerializeField] private Color hoverColor = new(1f,  1f,   1f,   0.4f);

    [Header("Enemy Preview")]
    [SerializeField] private Color enemyMoveColor = new(1f, 0.25f, 0.25f, 0.65f);

    private EnemyUnit previewEnemy;
    private List<GridPosition> previewEnemyPositions;

    private Tilemap                          paintedTilemap;
    private Dictionary<Vector3Int, TileBase> originalTiles = new();
    private HashSet<Vector3Int>              painted        = new();

    private void Awake() => Instance = this;

    private void Start()
    {
        if (paintedTilemap == null)
            RefreshTilemap();
    }

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady  += OnLevelReady;
        RoomManager.OnAnyRoomChanged += OnRoomChanged;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady  -= OnLevelReady;
        RoomManager.OnAnyRoomChanged -= OnRoomChanged;
        ResetAll();
    }

    private void OnLevelReady()                              => RefreshTilemap();

    /// <summary>
    /// Called when RoomManager's current room changes — including when it is
    /// set to null while the player is in a hallway. We still call
    /// RefreshTilemap() so the unit-grid fallback path runs.
    /// </summary>
    private void OnRoomChanged(LevelGenerator.PlacedRoom room) => RefreshTilemap();

    // ── Tilemap resolution ─────────────────────────────────────────────────

    private void RefreshTilemap()
    {
        ResetAll();

        // Primary: use whatever grid the unit is registered in right now.
        // This covers both normal rooms and hallways transparently.
        var unit = FindLocalUnit();
        if (unit != null)
        {
            var unitGrid = unit.GetCurrentRoomGrid();
            if (unitGrid != null)
            {
                paintedTilemap = unitGrid.GetFloorTilemap();
                if (paintedTilemap != null) return;
            }
        }

        // Fallback: RoomManager's current room (covers the brief window
        // before the unit is registered in a grid)
        paintedTilemap = RoomManager.Instance?.GetCurrentRoomGrid()?.GetFloorTilemap();

        if (paintedTilemap == null && GameManager.IsMultiplayer)
            TryFindLocalPlayerRoom();
    }

    private void TryFindLocalPlayerRoom()
    {
        foreach (var bridge in FindObjectsByType<NetworkedPlayerBridge>(FindObjectsSortMode.None))
        {
            if (!bridge.IsOwner) continue;
            var pos = bridge.GetNetworkGridPosition();
            var gen = FindAnyObjectByType<LevelGenerator>();
            if (gen == null) return;
            foreach (var placed in gen.GetAllRooms())
            {
                if (placed.roomGrid == null || !placed.roomGrid.IsInitialized()) continue;
                if (placed.roomGrid.IsValidGridPosition(pos))
                {
                    RoomManager.Instance?.SetCurrentRoom(placed);
                    return;
                }
            }
        }
    }

    // ── Update loop ────────────────────────────────────────────────────────

    private void Update()
    {
        // Resolve tilemap from unit grid every frame so hallway transit is seamless.
        var unit     = FindLocalUnit();
        var unitGrid = unit?.GetCurrentRoomGrid();
        var tilemap  = unitGrid?.GetFloorTilemap()
                    ?? RoomManager.Instance?.GetCurrentRoomGrid()?.GetFloorTilemap();

        if (tilemap != paintedTilemap)
        {
            ResetAll();
            paintedTilemap = tilemap;
        }

        if (paintedTilemap == null) return;

        ResetAll();
        GridCostVisualizer.Instance?.ClearAll();

        if (previewEnemy != null && previewEnemyPositions != null)
        {
            Paint(previewEnemyPositions, enemyMoveColor);
            return;
        }

        if (!IsPlayerPhaseNow()) return;

        var action = UnitActionSystem.Instance?.GetSelectedAction();
        if (action == null) return;

        // Use the unit's current grid for all mouse → grid position conversions.
        // This is correct in rooms AND in hallways.
        var activeGrid = unitGrid ?? RoomManager.Instance?.GetCurrentRoomGrid();

        if (action is MoveAction move)
        {
            Paint(move.GetValidActionGridPositionList(), moveColor);

            if (activeGrid != null)
            {
                var mouseGP = activeGrid.GetGridPosition(MouseWorld2D.GetPosition());
                if (move.IsValidTarget(mouseGP))
                {
                    int cost = move.GetMoveCost(mouseGP);
                    if (cost >= 0 && GridCostVisualizer.Instance != null)
                        GridCostVisualizer.Instance.ShowCost(mouseGP, cost);
                }
            }
        }
        else if (action is CombatAction combat)
        {
            Color rc = combat.ActionData != null ? combat.ActionData.rangeHighlightColor : rangeColor;
            Color ac = combat.ActionData != null ? combat.ActionData.aoeHighlightColor   : aoeColor;

            Paint(combat.GetValidActionGridPositionList(), rc);

            if (activeGrid != null)
            {
                var mouseGP = activeGrid.GetGridPosition(MouseWorld2D.GetPosition());
                Paint(combat.GetPreviewPositions(mouseGP), ac);
            }
        }

        // Hover highlight
        if (activeGrid != null)
        {
            var gp = activeGrid.GetGridPosition(MouseWorld2D.GetPosition());
            if (activeGrid.IsValidGridPosition(gp))
                PaintCell(new Vector3Int(gp.x, gp.y, 0), hoverColor);
        }
    }

    // ── Turn-phase helper ──────────────────────────────────────────────────

    private static bool IsPlayerPhaseNow()
    {
        if (GameManager.IsMultiplayer)
            return NetworkedTurnSystem.Instance == null || NetworkedTurnSystem.Instance.IsPlayerPhase;

        return TurnSystem.Instance == null || TurnSystem.Instance.IsPlayerTurn;
    }

    // ── Local unit helper ──────────────────────────────────────────────────

    private static Unit FindLocalUnit()
    {
        if (!GameManager.IsMultiplayer)
            return FindAnyObjectByType<Unit>();

        foreach (var u in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            var netObj = u.GetComponent<Unity.Netcode.NetworkObject>();
            if (netObj != null && netObj.IsOwner) return u;
        }
        return null;
    }

    // ── Paint helpers ──────────────────────────────────────────────────────

    private void Paint(List<GridPosition> list, Color color)
    {
        foreach (var gp in list)
            PaintCell(new Vector3Int(gp.x, gp.y, 0), color);
    }

    private void PaintCell(Vector3Int pos, Color color)
    {
        if (paintedTilemap == null || !paintedTilemap.HasTile(pos)) return;

        if (!originalTiles.ContainsKey(pos))
            originalTiles[pos] = paintedTilemap.GetTile(pos);

        paintedTilemap.SetTile(pos, solidWhiteTile);
        paintedTilemap.SetTileFlags(pos, TileFlags.None);
        paintedTilemap.SetColor(pos, color);
        painted.Add(pos);
    }

    private void ResetAll()
    {
        if (paintedTilemap == null) { painted.Clear(); originalTiles.Clear(); return; }

        foreach (var pos in painted)
        {
            if (originalTiles.TryGetValue(pos, out var orig))
            {
                paintedTilemap.SetTile(pos, orig);
                paintedTilemap.SetTileFlags(pos, TileFlags.None);
                paintedTilemap.SetColor(pos, Color.white);
            }
        }
        painted.Clear();
        originalTiles.Clear();
    }

    public void ShowPositions(List<GridPosition> positions, Color color)
    {
        if (paintedTilemap == null) return;
        Paint(positions, color);
    }

    public void HideAll() => ResetAll();

    public void ShowEnemyMoveRange(EnemyUnit enemy)
    {
        previewEnemy = enemy;
        previewEnemyPositions = BuildEnemyMoveRange(enemy);
    }

    public void ClearEnemyPreview()
    {
        previewEnemy = null;
        previewEnemyPositions = null;
    }

    private List<GridPosition> BuildEnemyMoveRange(EnemyUnit enemy)
    {
        List<GridPosition> positions = new();

        if (enemy == null || enemy.Stats == null || enemy.CurrentRoomGrid == null)
            return positions;

        RoomGrid room = enemy.CurrentRoomGrid;
        GridPosition enemyPos = enemy.GridPosition;
        int range = enemy.Stats.moveRange;

        Pathfinder pf = new Pathfinder(room);

        for (int dx = -range; dx <= range; dx++)
            for (int dy = -range; dy <= range; dy++)
            {
                if (Mathf.Abs(dx) + Mathf.Abs(dy) > range)
                    continue;

                if (dx == 0 && dy == 0)
                    continue;

                GridPosition test = new GridPosition(enemyPos.x + dx, enemyPos.y + dy);

                if (!room.IsValidGridPosition(test))
                    continue;

                if (!room.IsWalkableIgnoreOccupancy(test))
                    continue;

                if (room.HasAnyUnitOnGridPosition(test))
                    continue;

                List<GridPosition> path = pf.FindPath(enemyPos, test);

                if (path != null && path.Count > 0 && path.Count <= range)
                    positions.Add(test);
            }

        return positions;
    }
}