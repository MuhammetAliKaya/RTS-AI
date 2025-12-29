using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Globalization; // CSV Formatı için kritik
using System.Linq;
using RTS.Simulation.AI;
using RTS.Simulation.Core;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;

public class BenchmarkRunner : MonoBehaviour
{
    [Header("Görselleştirme")]
    public GameVisualizer Visualizer;

    [Header("Benchmark Ayarları")]
    public string CsvFileName = "General_vs_Hybrid_Benchmark.csv";
    public bool EnableCsvLogging = true;
    public float TimeScale = 5.0f; // Simülasyon hızı

    [Header("⚔️ RAKİP SEÇİMİ (ÇOK ÖNEMLİ)")]
    [Tooltip("Eğer işaretli ise aşağıya girdiğin 'Enemy Genomes' kullanılır (PSO). İşaretli değilse 'General Scripted AI' kullanılır.")]
    public bool UseTrainedEnemy = false;

    [Tooltip("Eğitimden çıkan en iyi 14 geni buraya yapıştır.")]
    public float[] EnemyGenomes;

    [Header("Hybrid AI Ayarları (Player 1)")]
    public float[] EcoGenes = new float[] { 30, 10, 50, 0.2f, 1, 1.0f, 3, 3, 1, 5, 0.5f, 100, 0, 0 };
    public float[] DefGenes = new float[] { 15, 20, 50, 0.8f, 2, 0.2f, 1, 1, 3, 2, 0.1f, 0, 100, 20 };
    public float[] AtkGenes = new float[] { 10, 60, 10, 0.1f, 4, 0.0f, 0, 0, 0, 2, 0.9f, 0, 0, 100 };

    [Range(-200f, 0f)] public float DefenseThreshold = -40f;
    [Range(0f, 200f)] public float AttackThreshold = 50f;

    // Simülasyon Değişkenleri
    private SimWorldState _world;
    private SpecializedMacroAI _enemyAI; // İsim değiştirdik: genel kullanım için
    private SpecializedMacroAI _hybridAgentBase;
    private HybridAdaptiveAI _hybridBrain;

    private bool _isInitialized = false;
    private bool _matchOver = false;

    // CSV Loglama
    private StringBuilder _csvContent;
    private float _logTimer = 0f;

    void Start()
    {
        InitializeCsv();
        StartMatch();
    }

    void Update()
    {
        if (!_isInitialized || _matchOver) return;

        // Hızlandırılmış Simülasyon
        float dt = Time.deltaTime * TimeScale;

        // 1. Simülasyonu İlerlet
        SimBuildingSystem.UpdateAllBuildings(_world, dt);
        var unitIDs = _world.Units.Keys.ToList();
        foreach (var id in unitIDs)
        {
            if (_world.Units.TryGetValue(id, out SimUnitData unit))
                SimUnitSystem.UpdateUnit(unit, _world, dt);
        }

        // 2. Yapay Zekaları Güncelle
        _enemyAI.Update(dt);        // Player 2 (Rakip)
        _hybridBrain.Update(dt);    // Player 1 (Bizim Hybrid)

        // 3. Loglama (Saniyede 1)
        _logTimer += Time.deltaTime;
        if (_logTimer >= 1.0f)
        {
            _logTimer = 0f;
            LogMetrics();
        }

        // 4. Oyun Bitti mi?
        CheckGameOver();
    }

    void StartMatch()
    {
        _world = new SimWorldState(SimConfig.MAP_WIDTH, SimConfig.MAP_HEIGHT);
        if (Visualizer != null) Visualizer.Initialize(_world);

        SetupMapAndResources();

        // Oyuncuları Kur
        SetupPlayer(1, new int2(5, 5)); // Biz (Sol Alt)
        SetupPlayer(2, new int2(SimConfig.MAP_WIDTH - 6, SimConfig.MAP_HEIGHT - 6)); // Düşman (Sağ Üst)

        // --- RAKİP SEÇİMİ MANTIĞI ---
        if (UseTrainedEnemy && EnemyGenomes != null && EnemyGenomes.Length > 0)
        {
            // PSO EĞİTİLMİŞ AJAN (Parametrik Mod)
            // Modu formalite icabı Aggressive seçiyoruz, kararları genler verecek.
            _enemyAI = new SpecializedMacroAI(_world, 2, EnemyGenomes, AIStrategyMode.Aggressive);
            Debug.Log($"<color=red><b>🤖 RAKİP: EĞİTİLMİŞ PSO AJANI ({EnemyGenomes.Length} Gen)</b></color>");
        }
        else
        {
            // GENERAL SCRIPTED BOT (Kural Tabanlı - General Mod)
            // Genler null olduğu için Static Behavior çalışacak.
            _enemyAI = new SpecializedMacroAI(_world, 2, null, AIStrategyMode.General);
            Debug.Log("<color=orange><b>🤖 RAKİP: GENERAL SCRIPTED AI (Dengeli Bot)</b></color>");
        }

        // BİZİM AJAN (HYBRID)
        _hybridAgentBase = new SpecializedMacroAI(_world, 1, EcoGenes, AIStrategyMode.Economic);

        _hybridBrain = new HybridAdaptiveAI(
            _world, 1, _hybridAgentBase,
            EcoGenes, DefGenes, AtkGenes,
            DefenseThreshold, AttackThreshold,
            0, 0, 5, 7500
        );

        _isInitialized = true;
        Debug.Log("🏁 BENCHMARK BAŞLADI: Hybrid (P1) vs Seçilen Rakip (P2)");
    }

