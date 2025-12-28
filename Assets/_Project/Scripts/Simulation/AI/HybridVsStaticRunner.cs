using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RTS.Simulation.AI;
using RTS.Simulation.Core;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using UnityEngine;

public class HybridVsStaticRunner : MonoBehaviour
{
    [Header("Görselleştirme")]
    public GameVisualizer Visualizer;

    [Header("Debug")]
    public bool ShowDetailedGSFLogs = true;

    [Header("Düşman Ayarları")]
    public AIStrategyMode EnemyStaticMode = AIStrategyMode.Aggressive;

    [Header("🔒 BAŞLANGIÇ KİLİTLERİ (DEFANS)")]
    public int ForceDefenseTicks = 300;
    public int RequiredTowersToUnlock = 3;

    [Header("🔥 SALDIRI TETİKLEYİCİLERİ (YENİ)")]
    [Tooltip("Asker sayısı bu değere ulaşırsa ŞÜPHESİZ SALDIRI moduna geçer.")]
    public int ForceAttackSoldierCount = 20;

    [Tooltip("Toplam kaynak (Odun+Taş+Et) bu değere ulaşırsa ŞÜPHESİZ SALDIRI moduna geçer.")]
    public int ForceAttackResourceLevel = 2500;

    [Header("⚙️ GSF HASSASİYET AYARLARI")]
    [Range(-200f, 0f)] public float DefenseThreshold = -40f;
    [Range(0f, 200f)] public float AttackThreshold = 50f;

    [Header("🧬 HYBRID AI GENLERİ")]
    public float[] EconomyGenes = new float[] { 30, 10, 50, 0.2f, 1, 1.0f, 3, 3, 1, 5, 0.5f, 100, 0, 0 };
    public float[] DefenseGenes = new float[] { 15, 20, 50, 0.8f, 2, 0.2f, 1, 1, 3, 2, 0.1f, 0, 100, 20 };
    public float[] AttackGenes = new float[] { 10, 60, 10, 0.1f, 4, 0.0f, 0, 0, 0, 2, 0.9f, 0, 0, 100 };

    private SimWorldState _world;
    private SpecializedMacroAI _staticEnemyAI;
    private SpecializedMacroAI _hybridAgentBase;
    private HybridAdaptiveAI _hybridBrain;
    private bool _isInitialized = false;
    private float _logTimer = 0f;
    private bool _isResetting = false;

    void Start()
    {
        StartGame();
    }

    void Update()
    {
        if (!_isInitialized || _isResetting) return;

        float dt = Time.deltaTime;

        SimBuildingSystem.UpdateAllBuildings(_world, dt);

        var unitIDs = _world.Units.Keys.ToList();
        foreach (var id in unitIDs)
        {
            if (_world.Units.TryGetValue(id, out SimUnitData unit))
                SimUnitSystem.UpdateUnit(unit, _world, dt);
        }

        _staticEnemyAI.Update(dt);
        _hybridBrain.Update(dt);

        if (ShowDetailedGSFLogs)
        {
            _logTimer += dt;
            if (_logTimer >= 1.0f) { _logTimer = 0f; LogGSFDetails(); }
        }

        CheckGameOver();
    }

    private void CheckGameOver()
    {
        bool p1Alive = _world.Buildings.Values.Any(b => b.PlayerID == 1 && b.Type == SimBuildingType.Base);
        bool p2Alive = _world.Buildings.Values.Any(b => b.PlayerID == 2 && b.Type == SimBuildingType.Base);

        if (!p1Alive || !p2Alive)
        {
            string winner = p1Alive ? "MAVİ (HYBRID AI)" : "KIRMIZI (STATİK AI)";
            string color = p1Alive ? "green" : "red";
            Debug.Log($"<color={color}><b>🏆 OYUN BİTTİ! KAZANAN: {winner}</b></color>");
            StartCoroutine(ResetGameRoutine());
        }
    }

    private IEnumerator ResetGameRoutine()
    {
        _isResetting = true;
        Debug.Log("🔄 Oyun 3 saniye içinde yeniden başlatılıyor...");
        yield return new WaitForSeconds(3.0f);
        StartGame();
        _isResetting = false;
    }

    private void StartGame()
    {
        InitializeSimulation();
        if (Visualizer != null) Visualizer.Initialize(_world);
    }

    private void LogGSFDetails()
    {
        float inactivity = (_hybridBrain != null) ? _hybridBrain.GetInactivityTimer() : 0f;

        // --- DÜZELTME BURADA ---
        // CalculateGSF fonksiyonuna eksik olan parametreleri ekledik:
        // ForceDefenseTicks, RequiredTowersToUnlock, ForceAttackSoldierCount, ForceAttackResourceLevel
        var m = SimGameStateAnalyzer.CalculateGSF(
            _world,
            1,
            inactivity,
            ForceDefenseTicks,
            RequiredTowersToUnlock,
            ForceAttackSoldierCount,
            ForceAttackResourceLevel
        );
        // -----------------------

        int towerCount = _world.Buildings.Values.Count(b => b.PlayerID == 1 && b.Type == SimBuildingType.Tower);
        int soldierCount = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Soldier);
        var pData = SimResourceSystem.GetPlayer(_world, 1);
        int totalRes = (pData != null) ? (pData.Wood + pData.Stone + pData.Meat) : 0;

        string statusColor = "white";
        string currentMode = "DENGELİ (Ekonomi)";

        bool isLocked = (_world.TickCount < ForceDefenseTicks) && (towerCount < RequiredTowersToUnlock);

