using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tile placed in the Start room to mark where the player spawns.
///
/// WHY A SEPARATE TILE TYPE?
///   SpawnPointTile is used by the hallway system to find room entry positions —
///   the cells players land on when walking in from a corridor. If LevelGenerator
///   uses those same tiles for the initial player spawn it picks a hallway-entry
///   cell which is right next to the door, often overlapping with the hallway's
///   walk trigger and immediately sending the player into a hallway on frame 1.
///
///   PlayerSpawnTile is placed in the centre of the Start room (or wherever you
///   want the player to begin) and is never used by the hallway system.
///
/// HOW TO USE:
///   1. Create asset: Assets → Create → Tiles → Player Spawn Tile
///   2. Paint exactly one instance onto the Start room's SpawnPoints tilemap
///      (or a dedicated PlayerSpawn tilemap layer — either works as long as you
///      name it consistently with what RoomSpawnPointReader scans).
///   3. LevelGenerator.FindSpawnTileFromSpawnPoints() checks for this type
///      first before falling back to SpawnPointTile positions.
/// </summary>
[CreateAssetMenu(menuName = "Tiles/Player Spawn Tile", fileName = "PlayerSpawnTile")]
public class PlayerSpawnTile : TileBase
{
    // No extra data needed — the tile's presence in the tilemap is the signal.
    // Override GetTileData if you want a specific sprite in the editor.
    public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
    {
        tileData.color = new Color(0f, 1f, 0f, 0.5f); // semi-transparent green in editor
    }
}