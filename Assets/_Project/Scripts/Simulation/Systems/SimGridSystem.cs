using RTS.Simulation.Data;
using System.Collections.Generic;
using UnityEngine;

namespace RTS.Simulation.Systems
{
    public static class SimGridSystem
    {
        // Yürünebilir mi? (Aynı kaldı)
        public static bool IsWalkable(SimWorldState world, int2 pos)
        {
            if (!world.Map.IsInBounds(pos)) return false;
            var node = world.Map.Grid[pos.x, pos.y];
            // Yürünebilir zemin olmalı VE üzerinde kimse olmamalı
            return node.IsWalkable && node.OccupantID == -1;
        }

        // Yakınlarda boş yer bul (Aynı kaldı)
        public static int2? FindWalkableNeighbor(SimWorldState world, int2 center)
        {
            // Önce 4 Ana Yön
            int2[] cardinals = { new int2(0, 1), new int2(0, -1), new int2(1, 0), new int2(-1, 0) };
            foreach (var dir in cardinals)
            {
                int2 p = new int2(center.x + dir.x, center.y + dir.y);
                if (IsWalkable(world, p)) return p;
            }
            // Sonra Çaprazlar
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
            // Manhattan Mesafesi (Kareler için en uygunu)
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        }

        // --- A* (A-STAR) ALGORİTMASI ---
        // Artık engellerin etrafından dolaşabilir.
        public static List<int2> FindPath(SimWorldState world, int2 start, int2 end)
        {
            // Başlangıç veya Bitiş harita dışındaysa iptal
            if (!world.Map.IsInBounds(start) || !world.Map.IsInBounds(end)) return new List<int2>();

            // Olduğumuz yere gitmeye çalışıyorsak boş liste dön
            if (start == end) return new List<int2>();

            // A* Veri Yapıları
            var openSet = new List<int2> { start };
            var cameFrom = new Dictionary<int2, int2>(); // Yolu geri sarmak için

            var gScore = new Dictionary<int2, int>(); // Başlangıçtan buraya maliyet
            gScore[start] = 0;

            var fScore = new Dictionary<int2, int>(); // Tahmini toplam maliyet (g + h)
            fScore[start] = GetDistance(start, end);

            // Güvenlik Limiti (Sonsuz döngüye girmesin diye)
            int safetyLoop = 3000;

            while (openSet.Count > 0 && safetyLoop > 0)
            {
                safetyLoop--;

                // 1. Açık listedeki en düşük F skorlu kareyi seç
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

                // 2. Hedefe ulaştık mı?
                if (current == end)
                {
                    return ReconstructPath(cameFrom, current);
                }

                openSet.Remove(current);

                // 3. Komşuları Gez (8 Yön)
                foreach (var neighbor in GetNeighbors(current))
                {
                    // Engel Kontrolü:
                    // Eğer komşu yürünebilir DEĞİLSE ve Hedef nokta DEĞİLSE atla.
                    // (Hedef nokta dolu olabilir ama oraya gitmek istiyoruzdur, o yüzden hariç tutuyoruz)
                    if (!IsWalkable(world, neighbor) && neighbor != end)
                        continue;

                    // Yeni G Skoru (Her adım 1 maliyet)
                    int tentativeGScore = gScore[current] + 1;

                    if (!gScore.ContainsKey(neighbor) || tentativeGScore < gScore[neighbor])
                    {
                        // Bu yol daha iyi, kaydet
                        cameFrom[neighbor] = current;
                        gScore[neighbor] = tentativeGScore;
                        fScore[neighbor] = tentativeGScore + GetDistance(neighbor, end);

                        if (!openSet.Contains(neighbor))
                            openSet.Add(neighbor);
                    }
                }
            }

            // Yol bulunamadı
            return new List<int2>();
        }

        // Yolu geriye doğru oluşturma
        private static List<int2> ReconstructPath(Dictionary<int2, int2> cameFrom, int2 current)
        {
            var totalPath = new List<int2>();

            // Hedef noktayı listeye ekle
            totalPath.Add(current);

            while (cameFrom.ContainsKey(current))
            {
                current = cameFrom[current];
                totalPath.Add(current);
            }

            // Listeyi ters çevir (Start -> End olsun)
            totalPath.Reverse();

            // Başlangıç noktasını (şu an durduğumuz yeri) listeden çıkar, çünkü oraya yürümeyeceğiz
            if (totalPath.Count > 0) totalPath.RemoveAt(0);

            return totalPath;
        }

        // Komşuları getiren yardımcı (8 Yön)
        private static IEnumerable<int2> GetNeighbors(int2 pos)
        {
            // Ana Yönler
            yield return new int2(pos.x + 1, pos.y);
            yield return new int2(pos.x - 1, pos.y);
            yield return new int2(pos.x, pos.y + 1);
            yield return new int2(pos.x, pos.y - 1);
            // Çaprazlar
            yield return new int2(pos.x + 1, pos.y + 1);
            yield return new int2(pos.x + 1, pos.y - 1);
            yield return new int2(pos.x - 1, pos.y + 1);
            yield return new int2(pos.x - 1, pos.y - 1);
        }
        // --- MAZE GENERATION (NEW ARCHITECTURE) ---
        public static void GenerateMazeMap(SimMapData map)
        {
            // 1: Duvar, 0: Yol
            // 10x10 Basit bir 'U' labirenti
            int[,] maze = new int[,]
            {
                { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
                { 1, 0, 0, 0, 0, 0, 0, 0, 0, 1 }, // Başlangıç (1,1)
                { 1, 0, 1, 1, 1, 1, 1, 1, 0, 1 },
                { 1, 0, 1, 0, 0, 0, 0, 1, 0, 1 },
                { 1, 0, 1, 0, 1, 1, 0, 1, 0, 1 }, // Engel
                { 1, 0, 1, 0, 1, 1, 0, 1, 0, 1 },
                { 1, 0, 0, 0, 0, 0, 0, 0, 0, 1 }, // Hedef (1,6)
                { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }
            };

            int rows = maze.GetLength(0);
            int cols = maze.GetLength(1);

            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    var node = map.Grid[x, y];
                    node.OccupantID = -1; // Temizle

                    // Labirent sınırları içindeyse deseni işle
                    if (x < rows && y < cols)
                    {
                        if (maze[x, y] == 1)
                        {
                            node.Type = SimTileType.Stone;
                            node.IsWalkable = false;
                        }
                        else
                        {
                            node.Type = SimTileType.Grass;
                            node.IsWalkable = true;
                        }
                    }
                    else
                    {
                        // Dışarısı duvar olsun
                        node.Type = SimTileType.Stone;
                        node.IsWalkable = false;
                    }
                }
            }
            Debug.Log("SimGridSystem: Maze Map Generated.");
        }

        public static float GetDistanceSq(int2 a, int2 b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            return (dx * dx) + (dy * dy);
        }
    }
}