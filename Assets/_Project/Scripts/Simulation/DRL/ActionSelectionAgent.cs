using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;

public class ActionSelectionAgent : Agent
{
    private RTSOrchestrator _orchestrator;
    private SimWorldState _world;
    private DRLActionTranslator _translator;

    // --- SABİTLER (RTSAgent'tan alındı) ---
    private const int ACT_WAIT = 0;
    private const int ACT_BUILD_HOUSE = 1;
    private const int ACT_BUILD_BARRACKS = 2;
    private const int ACT_TRAIN_WORKER = 3;
    private const int ACT_TRAIN_SOLDIER = 4;
    private const int ACT_BUILD_WOODCUTTER = 5;
    private const int ACT_BUILD_STONEPIT = 6;
    private const int ACT_BUILD_FARM = 7;
    private const int ACT_BUILD_TOWER = 8;
    private const int ACT_BUILD_WALL = 9;
    private const int ACT_ATTACK_ENEMY = 10;
    private const int ACT_MOVE_TO = 11;
    private const int ACT_GATHER_RES = 12;

    private bool _hasPendingDemo = false;
    private int _pendingActionType = 0;

    public void Setup(RTSOrchestrator orchestrator, SimWorldState world, DRLActionTranslator translator)
    {
        _orchestrator = orchestrator;
        _world = world;
        _translator = translator;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Gözlem 1: Seçilen Birim Türü (Context)
        // Orkestratörden hangi birimin seçildiğini öğreniyoruz.
        int selectedIndex = _orchestrator.SelectedSourceIndex;
        float contextObs = _translator.GetEncodedTypeAt(selectedIndex);
        sensor.AddObservation(contextObs);

        // Gözlem 2: Kaynaklar (Maliyet hesabı yapabilmesi için)
        if (_world.Players.ContainsKey(_orchestrator.MyPlayerID))
        {
            var me = _world.Players[_orchestrator.MyPlayerID];
            // Logaritmik scale yerine şimdilik direkt değer veya basit scale verebiliriz.
            // RTSAgent'taki LogScale fonksiyonu varsa onu da kullanabilirsin ama
            // basitçe 1000'e bölmek de iş görür.
            sensor.AddObservation(me.Wood / 1000f);
            sensor.AddObservation(me.Stone / 1000f);
            sensor.AddObservation(me.Meat / 1000f);
            sensor.AddObservation(me.CurrentPopulation / 50f);
        }
        else
        {
            sensor.AddObservation(new float[4]);
        }
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        // 1. Veri Doğrulama
        if (_world == null || !_world.Players.ContainsKey(_orchestrator.MyPlayerID)) return;

        int sourceIndex = _orchestrator.SelectedSourceIndex;

        // --- DÜZELTİLMESİ GEREKEN KISIM BURASI ---
        if (sourceIndex == -1)
        {
            actionMask.SetActionEnabled(0, ACT_WAIT, true); // Bekle (0) her zaman açık olsun

            // HATA ÇÖZÜMÜ: Döngüyü 50 yerine 13 ile sınırla.
            // Branch Size ayarın 13 olduğu için sadece 1'den 12'ye kadar olanları kapatmalısın.
            for (int i = 1; i < 13; i++)
            {
                actionMask.SetActionEnabled(0, i, false);
            }
            return;
        }

        SimUnitType unitType = _translator.GetUnitTypeAt(sourceIndex);
        SimBuildingType buildingType = _translator.GetBuildingTypeAt(sourceIndex);
        var me = _world.Players[_orchestrator.MyPlayerID];

        // Yardımcı Fonksiyon: Kaynak Yetiyor mu?
        bool CanAfford(int w, int s, int m) => me.Wood >= w && me.Stone >= s && me.Meat >= m;

        // 2. Action Space Sınırlama
        // Bizim 0'dan 12'ye kadar eylemimiz var.
        // ML-Agents "Discrete Branch 0 Size" muhtemelen MapSize (örn. 1024) kadar ayarlıdır.
        // Bu yüzden 13 ve sonrasını tamamen kapatıyoruz.
        int totalMapActionSize = _world.Map.Width * _world.Map.Height;
        // Eğer Branch Size harita boyutu kadarsa (ActionSelector için 13 yeterli ama muhtemelen ortak config kullanılıyor):
        // for (int i = 13; i < totalMapActionSize; i++)
        // {
        //     actionMask.SetActionEnabled(0, i, false);
        // }

        // ---------------------------------------------------------
        // SENARYO 1: İŞÇİ (WORKER)
        // ---------------------------------------------------------
        if (unitType == SimUnitType.Worker)
        {
            // --- YAPAMAYACAKLARI ---
            actionMask.SetActionEnabled(0, ACT_TRAIN_WORKER, false);  // Üretim yapamaz
            actionMask.SetActionEnabled(0, ACT_TRAIN_SOLDIER, false);
            actionMask.SetActionEnabled(0, ACT_ATTACK_ENEMY, false);  // İşçi saldırmaz (Tercihen)

            // --- YAPABİLECEKLERİ (Temel) ---
            actionMask.SetActionEnabled(0, ACT_MOVE_TO, true);
            actionMask.SetActionEnabled(0, ACT_GATHER_RES, true);
            actionMask.SetActionEnabled(0, ACT_WAIT, true);

            // --- İNŞAAT (Maliyet Kontrolü) ---
            actionMask.SetActionEnabled(0, ACT_BUILD_HOUSE, CanAfford(SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT));
            actionMask.SetActionEnabled(0, ACT_BUILD_BARRACKS, CanAfford(SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT));
            actionMask.SetActionEnabled(0, ACT_BUILD_WOODCUTTER, CanAfford(SimConfig.WOODCUTTER_COST_WOOD, SimConfig.WOODCUTTER_COST_STONE, SimConfig.WOODCUTTER_COST_MEAT));
            actionMask.SetActionEnabled(0, ACT_BUILD_STONEPIT, CanAfford(SimConfig.STONEPIT_COST_WOOD, SimConfig.STONEPIT_COST_STONE, SimConfig.STONEPIT_COST_MEAT));
            actionMask.SetActionEnabled(0, ACT_BUILD_FARM, CanAfford(SimConfig.FARM_COST_WOOD, SimConfig.FARM_COST_STONE, SimConfig.FARM_COST_MEAT));
            actionMask.SetActionEnabled(0, ACT_BUILD_TOWER, CanAfford(SimConfig.TOWER_COST_WOOD, SimConfig.TOWER_COST_STONE, SimConfig.TOWER_COST_MEAT));
            actionMask.SetActionEnabled(0, ACT_BUILD_WALL, CanAfford(SimConfig.WALL_COST_WOOD, SimConfig.WALL_COST_STONE, SimConfig.WALL_COST_MEAT));
        }
        // ---------------------------------------------------------
        // SENARYO 2: ASKER (SOLDIER)
        // ---------------------------------------------------------
        else if (unitType == SimUnitType.Soldier)
        {
            // --- YAPAMAYACAKLARI ---
            // İnşaatların hepsi kapalı (1, 2, 5, 6, 7, 8, 9)
            actionMask.SetActionEnabled(0, ACT_BUILD_HOUSE, false);
            actionMask.SetActionEnabled(0, ACT_BUILD_BARRACKS, false);
            actionMask.SetActionEnabled(0, ACT_BUILD_WOODCUTTER, false);
            actionMask.SetActionEnabled(0, ACT_BUILD_STONEPIT, false);
            actionMask.SetActionEnabled(0, ACT_BUILD_FARM, false);
            actionMask.SetActionEnabled(0, ACT_BUILD_TOWER, false);
            actionMask.SetActionEnabled(0, ACT_BUILD_WALL, false);

            // Üretim ve Toplama kapalı
            actionMask.SetActionEnabled(0, ACT_TRAIN_WORKER, false);
            actionMask.SetActionEnabled(0, ACT_TRAIN_SOLDIER, false);
            actionMask.SetActionEnabled(0, ACT_GATHER_RES, false);

            // --- YAPABİLECEKLERİ ---
            actionMask.SetActionEnabled(0, ACT_ATTACK_ENEMY, true);
            actionMask.SetActionEnabled(0, ACT_MOVE_TO, true);
            actionMask.SetActionEnabled(0, ACT_WAIT, true);
        }
        // ---------------------------------------------------------
        // SENARYO 3: BİNA (BUILDING)
        // ---------------------------------------------------------
        else if (buildingType != SimBuildingType.None)
        {
            // Binalar yürüyemez, saldıramaz (kule hariç otomatiktir), toplayamaz, inşa edemez.
            actionMask.SetActionEnabled(0, ACT_ATTACK_ENEMY, false);
            actionMask.SetActionEnabled(0, ACT_MOVE_TO, false);
            actionMask.SetActionEnabled(0, ACT_GATHER_RES, false);

            // Tüm inşaat butonlarını kapat
            actionMask.SetActionEnabled(0, ACT_BUILD_HOUSE, false);
            actionMask.SetActionEnabled(0, ACT_BUILD_BARRACKS, false);
            actionMask.SetActionEnabled(0, ACT_BUILD_WOODCUTTER, false);
            actionMask.SetActionEnabled(0, ACT_BUILD_STONEPIT, false);
            actionMask.SetActionEnabled(0, ACT_BUILD_FARM, false);
            actionMask.SetActionEnabled(0, ACT_BUILD_TOWER, false);
            actionMask.SetActionEnabled(0, ACT_BUILD_WALL, false);

            // --- ÜRETİM KONTROLÜ ---
            if (buildingType == SimBuildingType.Base)
            {
                // Base sadece İşçi basar
                bool canTrain = CanAfford(SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT);
                actionMask.SetActionEnabled(0, ACT_TRAIN_WORKER, canTrain);
                actionMask.SetActionEnabled(0, ACT_TRAIN_SOLDIER, false);
            }
            else if (buildingType == SimBuildingType.Barracks)
            {
                // Barracks sadece Asker basar
                bool canTrain = CanAfford(SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT);
                actionMask.SetActionEnabled(0, ACT_TRAIN_SOLDIER, canTrain);
                actionMask.SetActionEnabled(0, ACT_TRAIN_WORKER, false);
            }
            else
            {
                // Ev, Kule, Farm vb. üretim yapamaz
                actionMask.SetActionEnabled(0, ACT_TRAIN_WORKER, false);
                actionMask.SetActionEnabled(0, ACT_TRAIN_SOLDIER, false);
            }

            actionMask.SetActionEnabled(0, ACT_WAIT, true); // Bekle her zaman serbest
        }
        else
        {
            // Ne birim ne bina (Hatalı durum veya boşluk) -> Sadece Wait
            for (int i = 1; i <= 12; i++) actionMask.SetActionEnabled(0, i, false);
            actionMask.SetActionEnabled(0, ACT_WAIT, true);
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        int actionType = actions.DiscreteActions[0];
        _orchestrator.OnActionSelected(actionType);
    }

    public void RegisterExternalAction(int actionType, int sourceIndex, int targetIndex)
    {
        _pendingActionType = actionType;
        _hasPendingDemo = true;

        RequestDecision();
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discreteActions = actionsOut.DiscreteActions;

        if (_hasPendingDemo)
        {
            discreteActions[0] = _pendingActionType;
            _hasPendingDemo = false;
        }
        else
        {
            discreteActions[0] = 0; // Wait
        }
    }
}