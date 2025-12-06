using RTS.Simulation.Data;
using RTS.Simulation.Core; // SimGameContext için
using System.Collections.Generic;
using System;

namespace RTS.Simulation.Systems
{
    public class SimGridSystem
    {
        // --- Instance Yapısı ---
        private SimWorldState _world;

        public int Width => _world.Map.Width;
        public int Height => _world.Map.Height;

        public SimGridSystem(SimWorldState world = null)
        {
            _world = world ?? SimGameContext.ActiveWorld;
        }

        // --- Instance Wrappers ---
        public bool IsWalkable(int2 pos) => IsWalkable(_world, pos);
        public SimMapNode GetNode(int x, int y) => GetNode(_world, x, y);
        // -------------------------

        // --- STATİK FONKSİYONLAR ---

        public static SimMapNode GetNode(SimWorldState world, int x, int y)
        {
            // DÜZELTME: IsValid yerine IsInBounds kullanıyoruz
            if (!world.Map.IsInBounds(new int2(x, y))) return null;
            return world.Map.Grid[x, y];
        }

        public static bool IsWalkable(SimWorldState world, int2 pos)
        {
            if (!world.Map.IsInBounds(pos)) return false;
            var node = world.Map.Grid[pos.x, pos.y];
            return node.IsWalkable && node.OccupantID == -1;
        }

        public static int2? FindWalkableNeighbor(SimWorldState world, int2 center)
        {
            int2[] cardinals = { new int2(0, 1), new int2(0, -1), new int2(1, 0), new int2(-1, 0) };
            foreach (var dir in cardinals)
            {
                int2 p = new int2(center.x + dir.x, center.y + dir.y);
                if (IsWalkable(world, p)) return p;
            }
            int2[] diagonals = { new int2(1, 1), new int2(1, -1), new int2(-1, 1), new int2(-1, -1) };
            foreach (var dir in diagonals)
            {
                int2 p = new int2(center.x + dir.x, center.y + dir.y);
                if (IsWalkable(world, p)) return p;
            }
            return null;
        }

        public static int GetDistance(int2 a, int2 b)
        {
            return Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);
        }

        public static List<int2> FindPath(SimWorldState world, int2 start, int2 end)
        {
            if (!world.Map.IsInBounds(start) || !world.Map.IsInBounds(end)) return new List<int2>();
            if (start == end) return new List<int2>();

            var openSet = new List<int2> { start };
            var cameFrom = new Dictionary<int2, int2>();
            var gScore = new Dictionary<int2, int>();
            gScore[start] = 0;
            var fScore = new Dictionary<int2, int>();
            fScore[start] = GetDistance(start, end);

            int safetyLoop = 3000;

            while (openSet.Count > 0 && safetyLoop > 0)
            {
                safetyLoop--;
                int2 current = openSet[0];
                int currentF = fScore.ContainsKey(current) ? fScore[current] : int.MaxValue;

                for (int i = 1; i < openSet.Count; i++)
                {
                    int score = fScore.ContainsKey(openSet[i]) ? fScore[openSet[i]] : int.MaxValue;
                    if (score < currentF)
                    {
                        current = openSet[i];
                        currentF = score;
                    }
                }

                if (current == end) return ReconstructPath(cameFrom, current);

                openSet.Remove(current);

                foreach (var neighbor in GetNeighbors(current))
                {
                    if (!IsWalkable(world, neighbor) && neighbor != end) continue;

                    int tentativeGScore = gScore[current] + 1;
                    if (!gScore.ContainsKey(neighbor) || tentativeGScore < gScore[neighbor])
                    {
                        cameFrom[neighbor] = current;
                        gScore[neighbor] = tentativeGScore;
                        fScore[neighbor] = tentativeGScore + GetDistance(neighbor, end);
                        if (!openSet.Contains(neighbor)) openSet.Add(neighbor);
                    }
                }
            }
            return new List<int2>();
        }

        private static List<int2> ReconstructPath(Dictionary<int2, int2> cameFrom, int2 current)
        {
            var totalPath = new List<int2> { current };
            while (cameFrom.ContainsKey(current))
            {
                current = cameFrom[current];
                totalPath.Add(current);
            }
            totalPath.Reverse();
            if (totalPath.Count > 0) totalPath.RemoveAt(0);
            return totalPath;
        }

        private static IEnumerable<int2> GetNeighbors(int2 pos)
        {
            yield return new int2(pos.x + 1, pos.y);
            yield return new int2(pos.x - 1, pos.y);
            yield return new int2(pos.x, pos.y + 1);
            yield return new int2(pos.x, pos.y - 1);
            yield return new int2(pos.x + 1, pos.y + 1);
            yield return new int2(pos.x + 1, pos.y - 1);
            yield return new int2(pos.x - 1, pos.y + 1);
            yield return new int2(pos.x - 1, pos.y - 1);
        }

        public static void GenerateMazeMap(SimMapData map)
        {
            int[,] maze = new int[,]
            {
                { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
                { 1, 0, 0, 0, 0, 0, 0, 0, 0, 1 },
                { 1, 0, 1, 1, 1, 1, 1, 1, 0, 1 },
                { 1, 0, 1, 0, 0, 0, 0, 1, 0, 1 },
                { 1, 0, 1, 0, 1, 1, 0, 1, 0, 1 },
                { 1, 0, 1, 0, 1, 1, 0, 1, 0, 1 },
                { 1, 0, 0, 0, 0, 0, 0, 0, 0, 1 },
                { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }
            };
            int rows = maze.GetLength(0);
            int cols = maze.GetLength(1);

            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    var node = map.Grid[x, y];
                    node.OccupantID = -1;
                    if (x < rows && y < cols)
                    {
                        if (maze[x, y] == 1) { node.Type = SimTileType.Stone; node.IsWalkable = false; }
                        else { node.Type = SimTileType.Grass; node.IsWalkable = true; }
                    }
                    else { node.Type = SimTileType.Stone; node.IsWalkable = false; }
                }
            }
        }

        public static float GetDistanceSq(int2 a, int2 b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            return (dx * dx) + (dy * dy);
        }
    }
}