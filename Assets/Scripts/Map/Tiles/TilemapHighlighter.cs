using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TilemapHighlighter : MonoBehaviour
{
    public static TilemapHighlighter Instance { get; private set; }

    [Header("Overlay tile — plain white 1x1 sprite TileBase")]
    [SerializeField] private TileBase overlayTile;

    [Header("Highlight colours")]
    [SerializeField] private Color moveColor      = new(0.2f, 0.6f, 1f,    0.55f);
    [SerializeField] private Color rangeColor     = new(1f,  0.85f, 0f,    0.55f);
    [SerializeField] private Color aoeColor       = new(1f,  0.15f, 0.15f, 0.55f);
    [SerializeField] private Color hoverColor     = new(1f,  1f,    1f,    0.35f);
    [SerializeField] private Color enemyMoveColor = new(1f,  0.25f, 0.25f, 0.5f);

    [Header("Overlay sorting")]
    [SerializeField] private string overlaySortingLayer = "Default";
    [SerializeField] private int    overlaySortingOrder = 10;

    private GameObject overlayRoot;
    private Tilemap    overlayTilemap;
    private Grid       overlayGrid;

    private EnemyUnit          previewEnemy;
    private List<GridPosition> previewEnemyPositions;
    private bool               isSystemBusy;

    private void Awake()
    {
        Instance = this;
        BuildOverlay(); // always build fresh — no DontDestroyOnLoad
    }

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady  += OnLevelReady;
        RoomManager.OnAnyRoomChanged += OnRoomChanged;

        if (UnitActionSystem.Instance != null)
            UnitActionSystem.Instance.OnBusyChanged += OnBusyChanged;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady  -= OnLevelReady;
        RoomManager.OnAnyRoomChanged -= OnRoomChanged;

        if (UnitActionSystem.Instance != null)
            UnitActionSystem.Instance.OnBusyChanged -= OnBusyChanged;

        SafeClear();
    }

    private void OnBusyChanged(object sender, bool busy)
    {
        isSystemBusy = busy;
        if (busy) SafeClear();
    }

    private void OnDestroy()
    {
        SafeClear();
        if (overlayRoot != null) Destroy(overlayRoot);
    }

    private void OnLevelReady()                              => SafeClear();
    private void OnRoomChanged(LevelGenerator.PlacedRoom _)  => SafeClear();

    // ── Overlay ────────────────────────────────────────────────────────────

    private void BuildOverlay()
    {
        if (overlayRoot != null) Destroy(overlayRoot);

        overlayRoot = new GameObject("__HighlightOverlay__");
        // Do NOT DontDestroyOnLoad — each scene/client gets its own overlay.

        overlayGrid          = overlayRoot.AddComponent<Grid>();
        overlayGrid.cellSize = new Vector3(1f, 1f, 0f);
        overlayGrid.cellGap  = Vector3.zero;

        var tmGo = new GameObject("Tilemap");
        tmGo.transform.SetParent(overlayRoot.transform, worldPositionStays: false);
        overlayTilemap = tmGo.AddComponent<Tilemap>();

        var ren = tmGo.AddComponent<TilemapRenderer>();
        ren.sortingLayerName = overlaySortingLayer;
        ren.sortingOrder     = overlaySortingOrder;
    }

    private void SafeClear()
    {
        if (overlayTilemap != null) overlayTilemap.ClearAllTiles();
    }

    // ── Update ─────────────────────────────────────────────────────────────

    private void Update()
    {
        SafeClear();
        if (isSystemBusy) return;

        GridCostVisualizer.Instance?.ClearAll();

        // Get the LOCAL player's unit — each client highlights for their own unit.
        var unit     = FindLocalUnit();
        var unitGrid = unit?.GetCurrentRoomGrid();

        if (previewEnemy != null && previewEnemyPositions != null)
        {
            var enemyGrid = previewEnemy.CurrentRoomGrid;
            if (enemyGrid != null)
                PaintGridPositions(previewEnemyPositions, enemyMoveColor, enemyGrid);
            return;
        }

        if (!IsPlayerPhaseNow()) return;

        // Get the selected action for the LOCAL unit specifically.
        // In multiplayer UnitActionSystem.selectedUnit is already the owned unit,
        // but double-check to be safe.
        var action = UnitActionSystem.Instance?.GetSelectedAction();
        if (action == null) return;

        // Make sure the action belongs to our local unit.
        if (unit != null && action.GetUnit() != unit) return;

        Vector3 mouseWorld = GetMouseWorldRaw();

        if (action is MoveAction move)
        {
            PaintWorldPositions(move.GetValidActionWorldPositions(), moveColor);

            if (unitGrid != null)
            {
                var mouseGP = unitGrid.GetGridPosition(mouseWorld);
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
            if (unitGrid != null)
            {
                Color rc = combat.ActionData?.rangeHighlightColor ?? rangeColor;
                Color ac = combat.ActionData?.aoeHighlightColor   ?? aoeColor;

                PaintGridPositions(combat.GetValidActionGridPositionList(), rc, unitGrid);

                var mouseGP = unitGrid.GetGridPosition(mouseWorld);
                PaintGridPositions(combat.GetPreviewPositions(mouseGP), ac, unitGrid);
            }
        }

        var hoverGrid = unitGrid ?? RoomManager.Instance?.GetCurrentRoomGrid();
        if (hoverGrid != null)
        {
            var hoverGP = hoverGrid.GetGridPosition(mouseWorld);
            if (hoverGrid.IsValidGridPosition(hoverGP))
                PaintOverlayCell(
                    WorldToOverlayCell(hoverGrid.GetWorldPosition(hoverGP)),
                    hoverColor);
        }
    }

    // ── Paint helpers ──────────────────────────────────────────────────────

    private void PaintWorldPositions(IList<Vector3> worldPositions, Color color)
    {
        if (worldPositions == null) return;
        foreach (var wp in worldPositions)
            PaintOverlayCell(WorldToOverlayCell(wp), color);
    }

    private void PaintGridPositions(IList<GridPosition> gps, Color color, RoomGrid grid)
    {
        if (gps == null || grid == null) return;
        foreach (var gp in gps)
            PaintOverlayCell(WorldToOverlayCell(grid.GetWorldPosition(gp)), color);
    }

    private void PaintOverlayCell(Vector3Int cell, Color color)
    {
        if (overlayTilemap == null || overlayTile == null) return;
        overlayTilemap.SetTile(cell, overlayTile);
        overlayTilemap.SetTileFlags(cell, TileFlags.None);
        overlayTilemap.SetColor(cell, color);
    }

    private Vector3Int WorldToOverlayCell(Vector3 worldPos)
    {
        if (overlayGrid != null) return overlayGrid.WorldToCell(worldPos);
        return new Vector3Int(
            Mathf.FloorToInt(worldPos.x),
            Mathf.FloorToInt(worldPos.y), 0);
    }

    // ── Public API ─────────────────────────────────────────────────────────

    private static Vector3 GetMouseWorldRaw()
    {
        if (Camera.main == null) return Vector3.zero;
        Vector3 raw = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        return new Vector3(raw.x, raw.y, 0f);
    }

    public void ShowPositions(List<GridPosition> positions, Color color)
    {
        var grid = FindLocalUnit()?.GetCurrentRoomGrid()
                ?? RoomManager.Instance?.GetCurrentRoomGrid();
        if (grid != null) PaintGridPositions(positions, color, grid);
    }

    public void HideAll() => SafeClear();

    public void ShowEnemyMoveRange(EnemyUnit enemy)
    {
        previewEnemy          = enemy;
        previewEnemyPositions = BuildEnemyMoveRange(enemy);
    }

    public void ClearEnemyPreview()
    {
        previewEnemy          = null;
        previewEnemyPositions = null;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static bool IsPlayerPhaseNow()
    {
        if (GameManager.IsMultiplayer)
            return NetworkedTurnSystem.Instance == null ||
                   NetworkedTurnSystem.Instance.IsPlayerPhase;
        return TurnSystem.Instance == null || TurnSystem.Instance.IsPlayerTurn;
    }

    /// <summary>Returns the local client's owned Unit (singleplayer or multiplayer).</summary>
    private static Unit FindLocalUnit()
    {
        // UnitActionSystem already tracks the owned unit — use it directly.
        var fromActionSystem = UnitActionSystem.Instance?.GetSelectedUnit();
        if (fromActionSystem != null) return fromActionSystem;

        // Fallback: search by ownership.
        if (!GameManager.IsMultiplayer)
            return FindAnyObjectByType<Unit>();

        foreach (var u in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            var net = u.GetComponent<Unity.Netcode.NetworkObject>();
            if (net != null && net.IsOwner) return u;
        }
        return null;
    }

    private List<GridPosition> BuildEnemyMoveRange(EnemyUnit enemy)
    {
        var positions = new List<GridPosition>();
        if (enemy?.Stats == null || enemy.CurrentRoomGrid == null) return positions;

        RoomGrid     room     = enemy.CurrentRoomGrid;
        GridPosition enemyPos = enemy.GridPosition;
        int          range    = enemy.Stats.moveRange;
        var          pf       = new Pathfinder(room);

        for (int dx = -range; dx <= range; dx++)
        for (int dy = -range; dy <= range; dy++)
        {
            if (Mathf.Abs(dx) + Mathf.Abs(dy) > range || (dx == 0 && dy == 0)) continue;
            var test = new GridPosition(enemyPos.x + dx, enemyPos.y + dy);
            if (!room.IsValidGridPosition(test))       continue;
            if (!room.IsWalkableIgnoreOccupancy(test)) continue;
            if (room.HasAnyUnitOnGridPosition(test))   continue;
            var path = pf.FindPath(enemyPos, test);
            if (path != null && path.Count > 0 && path.Count <= range)
                positions.Add(test);
        }
        return positions;
    }
}