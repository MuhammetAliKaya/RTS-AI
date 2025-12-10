using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
using System.Linq; // Linq kütüphanesini eklemeyi unutma



public class RTSAgent : Agent
{
    private SimWorldState _world;
    private SimGridSystem _gridSystem;
    private SimUnitSystem _unitSystem; // ARTIK BU ÖNEMLİ
    private SimBuildingSystem _buildingSystem;

    private DRLActionTranslator _translator;
    private RTSGridSensor _gridSensor;

    public DRLSimRunner Runner;
    public AdversarialTrainerRunner CombatRunner;

    private int _lastDebugX = -1;
    private int _lastDebugY = -1;
    private int _lastDebugCommand = 0;
    private RTSGridSensorComponent _gridSensorComp;
    public bool ShowDebugLogs = true;


    public void Setup(SimWorldState world, SimGridSystem gridSys, SimUnitSystem unitSys, SimBuildingSystem buildSys)
    {
        _world = world;
        _gridSystem = gridSys;
        _unitSystem = unitSys;
        _buildingSystem = buildSys;

        // --- YENİ EKLENEN KISIM ---
        // Bileşeni bul (Agent ile aynı obje üzerinde olmalı)
        if (_gridSensorComp == null)
            _gridSensorComp = GetComponent<RTSGridSensorComponent>();

        // Sensöre world verisini gönder ki okuyabilsin
        if (_gridSensorComp != null)
        {
            _gridSensorComp.InitializeSensor(_world, _gridSystem);
        }
        // --------------------------
        _translator = new DRLActionTranslator(_world, _unitSystem, _buildingSystem, _gridSystem);

        // Setup çağrıldığında eventleri yeniden bağlamamız gerekebilir
        // Ama genellikle OnEnable yeterlidir. Setup OnEnable'dan sonra çağrılırsa diye:
        RebindEvents();
    }

    // --- DEĞİŞİKLİK BURADA: Static Event yerine Instance Event ---
    protected override void OnEnable()
    {
        base.OnEnable();
        RebindEvents();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (_unitSystem != null)
        {
            _unitSystem.OnUnitAttackedUnit -= HandleUnitAttackedUnit;
            _unitSystem.OnUnitAttackedBuilding -= HandleUnitAttackedBuilding;
            _unitSystem.OnUnitKilledEnemy -= HandleUnitKilledEnemy;
            _unitSystem.OnUnitDestroyedBuilding -= HandleUnitDestroyedBuilding;
        }
    }

    private void RebindEvents()
    {
        // Önce temizle (çift aboneliği önlemek için)
        if (_unitSystem != null)
        {
            _unitSystem.OnUnitAttackedUnit -= HandleUnitAttackedUnit;
            _unitSystem.OnUnitAttackedBuilding -= HandleUnitAttackedBuilding;
            _unitSystem.OnUnitKilledEnemy -= HandleUnitKilledEnemy;
            _unitSystem.OnUnitDestroyedBuilding -= HandleUnitDestroyedBuilding;

            // Sonra ekle
            _unitSystem.OnUnitAttackedUnit += HandleUnitAttackedUnit;
            _unitSystem.OnUnitAttackedBuilding += HandleUnitAttackedBuilding;
            _unitSystem.OnUnitKilledEnemy += HandleUnitKilledEnemy;
            _unitSystem.OnUnitDestroyedBuilding += HandleUnitDestroyedBuilding;
        }
    }
    // -------------------------------------------------------------

    private void HandleUnitAttackedUnit(SimUnitData attacker, SimUnitData victim, float damage)
    {
        if (attacker.PlayerID == 1) AddReward(damage * 0.002f);
    }

    private void HandleUnitAttackedBuilding(SimUnitData attacker, SimBuildingData building, float damage)
    {
        if (attacker.PlayerID == 1)
        {
            float multiplier = (building.Type == SimBuildingType.Base) ? 0.005f : 0.002f;
            AddReward(damage * multiplier);
        }
    }

    private void HandleUnitKilledEnemy(SimUnitData attacker, SimUnitData victim)
    {
        if (attacker.PlayerID == 1) AddReward(1.0f);
    }

    private void HandleUnitDestroyedBuilding(SimUnitData attacker, SimBuildingData building)
    {
        if (attacker.PlayerID == 1)
        {
            if (building.Type == SimBuildingType.Base)
            {
                AddReward(10.0f);
                EndEpisode();
            }
            else AddReward(2.0f);
        }
    }

    public override void OnEpisodeBegin()
    {
        if (Runner != null) Runner.ResetSimulation();
        else if (CombatRunner != null) CombatRunner.ResetSimulation();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (_world == null)
        {
            for (int i = 0; i < 6; i++) sensor.AddObservation(0f);
            return;
        }

        if (_world.Players.ContainsKey(1))
        {
            var me = _world.Players[1];
            sensor.AddObservation(me.Wood / 1000f);
            sensor.AddObservation(me.Stone / 500f);
            sensor.AddObservation(me.Meat / 500f);
            sensor.AddObservation(me.CurrentPopulation / 50f);

            int soldierCount = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Soldier && u.State != SimTaskType.Dead);
            int workerCount = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Worker && u.State != SimTaskType.Dead);

