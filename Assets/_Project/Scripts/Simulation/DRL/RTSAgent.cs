using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
using System.Linq;

public class RTSAgent : Agent
{
    // Singleton erişim (UI ve InputManager'ın ulaşması için)
    public static RTSAgent Instance;

    private SimWorldState _world;
    private SimGridSystem _gridSystem;
    private SimUnitSystem _unitSystem;
    private SimBuildingSystem _buildingSystem;

    private DRLActionTranslator _translator;
    private RTSGridSensorComponent _gridSensorComp;

    public DRLSimRunner Runner;
    public AdversarialTrainerRunner CombatRunner; // İsteğe bağlı, varsa kalsın

    public bool ShowDebugLogs = true;

    // --- DIŞ EMRİ TUTAN DEĞİŞKENLER (BUFFER) ---
    private int _overrideActionType = 0;
    private int _overrideSourceIndex = 0;
    private int _overrideTargetIndex = 0;

    protected override void Awake()
    {
        base.Awake();
        Instance = this;
    }

    public void Setup(SimWorldState world, SimGridSystem gridSys, SimUnitSystem unitSys, SimBuildingSystem buildSys)
    {
        _world = world;
        _gridSystem = gridSys;
        _unitSystem = unitSys;
        _buildingSystem = buildSys;

        // Grid Sensor Bağlantısı
        if (_gridSensorComp == null)
            _gridSensorComp = GetComponent<RTSGridSensorComponent>();

        if (_gridSensorComp != null)
        {
            _gridSensorComp.InitializeSensor(_world, _gridSystem);
        }

        // Translator Kurulumu
        _translator = new DRLActionTranslator(_world, _unitSystem, _buildingSystem, _gridSystem);

        RebindEvents();
    }

    // --- EVENT YÖNETİMİ ---
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
        if (_unitSystem != null)
        {
            _unitSystem.OnUnitAttackedUnit -= HandleUnitAttackedUnit;
            _unitSystem.OnUnitAttackedBuilding -= HandleUnitAttackedBuilding;
            _unitSystem.OnUnitKilledEnemy -= HandleUnitKilledEnemy;
            _unitSystem.OnUnitDestroyedBuilding -= HandleUnitDestroyedBuilding;

            _unitSystem.OnUnitAttackedUnit += HandleUnitAttackedUnit;
            _unitSystem.OnUnitAttackedBuilding += HandleUnitAttackedBuilding;
            _unitSystem.OnUnitKilledEnemy += HandleUnitKilledEnemy;
            _unitSystem.OnUnitDestroyedBuilding += HandleUnitDestroyedBuilding;
        }
    }

    // --- ÖDÜL FONKSİYONLARI ---
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

    // --- DIŞ DÜNYADAN (UI/MOUSE) GELEN EMRİ KAYDET ---
    public void RegisterExternalAction(int actionType, int sourceIndex, int targetIndex)
    {
        _overrideActionType = actionType;
        _overrideSourceIndex = sourceIndex;
        _overrideTargetIndex = targetIndex;

        // İsteğe bağlı: Anlık tepki için karar isteyebilirsin
        RequestDecision();
    }

    public override void OnEpisodeBegin()
    {
        if (Runner != null) Runner.ResetSimulation();
        else if (CombatRunner != null) CombatRunner.ResetSimulation();

        // Buffer'ı temizle
        _overrideActionType = 0;
        _overrideSourceIndex = 0;
        _overrideTargetIndex = 0;
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
            sensor.AddObservation(me.Wood / 2000f);
            sensor.AddObservation(me.Stone / 1000f);
            sensor.AddObservation(me.Meat / 1000f);
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

    // --- EYLEM UYGULAMA (Action + Source + Target) ---
    public override void OnActionReceived(ActionBuffers actions)
    {
        if (_world == null) return;

        // 3 DAL (Branch) OKUYORUZ
        int actionType = actions.DiscreteActions[0]; // Ne yapılacak?
        int sourceIndex = actions.DiscreteActions[1]; // Kim yapacak?
        int targetIndex = actions.DiscreteActions[2]; // Nereye yapacak?

        // --- DEBUG LOG ---
        if (ShowDebugLogs && actionType != 0)
        {
            Debug.Log($"[AGENT] Action: {actionType}, Source: {sourceIndex}, Target: {targetIndex}");
        }

        // Translator'a gönder
        bool isSuccess = _translator.ExecuteAction(actionType, sourceIndex, targetIndex);

        // Cezalar / Ödüller
        if (!isSuccess && actionType != 0) AddReward(-0.001f); // Geçersiz hamle
        AddReward(-0.0001f); // Zaman cezası
    }

    // --- HEURISTIC (ÖĞRETMEN MODU) ---
    // Artık klavye okumuyor, RegisterExternalAction ile gelen veriyi kullanıyor.
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discreteActions = actionsOut.DiscreteActions;

        // 1. Eğer dışarıdan (UI/InputManager) bir emir geldiyse onu işle
        if (_overrideActionType != 0)
        {
            discreteActions[0] = _overrideActionType;
            discreteActions[1] = _overrideSourceIndex;
            discreteActions[2] = _overrideTargetIndex;

            if (ShowDebugLogs)
                Debug.Log($"[HEURISTIC] External Input -> Act:{_overrideActionType} Src:{_overrideSourceIndex} Tgt:{_overrideTargetIndex}");

            // Emri kullandık, sıfırla ki tekrar tekrar yapmasın
            _overrideActionType = 0;
            _overrideSourceIndex = 0;
            _overrideTargetIndex = 0;
        }
        else
        {
            // Emir yoksa 'Bekle'
            discreteActions[0] = 0;
            discreteActions[1] = 0;
            discreteActions[2] = 0;
        }
    }

    // Maskeleme şimdilik kapalı kalabilir veya 3 kanallı yapıya göre güncellenmelidir.
    // Şimdilik boş bırakıyoruz, çünkü Source seçimi dinamik olduğu için maskeleme çok karmaşıklaşır.
    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        // Gelişmiş maskeleme için Source Index'e göre valid action'ları kapatmak gerekir.
        // Şimdilik öğrenmesine izin verelim (Geçersiz hamleye ceza veriyoruz zaten).
    }
}