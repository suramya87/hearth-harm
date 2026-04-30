using UnityEngine;
using UnityEngine.Tilemaps;

[CreateAssetMenu(fileName = "SpawnPointTile", menuName = "Tiles/SpawnPoint Tile")]
public class SpawnPointTile : Tile
{
    [Tooltip("Entry direction this spawn point covers. " +
             "North = player entered through the north door.")]
    public LevelGenerator.Direction entryDirection;
}