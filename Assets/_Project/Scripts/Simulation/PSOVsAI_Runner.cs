using UnityEngine;
using System.Collections.Generic;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core; // SimGameContext ve SimConfig için

public class PSOVsAI_Runner : MonoBehaviour
{
    [Header("Referanslar")]
    public GameVisualizer Visualizer; // Sahnedeki Visualizer'ı buraya sürükle

    [Header("Yapay Zeka Beyni")]
    // EĞİTİM SONUCUNDA ALDIĞIN GBEST DİZİSİNİ BURAYA YAPIŞTIRACAKSIN
    // Örnek Varsayılan: [10, 20, 10, 0.5, 3, 0.5, 2, 2, 2, 5, 0.5, 10, 5, 0.8]
    public float[] BestGenes;

    private SimWorldState _world;
    private ParametricMacroAI _enemyAI;
    private bool _gameStarted = false;

    void Start()
    {
        StartMatch();
    }

    void StartMatch()
    {
        // 1. Dünyayı Sıfırla ve Kur
        if (Visualizer) Visualizer.ResetVisuals(); // ResetVisualizer veya ResetVisuals

        _world = new SimWorldState(SimConfig.MAP_WIDTH, SimConfig.MAP_HEIGHT);
        SimGameContext.ActiveWorld = _world; // UI ve Input sisteminin dünyayı görmesi için şart!

        // 2. Oyuncuları Kaydet
        // Player 1: SEN (İnsan) - Sol Alt
        SetupPlayer(1, new int2(5, 5));

        // Player 2: PSO AI (Düşman) - Sağ Üst
        SetupPlayer(2, new int2(SimConfig.MAP_WIDTH - 6, SimConfig.MAP_HEIGHT - 6));

        // 3. Kaynakları Dağıt
        GenerateResources();

        // 4. Yapay Zekayı Başlat (Eğer gen girildiyse)
        if (BestGenes != null && BestGenes.Length > 0)
        {
            // Random nesnesini Main Thread'den oluşturup veriyoruz
            _enemyAI = new ParametricMacroAI(_world, 2, BestGenes, new System.Random());
            Debug.Log("🤖 Düşman AI (GBest Modu) Devrede! Dikkatli ol...");
        }
        else
        {
            Debug.LogError("⚠️ Düşman Genleri (Best Genes) boş! Inspector'dan atamayı unutma.");
        }

        _gameStarted = true;
    }

    void Update()
    {
        if (!_gameStarted) return;

        float dt = Time.deltaTime;
        _world.TickCount++;

        // --- SİMÜLASYON DÖNGÜSÜ ---

        // 1. Binalar (Üretim, Kule Ateşi)
        SimBuildingSystem.UpdateAllBuildings(_world, dt);

        // 2. Birimler (Hareket, Savaş, Toplama)
        // Liste kopyası alarak güvenli döngü (Birim ölümleri listeyi bozmasın)
        var allUnits = new List<SimUnitData>(_world.Units.Values);
        foreach (var unit in allUnits)
        {
            SimUnitSystem.UpdateUnit(unit, _world, dt);
        }

        // 3. Düşman AI Karar Anı
        if (_enemyAI != null)
        {
            _enemyAI.Update(dt);
        }

        // Not: Senin kontrollerin (Tıklama, Emir verme) SimInputManager tarafından
        // otomatik olarak SimGameContext.ActiveWorld üzerinden işlenir. Ekstra kod gerekmez.
    }

    // --- KURULUM YARDIMCILARI ---
    void SetupPlayer(int id, int2 pos)
    {
        if (!_world.Players.ContainsKey(id))
            _world.Players.Add(id, new SimPlayerData { PlayerID = id });

        // Base Binası
        var baseB = new SimBuildingData
        {
            ID = _world.NextID(),
            PlayerID = id,
            Type = SimBuildingType.Base,
            GridPosition = pos,
            IsConstructed = true,
            Health = SimConfig.BASE_MAX_HEALTH,
            MaxHealth = SimConfig.BASE_MAX_HEALTH
        };
        SimBuildingSystem.InitializeBuildingStats(baseB);
        _world.Buildings.Add(baseB.ID, baseB);
        _world.Map.Grid[pos.x, pos.y].IsWalkable = false;
        _world.Map.Grid[pos.x, pos.y].OccupantID = baseB.ID;

        // Başlangıç Kaynakları (Config'den)
        SimResourceSystem.AddResource(_world, id, SimResourceType.Wood, SimConfig.START_WOOD);
        SimResourceSystem.AddResource(_world, id, SimResourceType.Meat, SimConfig.START_MEAT);
        SimResourceSystem.AddResource(_world, id, SimResourceType.Stone, SimConfig.START_STONE);

        // Başlangıç Nüfus ve İşçi
        SimResourceSystem.IncreaseMaxPopulation(_world, id, SimConfig.POPULATION_BASE);
        for (int i = 0; i < SimConfig.START_WORKER_COUNT; i++)
            SimBuildingSystem.SpawnUnit(_world, new int2(pos.x + 1 + i, pos.y), SimUnitType.Worker, id);
    }

    void GenerateResources()
    {
        for (int i = 0; i < 30; i++) SpawnResource(SimResourceType.Wood);
        for (int i = 0; i < 20; i++) SpawnResource(SimResourceType.Stone);
        for (int i = 0; i < 20; i++) SpawnResource(SimResourceType.Meat);
    }

    void SpawnResource(SimResourceType type)
    {
        int x = Random.Range(2, SimConfig.MAP_WIDTH - 2);
        int y = Random.Range(2, SimConfig.MAP_HEIGHT - 2);

        // Base'lerin dibine kaynak koyma
        if (Vector2.Distance(new Vector2(x, y), new Vector2(5, 5)) < 6) return;
        if (Vector2.Distance(new Vector2(x, y), new Vector2(SimConfig.MAP_WIDTH - 6, SimConfig.MAP_HEIGHT - 6)) < 6) return;

        int2 pos = new int2(x, y);
        if (SimGridSystem.IsWalkable(_world, pos))
        {
            var r = new SimResourceData { ID = _world.NextID(), Type = type, GridPosition = pos, AmountRemaining = 500 };
            _world.Resources.Add(r.ID, r);
            _world.Map.Grid[x, y].IsWalkable = false;

            // Görsel tip ataması
            if (type == SimResourceType.Wood) _world.Map.Grid[x, y].Type = SimTileType.Forest;
            else if (type == SimResourceType.Stone) _world.Map.Grid[x, y].Type = SimTileType.Stone;
            else _world.Map.Grid[x, y].Type = SimTileType.MeatBush;
        }
    }

    // Basit GUI: Kaynaklarını Göster
    void OnGUI()
    {
        if (_world == null) return;
        var p1 = SimResourceSystem.GetPlayer(_world, 1);
        if (p1 != null)
        {
            GUI.Box(new Rect(10, 10, 200, 100), "OYUNCU (SEN)");
            GUI.Label(new Rect(20, 30, 180, 20), $"Odun: {p1.Wood}");
            GUI.Label(new Rect(20, 50, 180, 20), $"Et: {p1.Meat} | Taş: {p1.Stone}");
            GUI.Label(new Rect(20, 70, 180, 20), $"Nüfus: {p1.CurrentPopulation}/{p1.MaxPopulation}");
        }
    }
}