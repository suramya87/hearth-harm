using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Bundles all tile assets used by HallwayTilemapPainter.
/// CREATE:  Assets → Create → Level Generation → Hallway Tile Set
/// </summary>
[CreateAssetMenu(menuName = "Level Generation/Hallway Tile Set", fileName = "HallwayTileSet")]
public class HallwayTileSet : ScriptableObject
{
    [Header("Floor — add as many variants as you like (picked randomly per cell)")]
    [Tooltip("Drag in your floor TileBase assets. Plain Tiles and Rule Tiles both work.")]
    public List<TileBase> FloorTiles = new();

    [Header("Walls")]
    [Tooltip("Create via Assets → Create → Level Generation → Hallway Wall Tile Set.")]
    public HallwayWallTileSet WallTileSet;

    /// <summary>Picks a random floor tile from the list.</summary>
    public TileBase GetRandomFloorTile()
    {
        if (FloorTiles == null || FloorTiles.Count == 0) return null;
        return FloorTiles[Random.Range(0, FloorTiles.Count)];
    }

    public bool IsValid => FloorTiles != null && FloorTiles.Count > 0 && WallTileSet != null;
}