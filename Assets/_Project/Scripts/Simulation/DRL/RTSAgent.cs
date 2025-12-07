using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;

public class RTSAgent : Agent
{
    private SimWorldState _world;
    private SimGridSystem _gridSystem;
    private SimUnitSystem _unitSystem;
    private SimBuildingSystem _buildingSystem;

    private DRLActionTranslator _translator;
    private RTSGridSensor _gridSensor;

    public DRLSimRunner Runner;

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
        // 1. Simülasyon henüz başlamadıysa veya oyuncu yoksa hiçbir şey yapma
        if (_world == null || !_world.Players.ContainsKey(1)) return;

        var player = _world.Players[1]; // Ajanın Player ID'si (Genelde 1)

        // --- BRANCH 0: KOMUTLAR (Commands) ---
        // SimConfig dosyasındaki maliyetlere göre tuşları kapatıyoruz.

        // -----------------------------------------------------------
        // 1. BİRİM ÜRETİMİ (WORKER & SOLDIER)
        // -----------------------------------------------------------

        // İŞÇİ (WORKER) BASMAK
        // Gereksinim: Et Maliyeti + Nüfus Limiti
        // Varsayılan Index: 10 (Kendi ActionTranslator listene göre kontrol et!)
        bool canBuildWorker = player.Meat >= SimConfig.WORKER_COST_MEAT &&
                              player.Wood >= SimConfig.WORKER_COST_WOOD && // Genelde 0 ama Config'e sadık kalalım
                              player.CurrentPopulation < player.MaxPopulation;

        if (!canBuildWorker)
        {
            actionMask.SetActionEnabled(0, 10, false);
        }

        // ASKER (SOLDIER) BASMAK
        // Gereksinim: Et + Odun + Nüfus Limiti
        // Varsayılan Index: 11
        bool canBuildSoldier = player.Meat >= SimConfig.SOLDIER_COST_MEAT &&
                               player.Wood >= SimConfig.SOLDIER_COST_WOOD &&
                               player.CurrentPopulation < player.MaxPopulation;

        if (!canBuildSoldier)
        {
            actionMask.SetActionEnabled(0, 11, false);
        }

        // -----------------------------------------------------------
        // 2. BİNA İNŞAATI
        // -----------------------------------------------------------

        // EV (HOUSE)
        // Varsayılan Index: 4
        bool canBuildHouse = player.Wood >= SimConfig.HOUSE_COST_WOOD &&
                             player.Stone >= SimConfig.HOUSE_COST_STONE &&
                             player.Meat >= SimConfig.HOUSE_COST_MEAT;

        if (!canBuildHouse) actionMask.SetActionEnabled(0, 4, false);


        // KIŞLA (BARRACKS)
        // Varsayılan Index: 5
        bool canBuildBarracks = player.Wood >= SimConfig.BARRACKS_COST_WOOD &&
                                player.Stone >= SimConfig.BARRACKS_COST_STONE &&
                                player.Meat >= SimConfig.BARRACKS_COST_MEAT;

        if (!canBuildBarracks) actionMask.SetActionEnabled(0, 5, false);


        // KULE (TOWER)
        // Varsayılan Index: 6 (Varsayım)
        bool canBuildTower = player.Wood >= SimConfig.TOWER_COST_WOOD &&
                             player.Stone >= SimConfig.TOWER_COST_STONE &&
                             player.Meat >= SimConfig.TOWER_COST_MEAT;

        if (!canBuildTower) actionMask.SetActionEnabled(0, 6, false);


        // ÇİFTLİK (FARM)
        // Varsayılan Index: 7
        bool canBuildFarm = player.Wood >= SimConfig.FARM_COST_WOOD &&
                            player.Stone >= SimConfig.FARM_COST_STONE &&
                            player.Meat >= SimConfig.FARM_COST_MEAT;

        if (!canBuildFarm) actionMask.SetActionEnabled(0, 7, false);


        // ODUNCU (WOODCUTTER)
        // Varsayılan Index: 8
        bool canBuildLumber = player.Wood >= SimConfig.WOODCUTTER_COST_WOOD &&
                              player.Stone >= SimConfig.WOODCUTTER_COST_STONE &&
                              player.Meat >= SimConfig.WOODCUTTER_COST_MEAT;

        if (!canBuildLumber) actionMask.SetActionEnabled(0, 8, false);


        // TAŞ OCAĞI (STONEPIT)
        // Varsayılan Index: 9
        bool canBuildStonePit = player.Wood >= SimConfig.STONEPIT_COST_WOOD &&
                                player.Stone >= SimConfig.STONEPIT_COST_STONE &&
                                player.Meat >= SimConfig.STONEPIT_COST_MEAT;

        if (!canBuildStonePit) actionMask.SetActionEnabled(0, 9, false);
    }
}