            sensor.AddObservation(soldierCount / 20f);
            sensor.AddObservation(workerCount / 20f);
        }
        else
        {
            for (int i = 0; i < 6; i++) sensor.AddObservation(0f);
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (_world == null) return;

        int actionType = actions.DiscreteActions[0];
        int targetIndex = actions.DiscreteActions[1];

        // --- DEBUG LOG: NE GELDİ? ---
        if (ShowDebugLogs && actionType != 0) // Bekle (0) dışındaki her şeyi yaz
        {
            Debug.Log($"[AGENT] Action Received -> Type: {actionType}, TargetIndex: {targetIndex}");
        }

        bool isSuccess = _translator.ExecuteAction(actionType, targetIndex);

        if (!isSuccess && actionType != 0) AddReward(-0.001f);
        AddReward(-0.0001f);
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        if (_world == null || !_world.Players.ContainsKey(1)) return;
        var player = _world.Players[1];

        // 1. Mevcut Durumu Analiz Et
        bool hasWorker = false;
        int soldierCount = 0;
        bool hasBarracks = false;
        bool hasBase = false;

        // Üniteleri say
        foreach (var u in _world.Units.Values)
        {
            if (u.PlayerID == 1 && u.State != SimTaskType.Dead)
            {
                if (u.UnitType == SimUnitType.Worker) hasWorker = true;
                if (u.UnitType == SimUnitType.Soldier) soldierCount++;
            }
        }

        // Binaları kontrol et
        foreach (var b in _world.Buildings.Values)
        {
            if (b.PlayerID == 1 && b.IsConstructed && b.Health > 0)
            {
                if (b.Type == SimBuildingType.Barracks) hasBarracks = true;
                if (b.Type == SimBuildingType.Base) hasBase = true;
            }
        }

        // --- AKSİYON MASKELEME (0-11 Arası) ---
        // 0: Bekle (Her zaman açık)

        // 1: İşçi Üret (Base lazım, Kaynak lazım, Nüfus yerim var mı?)
        bool canAffordWorker = SimResourceSystem.CanAfford(_world, 1, SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT);
        if (!hasBase || !canAffordWorker || player.CurrentPopulation >= player.MaxPopulation)
        {
            actionMask.SetActionEnabled(0, 1, false);
        }

        // 2: Asker Üret (Barracks lazım, Kaynak lazım, Nüfus yerim var mı?)
        bool canAffordSoldier = SimResourceSystem.CanAfford(_world, 1, SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT);
        if (!hasBarracks || !canAffordSoldier || player.CurrentPopulation >= player.MaxPopulation)
        {
            actionMask.SetActionEnabled(0, 2, false);
        }

        // --- İNŞAATLAR (İşçi Lazım + Kaynak Lazım) ---
        // Eğer işçim yoksa inşaat yapamam (3-9 arası kilitlenir)
        if (!hasWorker)
        {
            for (int i = 3; i <= 9; i++) actionMask.SetActionEnabled(0, i, false);

            // İşçi yoksa "İşçi Komutu" da verilemez (Action 11)
            actionMask.SetActionEnabled(0, 11, false);
        }
        else
        {
            // İşçim var, peki param var mı?
            CheckAffordability(actionMask, 3, SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT);        // House
            CheckAffordability(actionMask, 4, SimConfig.FARM_COST_WOOD, SimConfig.FARM_COST_STONE, SimConfig.FARM_COST_MEAT);          // Farm
            CheckAffordability(actionMask, 5, SimConfig.WOODCUTTER_COST_WOOD, SimConfig.WOODCUTTER_COST_STONE, SimConfig.WOODCUTTER_COST_MEAT); // WoodCutter
            CheckAffordability(actionMask, 6, SimConfig.STONEPIT_COST_WOOD, SimConfig.STONEPIT_COST_STONE, SimConfig.STONEPIT_COST_MEAT); // StonePit
            CheckAffordability(actionMask, 7, SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT); // Barracks

            // Kule ve Duvar maliyetleri Config'de yoksa manuel girebilirsin veya Config'e ekle
            // Örnek değerler: Tower (100,50,0), Wall (20,10,0)
            if (!SimResourceSystem.CanAfford(_world, 1, 100, 50, 0)) actionMask.SetActionEnabled(0, 8, false); // Tower
            if (!SimResourceSystem.CanAfford(_world, 1, 20, 10, 0)) actionMask.SetActionEnabled(0, 9, false);  // Wall
        }

        // 10: ORDUYU YÖNET (Askerim yoksa kapalı)
        if (soldierCount == 0)
        {
            actionMask.SetActionEnabled(0, 10, false);
        }

        // 11: İŞÇİYİ YÖNET (Yukarıda !hasWorker bloğunda zaten kapattık ama açıkta kalan durum varsa)
        // Eğer işçi varsa her zaman komut verilebilir (Topla/Yürü), ekstra kaynağa gerek yok.
    }

    private void CheckAffordability(IDiscreteActionMask mask, int actionIndex, int w, int s, int m)
    {
        if (!SimResourceSystem.CanAfford(_world, 1, w, s, m))
        {
            mask.SetActionEnabled(0, actionIndex, false);
        }
    }



    private void OnDrawGizmos()
    {
        if (_world == null || _lastDebugX == -1) return;
        Vector3 targetPos = new Vector3(_lastDebugX, 0.5f, _lastDebugY);
        Color debugColor = Color.white;
        switch (_lastDebugCommand)
        {
            case 0: debugColor = Color.gray; break;
            case 1: case 2: case 7: case 8: case 9: debugColor = Color.yellow; break;
            case 3: case 4: debugColor = Color.cyan; break;
            case 5: debugColor = Color.red; break;
            case 6: debugColor = Color.green; break;
        }
        Gizmos.color = debugColor;
        Gizmos.DrawWireCube(targetPos, new Vector3(0.9f, 0.1f, 0.9f));
        Gizmos.DrawLine(transform.position, targetPos);
    }

    // --- HEURISTIC KONTROL (OYUNCU GİRDİSİ) ---
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        Debug.Log($"[HEURISTIC] Right Click Detected at Index: HEREEE");
        var discreteActions = actionsOut.DiscreteActions;
        discreteActions[0] = 0;
        discreteActions[1] = 0;

        // 1. MOUSE POZİSYONU
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, 0);
        float rayDist;

        int gridIndex = 0;
        if (groundPlane.Raycast(ray, out rayDist))
        {
            Vector3 worldPos = ray.GetPoint(rayDist);
            int gx = Mathf.RoundToInt(worldPos.x);
            int gy = Mathf.RoundToInt(worldPos.z);

            if (gx >= 0 && gx < _world.Map.Width && gy >= 0 && gy < _world.Map.Height)
            {
                gridIndex = (gy * _world.Map.Width) + gx;
                discreteActions[1] = gridIndex;
            }
        }

        // 2. KLAVYE/MOUSE INPUTLARI (Loglu)
        int selectedAction = 0;

        if (Input.GetKey(KeyCode.Q)) selectedAction = 1;      // İşçi
        else if (Input.GetKey(KeyCode.W)) selectedAction = 2; // Asker
        else if (Input.GetKey(KeyCode.H)) selectedAction = 3; // House
        else if (Input.GetKey(KeyCode.F)) selectedAction = 4; // Farm (Barracks yazılmış eski kodda, dikkat et)
                                                              // NOT: Senin translator sıralaman şuydu: 
                                                              // 1:House, 2:Barracks, 3:Worker, 4:Soldier. DÜZELTİYORUM:

        // Translator Case'lerine göre Doğru Mapping:
        // Case 1: House
        // Case 2: Barracks
        // Case 3: Worker
        // Case 4: Soldier
        // Case 5: Rush Attack
        // Case 6: Nearest Attack
        // Case 7: Auto Gather
        // Case 10: Manual Army
        // Case 11: Manual Worker

        if (Input.GetKey(KeyCode.H)) selectedAction = 1;      // House
        else if (Input.GetKey(KeyCode.B)) selectedAction = 2; // Barracks
        else if (Input.GetKey(KeyCode.Q)) selectedAction = 3; // Worker
        else if (Input.GetKey(KeyCode.W)) selectedAction = 4; // Soldier
        else if (Input.GetKeyDown(KeyCode.Space)) selectedAction = 5; // Rush
        else if (Input.GetKey(KeyCode.A)) selectedAction = 6; // Attack Nearest
        else if (Input.GetKey(KeyCode.G)) selectedAction = 7; // Auto Gather

        // --- MOUSE TIKLAMALARI ---
        else if (Input.GetMouseButton(1))
        {
            selectedAction = 10; // Sağ Tık (Ordu)
            if (ShowDebugLogs) Debug.Log($"[HEURISTIC] Right Click Detected at Index: {gridIndex}");
        }
        else if (Input.GetMouseButton(0))
        {
            selectedAction = 11; // Sol Tık (İşçi)
            if (ShowDebugLogs) Debug.Log($"[HEURISTIC] Left Click Detected at Index: {gridIndex}");
        }

        if (selectedAction != 0)
        {
            discreteActions[0] = selectedAction;
            if (ShowDebugLogs) Debug.Log($"[HEURISTIC] Input Detected -> Key Action: {selectedAction}");
        }
    }
}