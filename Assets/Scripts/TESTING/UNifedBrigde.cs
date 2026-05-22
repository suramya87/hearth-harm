using UnityEngine;
using UnityEngine.Tilemaps;


public class LevelGeneratorUnifiedGridBridge : MonoBehaviour
{
    private void OnEnable()  => LevelGenerator.OnLevelReady += OnLevelReady;
    private void OnDisable() => LevelGenerator.OnLevelReady -= OnLevelReady;

    private void OnLevelReady()
    {
        EnsureUnifiedGridExists();
        UnifiedWorldGrid.Instance.Clear();
        RegisterAllRooms();
        RegisterAllHallways();

        Debug.Log($"[UnifiedGridBridge] Done. " +
                  $"Total cells: {UnifiedWorldGrid.Instance.AllCells.Count}");
    }

    // ── Ensure singleton ───────────────────────────────────────────────────

    private static void EnsureUnifiedGridExists()
    {
        if (UnifiedWorldGrid.Instance != null) return;

        var go = new GameObject("UnifiedWorldGrid");
        go.AddComponent<UnifiedWorldGrid>();
        Debug.Log("[UnifiedGridBridge] Created UnifiedWorldGrid singleton.");
    }

    // ── Register rooms ─────────────────────────────────────────────────────

    private static void RegisterAllRooms()
    {
        int count = 0;
        foreach (var setup in FindObjectsByType<RoomTilemapSetup>(FindObjectsSortMode.None))
        {
            if (!setup.IsInitialized) continue;

            var roomGrid = setup.GetComponent<RoomGrid>();
            if (roomGrid == null || !roomGrid.IsInitialized()) continue;

            Tilemap floor = roomGrid.GetFloorTilemap();
            Tilemap walls = roomGrid.GetWallsTilemap();

            if (floor == null)
            {
                Debug.LogWarning($"[UnifiedGridBridge] Room {setup.name} has no floor tilemap.");
                continue;
            }

            UnifiedWorldGrid.Instance.RegisterTilemap(floor, roomGrid, walls);
            count++;
        }
        Debug.Log($"[UnifiedGridBridge] Registered {count} rooms.");
    }

    // ── Register hallways ──────────────────────────────────────────────────

    private static void RegisterAllHallways()
    {
        int count = 0;
        foreach (var hg in FindObjectsByType<HallwayGrid>(FindObjectsSortMode.None))
        {
            if (!hg.IsReady) continue;

            Tilemap floor = hg.FloorTilemap;
            Tilemap walls = hg.WallsTilemap;
            RoomGrid rg   = hg.RoomGrid;

            if (floor == null || rg == null)
            {
                Debug.LogWarning($"[UnifiedGridBridge] Hallway {hg.name} missing floor or RoomGrid.");
                continue;
            }

            UnifiedWorldGrid.Instance.RegisterTilemap(floor, rg, walls);
            count++;
        }
        Debug.Log($"[UnifiedGridBridge] Registered {count} hallways.");
    }
}