using UnityEngine;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;

namespace RTS.Simulation.Scenarios
{
    public class MazeScenario : IScenario
    {
        public string ScenarioName => "A* Pathfinding Maze";

        public void SetupMap(SimWorldState world, int seed)
        {
            Debug.Log("--- ⚡ MAZE SCENARIO: HARD RESET BAŞLIYOR ⚡ ---");

            // 1. HARİTA BOYUTUNU ZORLA 16x16 YAP (Inspector Ayarını Ezer)
            world.Map = new SimMapData(16, 16);
            Debug.Log("Harita 16x16 olarak yeniden oluşturuldu.");

            // 2. Labirent Deseni (1: Duvar, 0: Yol)
            // Not: Dizi [y, x] veya [satır, sütun] okunur.
            // Görsel olarak Unity'de X sağa, Y yukarı artar. 
            // Burada array indekslemesi ile dünya koordinatını eşleştiriyoruz.
            int[,] maze = new int[,]
                        {
                { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }, // y=15 (Üst Duvar)
                { 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1 }, // y=14
                { 1, 0, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1, 1, 0, 1 }, // y=13
                { 1, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1, 0, 1 }, // y=12
                { 1, 0, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 0, 1 }, // y=11
                { 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 1 }, // y=10
                { 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 0, 1, 0, 1 }, // y=9
                { 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 1 }, // y=8 (Orta Geçiş)
                { 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 0, 1, 1, 1, 1, 1 }, // y=7
                { 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1 }, // y=6
                { 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1 }, // y=5
                { 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 }, // y=4
                { 1, 0, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }, // y=3
                { 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 }, // y=2
                { 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1 }, // y=1 (Start Noktası buraya yakın)
                { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }  // y=0 (Alt Duvar)
            };

            int rows = maze.GetLength(0); // Height
            int cols = maze.GetLength(1); // Width

            // Haritayı işle
            for (int x = 0; x < world.Map.Width; x++)
            {
                for (int y = 0; y < world.Map.Height; y++)
                {
                    var node = world.Map.Grid[x, y];
                    node.OccupantID = -1;
                    node.Type = SimTileType.Grass;
                    node.IsWalkable = true;

                    // Dizi sınırları içindeyse labirenti uygula
                    // Array'de maze[rows - 1 - y, x] yaparak Unity koordinatlarına (Y yukarı) çeviriyoruz.
                    if (x < cols && y < rows)
                    {
                        int val = maze[rows - 1 - y, x]; // Ters Y okuma
                        if (val == 1)
                        {
                            node.Type = SimTileType.Stone;
                            node.IsWalkable = false;

                            // Duvar Nesnesi Ekle (Görsel olması için)
                            var wall = new SimBuildingData
                            {
                                ID = world.NextID(),
                                PlayerID = 0, // Tarafsız
                                Type = SimBuildingType.Wall,
                                GridPosition = new int2(x, y),
                                IsConstructed = true
                            };
                            world.Buildings.Add(wall.ID, wall);
                            node.OccupantID = wall.ID;
                        }
                    }
                }
            }

            // 3. WORKER OLUŞTUR (Player ID: 1)
            int2 startPos = new int2(1, 1);

            // Eğer o kare doluysa (duvar varsa) temizle
            if (world.Map.Grid[startPos.x, startPos.y].OccupantID != -1)
            {
                int occID = world.Map.Grid[startPos.x, startPos.y].OccupantID;
                world.Buildings.Remove(occID);
                world.Map.Grid[startPos.x, startPos.y].OccupantID = -1;
                world.Map.Grid[startPos.x, startPos.y].IsWalkable = true;
            }

            var worker = new SimUnitData
            {
                ID = world.NextID(),
                PlayerID = 1, // <--- KONTROL ETTİĞİMİZ PLAYER ID (1)
                UnitType = SimUnitType.Worker,
                GridPosition = startPos,
                // CurrentHealth = 100,
                State = SimTaskType.Idle,
                MoveSpeed = 3.0f,
                AttackRange = 1.5f,
                AttackSpeed = 1.0f,
                Damage = 5
            };
            world.Units.Add(worker.ID, worker);
            world.Map.Grid[startPos.x, startPos.y].OccupantID = worker.ID;

            Debug.Log($"✅ WORKER OLUŞTURULDU! ID: {worker.ID}, PlayerID: {worker.PlayerID}, Konum: {startPos}");

            // 4. KAMERA AYARI (Opsiyonel)
            if (Camera.main != null)
            {
                Camera.main.transform.position = new Vector3(7.5f, 7.5f, -10f);
                Camera.main.orthographicSize = 8f; // Zoom
            }
        }

        public bool CheckWinCondition(SimWorldState world, int playerID)
        {
            // Basit kazanma koşulu (Hedef: 13, 1)
            return world.Map.Grid[13, 1].OccupantID != -1;
        }

        public float CalculateReward(SimWorldState world, int playerID, int action) { return 0f; }
    }
}