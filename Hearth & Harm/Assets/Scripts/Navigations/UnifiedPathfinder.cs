using System.Collections.Generic;
using UnityEngine;


public static class UnifiedPathfinder
{
    // ── Public data ────────────────────────────────────────────────────────

    public struct WorldStep
    {
        public Vector3  WorldPos;
        public RoomGrid OwnerGrid;
    }

    // ── Node ───────────────────────────────────────────────────────────────

    private class Node
    {
        public Vector3Int Key;
        public Node       Parent;
        public int        G, H;
        public int        F => G + H;

        public Node(Vector3Int key, Node parent, int g, int h)
        { Key = key; Parent = parent; G = g; H = h; }
    }

    // ── Primary API: returns world steps with owner grids ──────────────────

    /// <summary>
    /// Find a cross-grid path from startWorld to endWorld.
    /// Returns null if no path exists; empty list if already at destination.
    /// </summary>
    public static List<WorldStep> FindWorldPath(Vector3 startWorld, Vector3 endWorld,
                                                bool ignoreOccupancy = false)
    {
        var grid = UnifiedWorldGrid.Instance;
        if (grid == null) return null;

        Vector3Int startKey = UnifiedWorldGrid.WorldKey(startWorld);
        Vector3Int endKey   = UnifiedWorldGrid.WorldKey(endWorld);

        if (startKey == endKey) return new List<WorldStep>();

        var endData = grid.GetCell(endWorld);
        if (endData == null || !endData.IsFloor) return null;

        Node goal = RunAStar(startKey, endKey, ignoreOccupancy, grid);
        if (goal == null) return null;

        return BuildWorldPath(goal, grid);
    }

    // ── Secondary API: plain world positions (used by Pathfinder.cs) ──────

    /// <summary>
    /// Same search, returns only world positions.
    /// Pathfinder.FindPathUnified() converts these to GridPositions.
    /// </summary>
    public static List<Vector3> FindPath(Vector3 startWorld, Vector3 endWorld,
                                         bool ignoreOccupancy = false)
    {
        var steps = FindWorldPath(startWorld, endWorld, ignoreOccupancy);
        if (steps == null) return null;

        var result = new List<Vector3>(steps.Count);
        foreach (var s in steps) result.Add(s.WorldPos);
        return result;
    }

    // ── A* core ────────────────────────────────────────────────────────────

    private static Node RunAStar(Vector3Int startKey, Vector3Int endKey,
                                 bool ignoreOccupancy, UnifiedWorldGrid grid)
    {
        var open   = new List<Node>();
        var closed = new HashSet<Vector3Int>();
        var lookup = new Dictionary<Vector3Int, Node>();

        var startNode = new Node(startKey, null, 0, Heuristic(startKey, endKey));
        open.Add(startNode);
        lookup[startKey] = startNode;

        int limit = grid.AllCells.Count;
        int iter  = 0;

        while (open.Count > 0 && iter++ < limit)
        {
            Node current = Best(open);
            open.Remove(current);
            closed.Add(current.Key);

            if (current.Key == endKey) return current;

            foreach (var nKey in grid.GetWalkableNeighbours(current.Key, ignoreOccupancy))
            {
                if (closed.Contains(nKey)) continue;

                int newG = current.G + 1;

                if (lookup.TryGetValue(nKey, out var existing))
                {
                    if (newG < existing.G)
                    {
                        existing.Parent = current;
                        existing.G      = newG;
                    }
                }
                else
                {
                    var node = new Node(nKey, current, newG, Heuristic(nKey, endKey));
                    open.Add(node);
                    lookup[nKey] = node;
                }
            }
        }

        return null;
    }

    // ── Path reconstruction ────────────────────────────────────────────────

    private static List<WorldStep> BuildWorldPath(Node end, UnifiedWorldGrid grid)
    {
        var path = new List<WorldStep>();
        for (var n = end; n.Parent != null; n = n.Parent)
        {
            var data = grid.GetCell(new Vector3(n.Key.x, n.Key.y, 0));
            path.Add(new WorldStep
            {
                WorldPos  = data?.WorldCentre ?? new Vector3(n.Key.x, n.Key.y, 0),
                OwnerGrid = data?.OwnerGrid,
            });
        }
        path.Reverse();
        return path;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static int Heuristic(Vector3Int a, Vector3Int b) =>
        Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    private static Node Best(List<Node> list)
    {
        Node best = list[0];
        foreach (var n in list)
            if (n.F < best.F || (n.F == best.F && n.H < best.H)) best = n;
        return best;
    }
}