        bool isForcedAttack = (soldierCount >= ForceAttackSoldierCount) || (totalRes >= ForceAttackResourceLevel);

        if (isForcedAttack)
        {
            statusColor = "magenta";
            currentMode = $"🔥 TAM SALDIRI (Güç: {soldierCount}/{ForceAttackSoldierCount} Asker)";
        }
        else if (isLocked)
        {
            statusColor = "orange";
            currentMode = $"🔒 KİLİTLİ [Kule: {towerCount}/{RequiredTowersToUnlock} | Süre: {_world.TickCount}/{ForceDefenseTicks}]";
        }
        else if (m.GSF < DefenseThreshold) { statusColor = "red"; currentMode = "⚠️ SAVUNMA"; }
        else if (m.GSF > AttackThreshold) { statusColor = "green"; currentMode = "⚔️ SALDIRI"; }
        else { statusColor = "cyan"; currentMode = "💰 EKONOMİ"; }

        string log = $"<color={statusColor}><b>[GSF]</b> Skor: {m.GSF:F1} | Mod: {currentMode}</color>\n";
        log += $"   • 🛡️ SAVUNMA: Biz({m.MDP:F0}) vs Düşman({m.EDP:F0})\n";
        log += $"   • ⚔️ SALDIRI: Biz({m.MAP:F0}) vs Düşman({m.EAP:F0})\n";

        if (inactivity > 60f)
            log += $"   • 💤 DÜŞMAN UYUYOR: {inactivity:F0}sn (Bonus: +{(inactivity - 60) * 0.5f:F0})";

        Debug.Log(log);
    }

    private void InitializeSimulation()
    {
        _world = new SimWorldState(SimConfig.MAP_WIDTH, SimConfig.MAP_HEIGHT);

        for (int x = 0; x < SimConfig.MAP_WIDTH; x++)
        {
            for (int y = 0; y < SimConfig.MAP_HEIGHT; y++)
            {
                var node = _world.Map.Grid[x, y];
                node.Type = SimTileType.Grass;
                node.IsWalkable = true;
                node.OccupantID = -1;
            }
        }

        SpawnRandomResources(50, SimResourceType.Wood);
        SpawnRandomResources(50, SimResourceType.Stone);
        SpawnRandomResources(50, SimResourceType.Meat);

        SetupPlayer(1, 500, 500, 200, 10);
        if (!_world.Players.ContainsKey(2)) _world.Players.Add(2, new SimPlayerData { PlayerID = 2 });
        SetupPlayer(2, 500, 500, 200, 10);

        SpawnBaseWithWorkers(1, new int2(5, 5));
        SpawnBaseWithWorkers(2, new int2(SimConfig.MAP_WIDTH - 6, SimConfig.MAP_HEIGHT - 6));

        _staticEnemyAI = new SpecializedMacroAI(_world, 2, null, EnemyStaticMode);
        _hybridAgentBase = new SpecializedMacroAI(_world, 1, EconomyGenes, AIStrategyMode.Economic);

        _hybridBrain = new HybridAdaptiveAI(_world, 1, _hybridAgentBase,
                                            EconomyGenes, DefenseGenes, AttackGenes,
                                            DefenseThreshold, AttackThreshold,
                                            ForceDefenseTicks, RequiredTowersToUnlock,
                                            ForceAttackSoldierCount, ForceAttackResourceLevel);

        _isInitialized = true;
        Debug.Log($"⚔️ YENİ OYUN BAŞLADI!");
    }

    private void SetupPlayer(int id, int wood, int stone, int meat, int pop)
    {
        var p = _world.Players[id];
        p.Wood = wood; p.Stone = stone; p.Meat = meat;
        p.CurrentPopulation = 0; p.MaxPopulation = 0;
    }

    private void SpawnBaseWithWorkers(int playerID, int2 pos)
    {
        var baseB = SimBuildingSystem.CreateBuilding(_world, playerID, SimBuildingType.Base, pos);
        baseB.IsConstructed = true;
        baseB.ConstructionProgress = SimConfig.BUILDING_MAX_PROGRESS;
        baseB.Health = baseB.MaxHealth;
        SimResourceSystem.IncreaseMaxPopulation(_world, playerID, SimConfig.POPULATION_BASE);
        for (int i = 0; i < 3; i++) SimBuildingSystem.SpawnUnit(_world, pos, SimUnitType.Worker, playerID);
    }

    private void SpawnRandomResources(int count, SimResourceType type)
    {
        for (int i = 0; i < count; i++)
        {
            int rx = UnityEngine.Random.Range(2, SimConfig.MAP_WIDTH - 2);
            int ry = UnityEngine.Random.Range(2, SimConfig.MAP_HEIGHT - 2);
            int2 rPos = new int2(rx, ry);
            if (SimGridSystem.IsWalkable(_world, rPos))
            {
                var res = new SimResourceData { ID = _world.NextID(), Type = type, GridPosition = rPos, AmountRemaining = (type == SimResourceType.Wood) ? 500 : 250 };
                _world.Resources.Add(res.ID, res);
                _world.Map.Grid[rx, ry].IsWalkable = false;
                _world.Map.Grid[rx, ry].OccupantID = res.ID;
                if (type == SimResourceType.Stone) _world.Map.Grid[rx, ry].Type = SimTileType.Stone;
                if (type == SimResourceType.Wood) _world.Map.Grid[rx, ry].Type = SimTileType.Forest;
            }
        }
    }
}