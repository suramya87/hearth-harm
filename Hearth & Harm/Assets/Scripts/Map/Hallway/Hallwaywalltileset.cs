using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// ScriptableObject that holds one sprite per wall direction / corner.
/// Drag your individual wall sprites into the slots in the Inspector.
///
/// CREATE:  Assets → Create → Level Generation → Hallway Wall Tile Set
///
/// SPRITE SLOTS
///   The names match the standard 8-direction wall set used by most 2-D tilesets.
///   Leave a slot empty if you don't have that variant — the painter will fall
///   back to the closest match.
///
///   Straight edges          Corners (convex = outer, concave = inner)
///   ─────────────           ──────────────────────────────────────────
///   Top                     TopLeft_Convex      TopLeft_Concave
///   Bottom                  TopRight_Convex     TopRight_Concave
///   Left                    BottomLeft_Convex   BottomLeft_Concave
///   Right                   BottomRight_Convex  BottomRight_Concave
///
///   End caps (hallway terminates — no neighbour on one side)
///   ─────────────────────────────────────────────────────────
///   EndCap_Top / Bottom / Left / Right
/// </summary>
[CreateAssetMenu(menuName = "Level Generation/Hallway Wall Tile Set",
                 fileName  = "HallwayWallTileSet")]
public class HallwayWallTileSet : ScriptableObject
{
    [Header("Straight edges")]
    public Sprite Top;
    public Sprite Bottom;
    public Sprite Left;
    public Sprite Right;

    [Header("Convex (outer) corners")]
    public Sprite TopLeft_Convex;
    public Sprite TopRight_Convex;
    public Sprite BottomLeft_Convex;
    public Sprite BottomRight_Convex;

    [Header("Concave (inner) corners — used at bends")]
    public Sprite TopLeft_Concave;
    public Sprite TopRight_Concave;
    public Sprite BottomLeft_Concave;
    public Sprite BottomRight_Concave;

    [Header("End caps (open end of corridor)")]
    public Sprite EndCap_Top;
    public Sprite EndCap_Bottom;
    public Sprite EndCap_Left;
    public Sprite EndCap_Right;

    // ── Sprite lookup ──────────────────────────────────────────────────────

    public enum WallType
    {
        Top, Bottom, Left, Right,
        TopLeft_Convex,    TopRight_Convex,
        BottomLeft_Convex, BottomRight_Convex,
        TopLeft_Concave,    TopRight_Concave,
        BottomLeft_Concave, BottomRight_Concave,
        EndCap_Top, EndCap_Bottom, EndCap_Left, EndCap_Right
    }

    /// <summary>
    /// Returns the sprite for a given wall type.
    /// Falls back gracefully: concave → convex → straight edge → null.
    /// </summary>
    public Sprite Get(WallType type)
    {
        return type switch
        {
            WallType.Top    => Top    ?? Left,
            WallType.Bottom => Bottom ?? Left,
            WallType.Left   => Left   ?? Top,
            WallType.Right  => Right  ?? Top,

            WallType.TopLeft_Convex     => TopLeft_Convex     ?? Top,
            WallType.TopRight_Convex    => TopRight_Convex    ?? Top,
            WallType.BottomLeft_Convex  => BottomLeft_Convex  ?? Bottom,
            WallType.BottomRight_Convex => BottomRight_Convex ?? Bottom,

            WallType.TopLeft_Concave     => TopLeft_Concave     ?? TopLeft_Convex     ?? Top,
            WallType.TopRight_Concave    => TopRight_Concave    ?? TopRight_Convex    ?? Top,
            WallType.BottomLeft_Concave  => BottomLeft_Concave  ?? BottomLeft_Convex  ?? Bottom,
            WallType.BottomRight_Concave => BottomRight_Concave ?? BottomRight_Convex ?? Bottom,

            WallType.EndCap_Top    => EndCap_Top    ?? Top,
            WallType.EndCap_Bottom => EndCap_Bottom ?? Bottom,
            WallType.EndCap_Left   => EndCap_Left   ?? Left,
            WallType.EndCap_Right  => EndCap_Right  ?? Right,

            _ => null
        };
    }
}