    // --- CSV İŞLEMLERİ ---
    void InitializeCsv()
    {
        if (!EnableCsvLogging) return;
        _csvContent = new StringBuilder();
        // Başlıklar
        _csvContent.AppendLine("Time,P1_Pop,P1_Soldiers,P1_Res,P2_Pop,P2_Soldiers,P2_Res,ScoreDiff,HybridMode");
    }

    void LogMetrics()
    {
        if (!EnableCsvLogging || _matchOver) return;

        var p1 = SimResourceSystem.GetPlayer(_world, 1);
        var p2 = SimResourceSystem.GetPlayer(_world, 2);

        int p1Soldiers = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Soldier);
        int p2Soldiers = _world.Units.Values.Count(u => u.PlayerID == 2 && u.UnitType == SimUnitType.Soldier);

        float p1Res = p1.Wood + p1.Meat + p1.Stone;
        float p2Res = p2.Wood + p2.Meat + p2.Stone;

        // Skor farkı
        float scoreDiff = (p1.TotalDamageDealt - p1.TotalDamageTaken) - (p2.TotalDamageDealt - p2.TotalDamageTaken);

        // --- 1. GERÇEK MODU OKU ---
        string mode = (_hybridBrain != null) ? _hybridBrain.GetCurrentStrategy() : "Init";

        // --- 2. CSV'YE YAZ ---
        string line = string.Format(CultureInfo.InvariantCulture, "{0:F1},{1},{2},{3:F0},{4},{5},{6:F0},{7:F1},{8}",
            Time.time,
            p1.CurrentPopulation, p1Soldiers, p1Res,
            p2.CurrentPopulation, p2Soldiers, p2Res,
            scoreDiff,
            mode
        );
        _csvContent.AppendLine(line);

        // --- 3. DEBUG LOG (RENKLİ) ---
        string color = "white";
        if (mode == "Aggressive") color = "red";
        else if (mode == "Defensive") color = "orange";
        else if (mode == "Economic") color = "cyan";

        Debug.Log($"<color={color}><b>[{mode}]</b></color> Time: {Time.time:F1}s | ScoreDiff: {scoreDiff:F0} | Soldiers: {p1Soldiers} vs {p2Soldiers}");
    }

    void OnApplicationQuit()
    {
        SaveCsv();
    }

    void SaveCsv()
    {
        if (!EnableCsvLogging || _csvContent == null) return;

        string path = Path.Combine(Application.dataPath, "../" + CsvFileName);
        File.WriteAllText(path, _csvContent.ToString());
        Debug.Log($"💾 CSV Kaydedildi: {path}");
    }

    // --- YARDIMCI FONKSİYONLAR (AYNEN KORUNDU) ---
    void SetupPlayer(int id, int2 basePos)
    {
        if (!_world.Players.ContainsKey(id)) _world.Players.Add(id, new SimPlayerData { PlayerID = id });
        var p = _world.Players[id];
        p.Wood = 500; p.Meat = 200; p.Stone = 200;

        var baseB = SimBuildingSystem.CreateBuilding(_world, id, SimBuildingType.Base, basePos);
        baseB.IsConstructed = true; baseB.Health = baseB.MaxHealth;
        _world.Map.Grid[basePos.x, basePos.y].IsWalkable = false;
        _world.Map.Grid[basePos.x, basePos.y].OccupantID = baseB.ID;

        SimResourceSystem.IncreaseMaxPopulation(_world, id, SimConfig.POPULATION_BASE);
        for (int i = 0; i < 3; i++) SimBuildingSystem.SpawnUnit(_world, new int2(basePos.x + 1 + i, basePos.y), SimUnitType.Worker, id);
    }

    void SetupMapAndResources()
    {
        for (int x = 0; x < SimConfig.MAP_WIDTH; x++)
            for (int y = 0; y < SimConfig.MAP_HEIGHT; y++)
            {
                _world.Map.Grid[x, y].Type = SimTileType.Grass;
                _world.Map.Grid[x, y].IsWalkable = true;
            }

        SpawnResources(40, SimResourceType.Wood);
        SpawnResources(30, SimResourceType.Stone);
        SpawnResources(30, SimResourceType.Meat);
    }

    void SpawnResources(int count, SimResourceType type)
    {
        for (int i = 0; i < count; i++)
        {
            int rx = Random.Range(2, SimConfig.MAP_WIDTH - 2);
            int ry = Random.Range(2, SimConfig.MAP_HEIGHT - 2);
            int2 pos = new int2(rx, ry);
            if (SimGridSystem.IsWalkable(_world, pos))
            {
                var r = new SimResourceData { ID = _world.NextID(), Type = type, GridPosition = pos, AmountRemaining = 500 };
                _world.Resources.Add(r.ID, r);
                _world.Map.Grid[pos.x, pos.y].IsWalkable = false;
                if (type == SimResourceType.Wood) _world.Map.Grid[pos.x, pos.y].Type = SimTileType.Forest;
                else if (type == SimResourceType.Stone) _world.Map.Grid[pos.x, pos.y].Type = SimTileType.Stone;
            }
        }
    }

    void CheckGameOver()
    {
        bool p1Base = _world.Buildings.Values.Any(b => b.PlayerID == 1 && b.Type == SimBuildingType.Base);
        bool p2Base = _world.Buildings.Values.Any(b => b.PlayerID == 2 && b.Type == SimBuildingType.Base);

        if (!p1Base || !p2Base)
        {
            _matchOver = true;
            string winner = p1Base ? "HYBRID (P1)" : "ENEMY (P2)";
            Debug.Log($"🏆 MAÇ BİTTİ! Kazanan: {winner}");
            SaveCsv();
        }
    }
}