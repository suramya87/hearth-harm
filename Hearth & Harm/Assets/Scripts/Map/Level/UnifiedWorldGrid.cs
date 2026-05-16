using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Singleton flat walkability graph spanning every room and hallway.
/// </summary>
public class UnifiedWorldGrid : MonoBehaviour
{
    public static UnifiedWorldGrid Instance { get; private set; }

    public class CellData
    {
        public RoomGrid OwnerGrid;
        public Vector3  WorldCentre;
        public bool     IsFloor;
    }

    private readonly Dictionary<Vector3Int, CellData>         cells          = new();
    private readonly Dictionary<Tilemap,    HashSet<Vector3Int>> registeredKeys = new();

    // Individual wall cells added by DoorStripBlocker (keyed by world key).
    private readonly HashSet<Vector3Int> doorWallKeys = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    // ── Tilemap registration ───────────────────────────────────────────────

    public void RegisterTilemap(Tilemap floorTilemap, RoomGrid ownerGrid,
                                Tilemap wallsTilemap = null)
    {
        if (floorTilemap == null || ownerGrid == null)
        {
            Debug.LogWarning("[UnifiedWorldGrid] RegisterTilemap: null arg, skipping.");
            return;
        }

        Unregister(floorTilemap);
        var keys = new HashSet<Vector3Int>();

        foreach (Vector3Int cell in floorTilemap.cellBounds.allPositionsWithin)
        {
            if (!floorTilemap.HasTile(cell)) continue;
            Vector3    centre = floorTilemap.GetCellCenterWorld(cell);
            Vector3Int key    = WorldKey(centre);
            cells[key] = new CellData { OwnerGrid = ownerGrid, WorldCentre = centre, IsFloor = true };
            keys.Add(key);
        }

        if (wallsTilemap != null)
        {
            foreach (Vector3Int cell in wallsTilemap.cellBounds.allPositionsWithin)
            {
                if (!wallsTilemap.HasTile(cell)) continue;
                Vector3    centre = wallsTilemap.GetCellCenterWorld(cell);
                Vector3Int key    = WorldKey(centre);
                if (cells.ContainsKey(key)) continue; // floor wins
                cells[key] = new CellData { OwnerGrid = ownerGrid, WorldCentre = centre, IsFloor = false };
                keys.Add(key);
            }
        }

        registeredKeys[floorTilemap] = keys;
        Debug.Log($"[UnifiedWorldGrid] +{keys.Count} cells from " +
                  $"'{floorTilemap.transform.parent?.name ?? floorTilemap.name}'. " +
                  $"Total: {cells.Count}");
    }

    public void Unregister(Tilemap tm)
    {
        if (tm == null || !registeredKeys.TryGetValue(tm, out var keys)) return;
        foreach (var k in keys) cells.Remove(k);
        registeredKeys.Remove(tm);
    }

    public void Clear()
    {
        cells.Clear();
        registeredKeys.Clear();
        doorWallKeys.Clear();
    }

    // ── Door wall cell registration ────────────────────────────────────────

    public void RegisterWallCell(Vector3 worldPos, RoomGrid ownerGrid)
    {
        var key = WorldKey(worldPos);
        doorWallKeys.Add(key);

        // Override existing entry or add a new wall entry.
        cells[key] = new CellData
        {
            OwnerGrid   = ownerGrid,
            WorldCentre = new Vector3(
                Mathf.Round(worldPos.x * 2f) * 0.5f,
                Mathf.Round(worldPos.y * 2f) * 0.5f,
                0f),
            IsFloor = false   
        };
    }

    public void UnregisterWallCell(Vector3 worldPos)
    {
        var key = WorldKey(worldPos);
        if (!doorWallKeys.Contains(key)) return;

        doorWallKeys.Remove(key);
        cells.Remove(key);

        foreach (var kvp in registeredKeys)
        {
            if (!kvp.Value.Contains(key)) continue;

            Tilemap tm = kvp.Key;
            if (tm == null) continue;

            Vector3Int localCell = tm.WorldToCell(worldPos);
            if (!tm.HasTile(localCell)) continue;

            Vector3 centre = tm.GetCellCenterWorld(localCell);

            cells[key] = new CellData
            {
                OwnerGrid   = GetOwnerAt(worldPos) ?? FindAnyRoomGrid(),
                WorldCentre = centre,
                IsFloor     = true   // floor is being restored
            };
            break;
        }
    }

    // ── Query ──────────────────────────────────────────────────────────────

    public bool IsWalkable(Vector3 worldPos)
    {
        if (!cells.TryGetValue(WorldKey(worldPos), out var d) || !d.IsFloor) return false;
        return d.OwnerGrid == null || d.OwnerGrid.IsWalkableAtWorld(d.WorldCentre);
    }

    public bool IsWalkableIgnoreOccupancy(Vector3 worldPos)
        => cells.TryGetValue(WorldKey(worldPos), out var d) && d.IsFloor;

    public bool HasCell(Vector3 worldPos) => cells.ContainsKey(WorldKey(worldPos));

    public CellData GetCell(Vector3 worldPos)
    { cells.TryGetValue(WorldKey(worldPos), out var d); return d; }

    public CellData GetCellByKey(Vector3Int key)
    { cells.TryGetValue(key, out var d); return d; }

    public RoomGrid GetOwnerAt(Vector3 worldPos)
    { cells.TryGetValue(WorldKey(worldPos), out var d); return d?.OwnerGrid; }

    public IReadOnlyDictionary<Vector3Int, CellData> AllCells => cells;

    // ── Neighbours ─────────────────────────────────────────────────────────
    private static readonly Vector3Int[] Steps =
    {
        new( 2,  0, 0), new(-2,  0, 0),
        new( 0,  2, 0), new( 0, -2, 0),
    };

    public List<Vector3Int> GetWalkableNeighbours(Vector3Int key, bool ignoreOccupancy = false)
    {
        var result = new List<Vector3Int>(4);
        foreach (var step in Steps)
        {
            var n = key + step;
            if (!cells.TryGetValue(n, out var d) || !d.IsFloor) continue;
            if (!ignoreOccupancy && d.OwnerGrid != null)
                if (!d.OwnerGrid.IsWalkableAtWorld(d.WorldCentre)) continue;
            result.Add(n);
        }
        return result;
    }

    // ── Key ────────────────────────────────────────────────────────────────

    public static Vector3Int WorldKey(Vector3 w) =>
        new(Mathf.FloorToInt(w.x * 2f + 0.01f),
            Mathf.FloorToInt(w.y * 2f + 0.01f),
            0);

    // ── Helpers ────────────────────────────────────────────────────────────

    private static RoomGrid FindAnyRoomGrid()
        => Object.FindAnyObjectByType<RoomGrid>();
}