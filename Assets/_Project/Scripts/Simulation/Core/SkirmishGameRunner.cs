using UnityEngine;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core; // Context

public class SkirmishGameRunner : MonoBehaviour
{
    [Header("Ayarlar")]
    public int MapSize = 30;

    public GameVisualizer Visualizer;

    private SimWorldState _world;
    private SimpleMacroAI _enemyAI;

    void Start()
    {
        Debug.Log("⚔️ SKIRMISH BAŞLIYOR...");

        // 1. Dünyayı Kur
        // SimWorldState constructor'ı otomatik olarak Player 1'i (boş halde) oluşturur.
        _world = new SimWorldState(MapSize, MapSize);
        SimGameContext.ActiveWorld = _world;

        // 2. Oyuncuları Ayarla

        // --- PLAYER 1 (SEN) ---
        // SimWorldState zaten Player 1'i eklediği için .Add() yaparsak çakışır.
        // O yüzden var olanı güncelliyoruz veya yoksa ekliyoruz.
        var p1 = new SimPlayerData { PlayerID = 1, Wood = 20000, Stone = 20000, Meat = 20000, MaxPopulation = 100 };

        if (_world.Players.ContainsKey(1))
            _world.Players[1] = p1; // Varsa üzerine yaz (Overwrite)
        else
            _world.Players.Add(1, p1); // Yoksa ekle

        // --- PLAYER 2 (AI) ---
        var p2 = new SimPlayerData { PlayerID = 2, Wood = 0, Stone = 0, Meat = 50, MaxPopulation = 10 };

        if (_world.Players.ContainsKey(2))
            _world.Players[2] = p2;
        else
            _world.Players.Add(2, p2);

        // 3. Harita ve Kaynaklar
        GenerateMap();

        // 4. Üsleri Kur (Köşelere)
        SetupBase(1, new int2(4, 4)); // Sen (Sol Alt)
        SetupBase(2, new int2(MapSize - 5, MapSize - 5)); // AI (Sağ Üst)

        // 5. AI Başlat (Player 2'yi yönetecek)
        _enemyAI = new SimpleMacroAI(_world, 2);

        // 6. Görselleştirici
        if (Visualizer != null) Visualizer.RegenerateMap(_world.Map);

        // 7. Kamera
        if (Camera.main != null)
        {
            Camera.main.transform.position = new Vector3(MapSize / 2f, MapSize / 2f, -10f);
            Camera.main.orthographicSize = 10f;
        }
    }

    void Update()
    {
        // Eğer Start'ta hata olduysa ve _enemyAI oluşmadıysa devam etme
        if (_enemyAI == null || _world == null) return;

        float dt = Time.deltaTime;

        // AI Düşünür
        _enemyAI.Update(dt);

        // Fizik İşler
        foreach (var unit in _world.Units.Values)
        {
            SimUnitSystem.UpdateUnit(unit, _world, dt);
        }

        // Binalar İşler
        SimBuildingSystem.UpdateAllBuildings(_world, dt);
    }

    void SetupBase(int playerID, int2 pos)
    {
        // Ana Bina
        var baseB = new SimBuildingData
        {
            ID = _world.NextID(),
            PlayerID = playerID,
            Type = SimBuildingType.Base,
            GridPosition = pos,
            IsConstructed = true,
            ConstructionProgress = 100f
        };
        SimBuildingSystem.InitializeBuildingStats(baseB); // Can ve özellikleri ata
        _world.Buildings.Add(baseB.ID, baseB);
        _world.Map.Grid[pos.x, pos.y].OccupantID = baseB.ID;
        _world.Map.Grid[pos.x, pos.y].IsWalkable = false;

        // Yanına bir işçi
        SimBuildingSystem.SpawnUnit(_world, pos, SimUnitType.Worker, playerID);
    }

    void GenerateMap()
    {
        for (int x = 0; x < MapSize; x++)
        {
            for (int y = 0; y < MapSize; y++)
            {
                var node = _world.Map.Grid[x, y];
                node.Type = SimTileType.Grass;
                node.IsWalkable = true;
                node.OccupantID = -1;

                // Rastgele Kaynak (%5)
                if (Random.value < 0.05f)
                {
                    // Üslerin dibine (spawn noktalarına) kaynak koyma
                    if (x < 6 && y < 6) continue;
                    if (x > MapSize - 6 && y > MapSize - 6) continue;

                    var res = new SimResourceData
                    {
                        ID = _world.NextID(),
                        GridPosition = new int2(x, y),
                        AmountRemaining = 500
                    };

                    float r = Random.value;
                    if (r < 0.33f) { res.Type = SimResourceType.Wood; node.Type = SimTileType.Forest; }
                    else if (r < 0.66f) { res.Type = SimResourceType.Stone; node.Type = SimTileType.Stone; }
                    else { res.Type = SimResourceType.Meat; node.Type = SimTileType.MeatBush; }

                    _world.Resources.Add(res.ID, res);
                    node.IsWalkable = false;
                }
            }
        }
    }
}