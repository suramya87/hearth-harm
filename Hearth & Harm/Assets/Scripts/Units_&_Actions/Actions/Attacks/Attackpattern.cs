using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewAttackPattern", menuName = "Combat/Attack Pattern")]
public class AttackPattern : ScriptableObject
{
    [Serializable]
    public class PatternTile
    {
        [Tooltip("Y+ = Forward  Y- = Behind\nX+ = Right   X- = Left")]
        public Vector2Int offset;
        public PatternTile(int x, int y) { offset = new(x, y); }
    }

    [Header("Pattern Shape")]
    public List<PatternTile> tiles = new();

    [Header("Range")]
    [Min(0)] public int minRange;
    [Min(0)] public int maxRange;

#if UNITY_EDITOR
    [Header("Preview (read-only)")]
    [SerializeField, TextArea(2,4)] private string _info;
    private void OnValidate()
    {
        if (maxRange < minRange) maxRange = minRange;
        _info = $"Tiles: {tiles?.Count ?? 0}  Range: {minRange}-{maxRange} " +
                $"({(maxRange == 0 ? "MELEE" : "RANGED")})";
    }
#endif

    public List<GridPosition> GetAffectedPositions(GridPosition origin, Vector2Int facing,
                                                   int originOffset = 0)
    {
        var result = new List<GridPosition>();
        var shift  = facing * originOffset;
        foreach (var tile in tiles)
        {
            var r = RotateOffset(tile.offset, facing);
            result.Add(new GridPosition(origin.x + shift.x + r.x, origin.y + shift.y + r.y));
        }
        return result;
    }

    // North(0,1)=no rot | East(1,0)=90CW | South(0,-1)=180 | West(-1,0)=90CCW
    private static Vector2Int RotateOffset(Vector2Int o, Vector2Int facing)
    {
        if (facing == new Vector2Int( 0,  1)) return new( o.x,  o.y);
        if (facing == new Vector2Int( 1,  0)) return new( o.y, -o.x);
        if (facing == new Vector2Int( 0, -1)) return new(-o.x, -o.y);
        if (facing == new Vector2Int(-1,  0)) return new(-o.y,  o.x);
        return o;
    }

    public static AttackPattern CreateSingleFront()
    { var p = CreateInstance<AttackPattern>(); p.name="SingleFront"; p.tiles.Add(new(0,1)); return p; }

    public static AttackPattern CreateFrontArc()
    { var p = CreateInstance<AttackPattern>(); p.name="FrontArc";
      p.tiles.Add(new(-1,1)); p.tiles.Add(new(0,1)); p.tiles.Add(new(1,1)); return p; }

    public static AttackPattern CreateDiamond(int r)
    { var p = CreateInstance<AttackPattern>(); p.name=$"Diamond_{r}";
      for(int x=-r;x<=r;x++) for(int y=-r;y<=r;y++) if(Mathf.Abs(x)+Mathf.Abs(y)<=r) p.tiles.Add(new(x,y));
      return p; }
}