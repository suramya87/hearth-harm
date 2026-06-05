using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tile asset placed on the "SpawnPoints" tilemap layer in room prefabs.
/// Each tile marks where the player spawns when entering from a given direction.
/// Create four of these: Assets > Create > Tiles > SpawnPoint Tile
/// </summary>
[CreateAssetMenu(fileName = "SpawnPointTile", menuName = "Tiles/SpawnPoint Tile")]
public class SpawnPointTile : Tile
{
    [Tooltip("Entry direction this spawn point covers. " +
             "North = player entered through the north door.")]
    public LevelGenerator.Direction entryDirection;
}