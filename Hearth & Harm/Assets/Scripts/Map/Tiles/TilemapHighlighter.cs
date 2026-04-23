using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Paints coloured highlights directly onto the room's floor tilemap.
/// Replaces: GridSystemVisual, GridSystemVisualSingle, TilemapGridVisual,
///           NetworkedGridVisual, TilemapClickHandler.
///
/// SETUP
///   Attach to any persistent manager (e.g. GameManager) in your scene.
///   Assign solidWhiteTile (a plain white tile asset).
///   Nothing else required — it listens to UnitActionSystem and MouseWorld2D.
/// </summary>
public class TilemapHighlighter : MonoBehaviour
{
    public static TilemapHighlighter Instance { get; private set; }

    [Header("Tile asset — plain white TileBase")]
    [SerializeField] private TileBase solidWhiteTile;

    [Header("Default colors")]
    [SerializeField] private Color moveColor  = new(0.2f, 0.6f, 1f,  1f);
    [SerializeField] private Color rangeColor = new(1f,  0.85f, 0f,  1f);
    [SerializeField] private Color aoeColor   = new(1f,  0.15f, 0.15f, 1f);
    [SerializeField] private Color hoverColor = new(1f,  1f,   1f,  0.4f);

    // Tracking what we painted so we can undo it next frame
    private Tilemap                          paintedTilemap;
    private Dictionary<Vector3Int, TileBase> originalTiles  = new();
    private HashSet<Vector3Int>              painted         = new();

    private void Awake() => Instance = this;


    private void Start()
    {
        // Catch the case where RoomManager was already set before we subscribed
        if (paintedTilemap == null)
            RefreshTilemap();
    }


    private void OnEnable()
    {
        LevelGenerator.OnLevelReady += OnLevelReady;
        RoomManager.OnAnyRoomChanged += OnRoomChanged;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady -= OnLevelReady;
        RoomManager.OnAnyRoomChanged -= OnRoomChanged;
        ResetAll();
    }

    private void OnLevelReady() => RefreshTilemap();
    private void OnRoomChanged(LevelGenerator.PlacedRoom _) => RefreshTilemap();
    // private void OnRoomChanged(LevelGenerator.PlacedRoom _) => RefreshTilemap();

    private void RefreshTilemap()
    {
        ResetAll();
        var grid = RoomManager.Instance?.GetCurrentRoom()?.roomGrid;
        paintedTilemap = grid?.GetFloorTilemap();
        
        // In multiplayer, also try finding the local player's room directly
        if (paintedTilemap == null && GameManager.IsMultiplayer)
            TryFindLocalPlayerRoom();
    }

    private void TryFindLocalPlayerRoom()
    {
        foreach (var bridge in FindObjectsByType<NetworkedPlayerBridge>(FindObjectsSortMode.None))
        {
            if (!bridge.IsOwner) continue;
            // The bridge stores gridX/gridY — find which room contains that position
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

    private void Update()
    {
        var room = RoomManager.Instance?.GetCurrentRoomGrid();
        var tilemap = room?.GetFloorTilemap();

        // Debug.Log($"[Highlighter] tilemap={tilemap != null} painted={paintedTilemap != null} " +
            //   $"tile={solidWhiteTile != null} action={UnitActionSystem.Instance?.GetSelectedAction()?.GetType().Name ?? "null"}");

        if (tilemap != paintedTilemap)
        {
            ResetAll();
            paintedTilemap = tilemap;
        }

        if (paintedTilemap == null) return;

        ResetAll();

        if (GridCostVisualizer.Instance != null)
            GridCostVisualizer.Instance.ClearAll();

        var action = UnitActionSystem.Instance?.GetSelectedAction();
        if (action == null) return;

        if (action is MoveAction move)
        {
            Paint(move.GetValidActionGridPositionList(), moveColor);

            if (room != null)
            {
                var mouseGP = room.GetGridPosition(MouseWorld2D.GetPosition());

                if (move.IsValidTarget(mouseGP))
                {
                    int cost = move.GetMoveCost(mouseGP);
                    if (cost >= 0 && GridCostVisualizer.Instance != null)
                    {
                        GridCostVisualizer.Instance.ShowCost(mouseGP, cost);
                    }
                }
            }
        }
        else if (action is CombatAction combat)
        {
            Color rc = combat.ActionData != null ? combat.ActionData.rangeHighlightColor : rangeColor;
            Color ac = combat.ActionData != null ? combat.ActionData.aoeHighlightColor : aoeColor;

            Paint(combat.GetValidActionGridPositionList(), rc);

            if (room != null)
            {
                var mouseGP = room.GetGridPosition(MouseWorld2D.GetPosition());
                Paint(combat.GetPreviewPositions(mouseGP), ac);
            }
        }

        // Hover
        if (room != null)
        {
            var gp = room.GetGridPosition(MouseWorld2D.GetPosition());
            if (room.IsValidGridPosition(gp))
                PaintCell(new Vector3Int(gp.x, gp.y, 0), hoverColor);
        }
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

    // ── Public API (called from action bar tests etc.) ─────────────────────

    public void ShowPositions(List<GridPosition> positions, Color color)
    {
        if (paintedTilemap == null) return;
        Paint(positions, color);
    }

    public void HideAll() => ResetAll();

    

}