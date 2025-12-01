using UnityEngine;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core; // Context için

public class MazeDemoRunner : MonoBehaviour
{
    [Header("Görsel Ayarlar")]
    public GameVisualizer Visualizer;

    private SimWorldState _world;

    void Start()
    {
        Debug.Log("🌀 Maze Demo Başlatılıyor...");

        // 1. Dünyayı 16x16 Kur
        _world = new SimWorldState(16, 16);
        SimGameContext.ActiveWorld = _world;

        // 2. Labirenti Veri Olarak İşle
        GenerateMazeWithWalls();

        // 3. Worker Ekle (Hız ve PlayerID tanımlı)
        SpawnWorker();

        // 4. Görselleştiriciyi Tetikle
        if (Visualizer != null)
        {
            Visualizer.RegenerateMap(_world.Map);
        }

        // 5. Kamera Ayarı
        if (Camera.main != null)
        {
            Camera.main.transform.position = new Vector3(8f, 8f, -10f);
            Camera.main.orthographicSize = 9f;
        }
    }

    void Update()
    {
        // Sadece fizik güncellemesi
        float dt = Time.deltaTime;
        foreach (var unit in _world.Units.Values)
        {
            SimUnitSystem.UpdateUnit(unit, _world, dt);
        }
    }

    void GenerateMazeWithWalls()
    {
        // 16x16 Labirent Deseni (1: Duvar, 0: Yol)
        // DİKKAT: Array [satır, sütun] -> [y, x] okunur. Görselle eşleşmesi için ters okuyacağız.
        int[,] maze = new int[,]
        {
            { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }, // y=15 (Üst)
            { 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1 },
            { 1, 0, 1, 1, 1, 0, 1, 0, 1, 1, 1, 1, 1, 1, 0, 1 },
            { 1, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1, 0, 1 },
            { 1, 0, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 0, 1 },
            { 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 1 },
            { 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 0, 1, 0, 1 },
            { 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 1 },
            { 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 0, 1, 1, 1, 1, 1 },
            { 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1 },
            { 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1 },
            { 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 },
            { 1, 0, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
            { 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 },
            { 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1 },
            { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }  // y=0 (Alt)
        };

        int rows = maze.GetLength(0);
        int cols = maze.GetLength(1);

        for (int x = 0; x < 16; x++)
        {
            for (int y = 0; y < 16; y++)
            {
                var node = _world.Map.Grid[x, y];
                node.OccupantID = -1;

                // Varsayılan Çim
                node.Type = SimTileType.Grass;
                node.IsWalkable = true;

                // Diziden Oku (Ters Y)
                // Eğer harita dışındaysa (güvenlik için) duvar yap
                int val = 1;
                if (x < cols && y < rows)
                    val = maze[rows - 1 - y, x];

                if (val == 1)
                {
                    node.Type = SimTileType.Stone;
                    node.IsWalkable = false;

                    // *** DUVAR BİNASI EKLEME ***
                    var wall = new SimBuildingData
                    {
                        ID = _world.NextID(),
                        PlayerID = 0, // Doğa
                        Type = SimBuildingType.Wall, // Visualizer bunu WallPrefab ile çizer
                        GridPosition = new int2(x, y),
                        IsConstructed = true
                    };
                    _world.Buildings.Add(wall.ID, wall);
                    node.OccupantID = wall.ID;
                }
            }
        }
    }

    void SpawnWorker()
    {
        int2 startPos = new int2(1, 1);

        // Başlangıç noktası doluysa temizle
        var startNode = _world.Map.Grid[startPos.x, startPos.y];
        if (startNode.OccupantID != -1)
        {
            _world.Buildings.Remove(startNode.OccupantID);
            startNode.OccupantID = -1;
            startNode.IsWalkable = true;
            startNode.Type = SimTileType.Grass;
        }

        var worker = new SimUnitData
        {
            ID = _world.NextID(),
            PlayerID = 1,
            UnitType = SimUnitType.Worker,
            GridPosition = startPos,
            Health = 100,
            State = SimTaskType.Idle,
            MoveSpeed = 5.0f, // HIZ!
            AttackRange = 1f,
            AttackSpeed = 1f
        };
        _world.Units.Add(worker.ID, worker);
        startNode.OccupantID = worker.ID;
    }
}