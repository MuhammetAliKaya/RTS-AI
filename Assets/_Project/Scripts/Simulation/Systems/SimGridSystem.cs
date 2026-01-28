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

        private static readonly int2[] _neighborOffsets = new int2[] {
    new int2(1, 0), new int2(-1, 0), new int2(0, 1), new int2(0, -1),
    new int2(1, 1), new int2(1, -1), new int2(-1, 1), new int2(-1, -1)
};

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

        // public static List<int2> FindPath(SimWorldState world, int2 start, int2 end)
        // {
        //     if (!world.Map.IsInBounds(start) || !world.Map.IsInBounds(end)) return new List<int2>();
        //     if (start == end) return new List<int2>();

        //     var openSet = new List<int2> { start };
        //     var cameFrom = new Dictionary<int2, int2>();
        //     var gScore = new Dictionary<int2, int>();
        //     gScore[start] = 0;
        //     var fScore = new Dictionary<int2, int>();
        //     fScore[start] = GetDistance(start, end);

        //     int safetyLoop = 3000;

        //     while (openSet.Count > 0 && safetyLoop > 0)
        //     {
        //         safetyLoop--;
        //         int2 current = openSet[0];
        //         int currentF = fScore.ContainsKey(current) ? fScore[current] : int.MaxValue;

        //         for (int i = 1; i < openSet.Count; i++)
        //         {
        //             int score = fScore.ContainsKey(openSet[i]) ? fScore[openSet[i]] : int.MaxValue;
        //             if (score < currentF)
        //             {
        //                 current = openSet[i];
        //                 currentF = score;
        //             }
        //         }

        //         if (current == end) return ReconstructPath(cameFrom, current);

        //         openSet.Remove(current);

        //         foreach (var neighbor in GetNeighbors(current))
        //         {
        //             if (!IsWalkable(world, neighbor) && neighbor != end) continue;

        //             int tentativeGScore = gScore[current] + 1;
        //             if (!gScore.ContainsKey(neighbor) || tentativeGScore < gScore[neighbor])
        //             {
        //                 cameFrom[neighbor] = current;
        //                 gScore[neighbor] = tentativeGScore;
        //                 fScore[neighbor] = tentativeGScore + GetDistance(neighbor, end);
        //                 if (!openSet.Contains(neighbor)) openSet.Add(neighbor);
        //             }
        //         }
        //     }
        //     return new List<int2>();
        // }

        public static List<int2> FindPath(SimWorldState world, int2 start, int2 end)
        {
            if (world == null || world.Map == null || world.Map.Width <= 0) return null;

            if (!world.Map.IsInBounds(start) || !world.Map.IsInBounds(end)) return null;
            if (start.Equals(end)) return new List<int2>();

            int width = world.Map.Width;
            int maxNodes = width * world.Map.Height;

            // DEĞİŞİKLİK 1: Dictionary yerine Array (Çok daha hızlı erişim)
            int[] gScore = new int[maxNodes];
            int[] cameFrom = new int[maxNodes];
            bool[] closedSet = new bool[maxNodes];

            // Arrayleri temizle (Varsayılan değerler)
            Array.Fill(gScore, int.MaxValue);
            Array.Fill(cameFrom, -1);

            int startIndex = start.y * width + start.x;
            int endIndex = end.y * width + end.x;

            gScore[startIndex] = 0;

            // DEĞİŞİKLİK 2: List<int2> yerine MinHeap (En küçüğü anında bulur)
            MinHeap openSet = new MinHeap(maxNodes);
            openSet.Push(startIndex, GetChebyshevDistance(start, end));

            while (openSet.Count > 0)
            {
                int currentIdx = openSet.Pop(); // O(1) işlemle en iyiyi al

                if (currentIdx == endIndex) return ReconstructPath(cameFrom, endIndex, width);

                closedSet[currentIdx] = true;
                int cx = currentIdx % width;
                int cy = currentIdx / width;

                // DEĞİŞİKLİK 3: Foreach yerine for döngüsü (GC Alloc yok)
                for (int i = 0; i < 8; i++)
                {
                    int nx = cx + _neighborOffsets[i].x;
                    int ny = cy + _neighborOffsets[i].y;

                    if (nx < 0 || nx >= width || ny < 0 || ny >= world.Map.Height) continue;

                    int neighborIdx = ny * width + nx;
                    if (closedSet[neighborIdx]) continue;

                    // Yürünebilirlik kontrolü
                    var node = world.Map.Grid[nx, ny];
                    bool isTarget = (neighborIdx == endIndex);
                    if (!node.IsWalkable || (node.OccupantID != -1 && !isTarget)) continue;

                    int tentativeG = gScore[currentIdx] + 1;
                    if (tentativeG < gScore[neighborIdx])
                    {
                        cameFrom[neighborIdx] = currentIdx;
                        gScore[neighborIdx] = tentativeG;
                        // F-Score heap içinde tutuluyor, tekrar hesaplayıp pushluyoruz
                        openSet.Push(neighborIdx, tentativeG + GetChebyshevDistance(new int2(nx, ny), end));
                    }
                }
            }
            return null;
        }

        public static int GetChebyshevDistance(int2 a, int2 b)
        {
            return Math.Max(Math.Abs(a.x - b.x), Math.Abs(a.y - b.y));
        }

        // private static List<int2> ReconstructPath(Dictionary<int2, int2> cameFrom, int2 current)
        // {
        //     var totalPath = new List<int2> { current };
        //     while (cameFrom.ContainsKey(current))
        //     {
        //         current = cameFrom[current];
        //         totalPath.Add(current);
        //     }
        //     totalPath.Reverse();
        //     if (totalPath.Count > 0) totalPath.RemoveAt(0);
        //     return totalPath;
        // }

        private static List<int2> ReconstructPath(int[] cameFrom, int currentIdx, int width)
        {
            if (width <= 0) return new List<int2>();
            var path = new List<int2>();

            // Geriye doğru iz sür (Backtracking)
            while (cameFrom[currentIdx] != -1)
            {
                // Index'i (x, y) koordinatına çevir ve listeye ekle
                int x = currentIdx % width;
                int y = currentIdx / width;
                path.Add(new int2(x, y));

                // Bir önceki düğüme geç
                currentIdx = cameFrom[currentIdx];
            }

            path.Reverse(); // Tersten geldik, düzeltelim
            return path;
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



        private class MinHeap
        {
            private int[] _items;      // Node Indexleri
            private int[] _priorities; // F-Scoreları
            public int Count { get; private set; }

            public MinHeap(int capacity)
            {
                _items = new int[capacity];
                _priorities = new int[capacity];
                Count = 0;
            }

            public void Push(int item, int priority)
            {
                // Basit ekleme ve yukarı taşıma
                _items[Count] = item;
                _priorities[Count] = priority;
                int i = Count;
                Count++;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (_priorities[i] >= _priorities[p]) break;
                    // Swap
                    int ti = _items[i]; _items[i] = _items[p]; _items[p] = ti;
                    int tp = _priorities[i]; _priorities[i] = _priorities[p]; _priorities[p] = tp;
                    i = p;
                }
            }

            public int Pop()
            {
                int first = _items[0];
                Count--;
                _items[0] = _items[Count];
                _priorities[0] = _priorities[Count];

                // Aşağı taşıma (Heapify Down)
                int i = 0;
                while (true)
                {
                    int left = 2 * i + 1;
                    int right = 2 * i + 2;
                    int smallest = i;

                    if (left < Count && _priorities[left] < _priorities[smallest]) smallest = left;
                    if (right < Count && _priorities[right] < _priorities[smallest]) smallest = right;

                    if (smallest == i) break;

                    // Swap
                    int ti = _items[i]; _items[i] = _items[smallest]; _items[smallest] = ti;
                    int tp = _priorities[i]; _priorities[i] = _priorities[smallest]; _priorities[smallest] = tp;
                    i = smallest;
                }
                return first;
            }
        }
    }
}

