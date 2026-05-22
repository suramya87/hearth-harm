using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tile placed in the Start room to mark where the player spawns.
/// </summary>
[CreateAssetMenu(menuName = "Tiles/Player Spawn Tile", fileName = "PlayerSpawnTile")]
public class PlayerSpawnTile : TileBase
{
    public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
    {
        tileData.color = new Color(0f, 1f, 0f, 0.5f); // semi-transparent green in editor
    }
}