using System;


[Serializable]
public struct GridPosition : IEquatable<GridPosition>
{
    public int x;
    public int y;          
    public int z { get => y; set => y = value; }

    public GridPosition(int x, int y) { this.x = x; this.y = y; }

    public override bool   Equals(object obj)         => obj is GridPosition p && this == p;
    public          bool   Equals(GridPosition other) => this == other;
    public override int    GetHashCode()               => HashCode.Combine(x, y);
    public override string ToString()                  => $"({x}, {y})";

    public static bool         operator ==(GridPosition a, GridPosition b) => a.x == b.x && a.y == b.y;
    public static bool         operator !=(GridPosition a, GridPosition b) => !(a == b);
    public static GridPosition operator + (GridPosition a, GridPosition b) => new(a.x + b.x, a.y + b.y);
    public static GridPosition operator - (GridPosition a, GridPosition b) => new(a.x - b.x, a.y - b.y);

    public int ManhattanDistance(GridPosition other) =>
        Math.Abs(x - other.x) + Math.Abs(y - other.y);
}