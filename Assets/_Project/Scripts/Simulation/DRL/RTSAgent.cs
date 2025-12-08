using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;

public class RTSAgent : Agent
{
    private SimWorldState _world;
    private SimGridSystem _gridSystem;
    private SimUnitSystem _unitSystem;
    private SimBuildingSystem _buildingSystem;

    private DRLActionTranslator _translator;
    private RTSGridSensor _gridSensor;

    public DRLSimRunner Runner;

    private int _lastDebugX = -1;
    private int _lastDebugY = -1;
    private int _lastDebugCommand = 0;

    // Setup: Runner tarafından çağrılır
    public void Setup(SimWorldState world, SimGridSystem gridSys, SimUnitSystem unitSys, SimBuildingSystem buildSys)
    {
        _world = world;
        _gridSystem = gridSys;
        _unitSystem = unitSys;
        _buildingSystem = buildSys;

        _gridSensor = new RTSGridSensor(_world, _gridSystem);
        _translator = new DRLActionTranslator(_world, _unitSystem, _buildingSystem, _gridSystem);
    }

    public override void OnEpisodeBegin()
    {
        if (Runner != null) Runner.ResetSimulation();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (_world == null) return;
        _gridSensor.AddGlobalStats(sensor);
        _gridSensor.AddGridObservations(sensor);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (_world == null) return;


        int command = actions.DiscreteActions[0];
        int targetX = actions.DiscreteActions[1];
        int targetY = actions.DiscreteActions[2];

        _lastDebugCommand = command;
        _lastDebugX = targetX;
        _lastDebugY = targetY;

        // Hamleyi dene ve sonucunu al
        bool isSuccess = _translator.ExecuteAction(command, targetX, targetY);

        // --- DETAYLI LOGLAMA (Sadece İzleme Modunda ve Runner Tanımlıysa) ---
        // Bu blok sadece TrainMode kapalıyken çalışır ve konsola bilgi basar.
        if (Runner != null && !Runner.TrainMode)
        {
            string status = isSuccess ? "<color=green>BAŞARILI</color>" : "<color=red>BAŞARISIZ</color>";

            // Komut ismini anlamlandırma (Okunabilirlik için)
            string cmdName = "BİLİNMEYEN";
            switch (command)
            {
                case 0: cmdName = "BEKLE (Wait)"; break;
                case 1: cmdName = "HAREKET (Move)"; break;
                case 2: cmdName = "TOPLA (Harvest)"; break;
                case 3: cmdName = "SALDIR (Attack)"; break;
                case 4: cmdName = "İNŞA ET: EV"; break;
                case 5: cmdName = "İNŞA ET: KIŞLA"; break;
                case 6: cmdName = "İNŞA ET: KULE"; break;
                case 7: cmdName = "İNŞA ET: ÇİFTLİK"; break;
                case 8: cmdName = "İNŞA ET: ODUNCU"; break;
                case 9: cmdName = "İNŞA ET: TAŞ OCAĞI"; break;
                case 10: cmdName = "ÜRET: İŞÇİ"; break;
                case 11: cmdName = "ÜRET: ASKER"; break;
            }

            Debug.Log($"🧠 <b>[AGENT KARARI - Adım {StepCount}]</b> Komut: {cmdName} ({command}) | Hedef: ({targetX},{targetY}) | Sonuç: {status}");
        }
        // -------------------------------------------------------------------

        // --- İSTATİSTİK VE CEZA MEKANİZMASI ---

        if (!isSuccess && command != 0) // Beklemek (0) hariç, başarısız her hamle cezadır
        {
            AddReward(-0.005f); // Hatalı hamle cezası

            // DÜZELTME: 'Unity.MLAgents.Stats.' kısmı kaldırıldı.
            if (Unity.MLAgents.Academy.IsInitialized)
            {
                Unity.MLAgents.Academy.Instance.StatsRecorder.Add("Actions/Invalid_Move", 1.0f, StatAggregationMethod.Sum);
            }
        }
        else if (command != 0)
        {
            AddReward(0.001f); // Geçerli işlem ödülü (Motivasyon)

            // DÜZELTME: 'Unity.MLAgents.Stats.' kısmı kaldırıldı.
            if (Unity.MLAgents.Academy.IsInitialized)
            {
                Unity.MLAgents.Academy.Instance.StatsRecorder.Add("Actions/Valid_Move", 1.0f, StatAggregationMethod.Sum);
            }
        }
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        if (_world == null || !_world.Players.ContainsKey(1)) return;

        var player = _world.Players[1];

        // --- KOMUTLAR ---
        // 0: Bekle
        // 1: Ev, 2: Kışla
        // 3: İşçi, 4: Asker
        // 5: Saldır, 6: Topla
        // 7: Çiftlik, 8: Oduncu, 9: Taş Ocağı (YENİ)

        // --- İŞÇİ KONTROLÜ (İnşaat ve Toplama için İşçi Şart) ---
        bool hasWorker = false;
        int soldierCount = 0;
        bool hasBase = false;
        bool hasBarracks = false;

        foreach (var u in _world.Units.Values)
        {
            if (u.PlayerID == 1)
            {
                if (u.UnitType == SimUnitType.Worker) hasWorker = true;
                if (u.UnitType == SimUnitType.Soldier) soldierCount++;
            }
        }

        // Bina ve Kaynak Kontrolleri
        foreach (var b in _world.Buildings.Values)
        {
            if (b.PlayerID == 1 && b.IsConstructed)
            {
                if (b.Type == SimBuildingType.Base) hasBase = true;
                if (b.Type == SimBuildingType.Barracks) hasBarracks = true;
            }
        }

        // --- MASKELEME MANTIĞI ---
        if (Runner.CurrentLevel < 3)
        {
            actionMask.SetActionEnabled(0, 1, false); // Ev
            actionMask.SetActionEnabled(0, 2, false); // Kışla
            actionMask.SetActionEnabled(0, 7, false); // Çiftlik
            actionMask.SetActionEnabled(0, 8, false); // Oduncu
            actionMask.SetActionEnabled(0, 9, false); // Taş Ocağı
        }

        // Level 4'ten önce ASKER ÜRETİLEMEZ ve SALDIRILAMAZ
        if (Runner.CurrentLevel < 4)
        {
            actionMask.SetActionEnabled(0, 4, false); // Asker Üret
            actionMask.SetActionEnabled(0, 5, false); // Saldır
        }

        // 1. KAYNAK YETERSİZLİĞİ KONTROLLERİ
        // Ev (House)
        if (!SimResourceSystem.CanAfford(_world, 1, SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT))
            actionMask.SetActionEnabled(0, 1, false);

        // Kışla (Barracks)
        if (!SimResourceSystem.CanAfford(_world, 1, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT))
            actionMask.SetActionEnabled(0, 2, false);

        // Çiftlik (Farm) - YENİ
        if (!SimResourceSystem.CanAfford(_world, 1, SimConfig.FARM_COST_WOOD, SimConfig.FARM_COST_STONE, SimConfig.FARM_COST_MEAT))
            actionMask.SetActionEnabled(0, 7, false);

        // Oduncu (WoodCutter) - YENİ
        if (!SimResourceSystem.CanAfford(_world, 1, SimConfig.WOODCUTTER_COST_WOOD, SimConfig.WOODCUTTER_COST_STONE, SimConfig.WOODCUTTER_COST_MEAT))
            actionMask.SetActionEnabled(0, 8, false);

        // Taş Ocağı (StonePit) - YENİ
        if (!SimResourceSystem.CanAfford(_world, 1, SimConfig.STONEPIT_COST_WOOD, SimConfig.STONEPIT_COST_STONE, SimConfig.STONEPIT_COST_MEAT))
            actionMask.SetActionEnabled(0, 9, false);


        // 2. ÜRETİM KONTROLLERİ
        // İşçi (Base lazım + Para lazım + Yer lazım)
        bool canAffordWorker = SimResourceSystem.CanAfford(_world, 1, SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT);
        if (!hasBase || !canAffordWorker || player.CurrentPopulation >= player.MaxPopulation)
            actionMask.SetActionEnabled(0, 3, false);

        // Asker (Kışla lazım + Para lazım + Yer lazım)
        bool canAffordSoldier = SimResourceSystem.CanAfford(_world, 1, SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT);
        if (!hasBarracks || !canAffordSoldier || player.CurrentPopulation >= player.MaxPopulation)
            actionMask.SetActionEnabled(0, 4, false);


        // 3. BİRİM VARLIĞI KONTROLLERİ
        if (soldierCount == 0)
            actionMask.SetActionEnabled(0, 5, false); // Asker yoksa saldıramaz

        if (!hasWorker)
        {
            // İşçi yoksa hiç bir şey inşa edemez ve toplayamaz
            actionMask.SetActionEnabled(0, 1, false); // Ev
            actionMask.SetActionEnabled(0, 2, false); // Kışla
            actionMask.SetActionEnabled(0, 6, false); // Topla
            actionMask.SetActionEnabled(0, 7, false); // Çiftlik (YENİ)
            actionMask.SetActionEnabled(0, 8, false); // Oduncu (YENİ)
            actionMask.SetActionEnabled(0, 9, false); // Taş Ocağı (YENİ)
        }
    }
    private void OnDrawGizmos()
    {
        if (_world == null || _lastDebugX == -1) return;

        // Hedeflenen kareyi boya
        Vector3 targetPos = new Vector3(_lastDebugX, 0.5f, _lastDebugY); // Yükseklik 0.5

        // Komuta göre renk seç
        Color debugColor = Color.white;
        switch (_lastDebugCommand)
        {
            case 0: debugColor = Color.gray; break; // Bekle
            case 1: case 2: case 7: case 8: case 9: debugColor = Color.yellow; break; // İnşaat
            case 3: case 4: debugColor = Color.cyan; break; // Üretim
            case 5: debugColor = Color.red; break; // Saldırı
            case 6: debugColor = Color.green; break; // Toplama
        }

        Gizmos.color = debugColor;
        Gizmos.DrawWireCube(targetPos, new Vector3(0.9f, 0.1f, 0.9f));
        Gizmos.DrawLine(transform.position, targetPos); // Ajanın merkezinden hedefe çizgi çek
    }
}