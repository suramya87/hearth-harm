using System.Collections.Generic;
using UnityEngine;


public class Pathfinder
{
    private class Node
    {
        public GridPosition pos;
        public Node         parent;
        public int          g, h;
        public int          f => g + h;
        public Node(GridPosition pos, Node parent, int g, int h)
        { this.pos = pos; this.parent = parent; this.g = g; this.h = h; }
    }

    private readonly RoomGrid room;

    public Pathfinder(RoomGrid room) => this.room = room;

    // ── Public API ─────────────────────────────────────────────────────────

    public List<GridPosition> FindPath(GridPosition start, GridPosition end,
                                       bool ignoreOccupancy = false)
    {
        var open   = new List<Node>();
        var closed = new HashSet<GridPosition>();

        open.Add(new Node(start, null, 0, Heuristic(start, end)));

        int limit = room.GetWidth() * room.GetHeight();
        int iter  = 0;

        while (open.Count > 0 && iter++ < limit)
        {
            Node current = Best(open);
            open.Remove(current);
            closed.Add(current.pos);

            if (current.pos == end) return BuildPath(current);

            foreach (GridPosition nb in Neighbours(current.pos))
            {
                if (closed.Contains(nb))                  continue;
                if (!room.IsValidGridPosition(nb))         continue;
                if (room.IsWall(nb))                       continue;
                if (!ignoreOccupancy && nb != end && room.HasAnyUnitOnGridPosition(nb)) continue;

                int  newG    = current.g + 1;
                Node existing = open.Find(n => n.pos == nb);
                if (existing == null)
                    open.Add(new Node(nb, current, newG, Heuristic(nb, end)));
                else if (newG < existing.g)
                { existing.parent = current; existing.g = newG; }
            }
        }

        return new List<GridPosition>();
    }


    public List<GridPosition> FindPathToRange(GridPosition start, GridPosition target,
                                              int attackRange)
    {
        if (Heuristic(start, target) <= attackRange) return new();

        var candidates = new List<GridPosition>();
        for (int dx = -attackRange; dx <= attackRange; dx++)
        for (int dy = -attackRange; dy <= attackRange; dy++)
        {
            if (Mathf.Abs(dx) + Mathf.Abs(dy) != attackRange) continue;
            var c = new GridPosition(target.x + dx, target.y + dy);
            if (room.IsValidGridPosition(c) && room.IsWalkableIgnoreOccupancy(c))
                candidates.Add(c);
        }

        candidates.Sort((a, b) => Heuristic(start, a) - Heuristic(start, b));

        foreach (var c in candidates)
        {
            var path = FindPath(start, c);
            if (path.Count > 0) return path;
        }

        return FindPath(start, target, ignoreOccupancy: true);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static int Heuristic(GridPosition a, GridPosition b) =>
        Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    private static Node Best(List<Node> list)
    {
        Node best = list[0];
        foreach (var n in list)
            if (n.f < best.f || (n.f == best.f && n.h < best.h)) best = n;
        return best;
    }

    private static List<GridPosition> BuildPath(Node end)
    {
        var path = new List<GridPosition>();
        for (var n = end; n.parent != null; n = n.parent) path.Add(n.pos);
        path.Reverse();
        return path;
    }

    private static readonly GridPosition[] Dirs =
    {
        new(1,0), new(-1,0), new(0,1), new(0,-1)
    };
    private static IEnumerable<GridPosition> Neighbours(GridPosition p)
    {
        foreach (var d in Dirs) yield return p + d;
    }
}