using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
using System.Linq;
using System;
using System.Collections.Generic;

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
    public int _overrideActionType = 0;
    private int _overrideSourceIndex = 0;
    private int _overrideTargetIndex = 0;

    private const float LOG_ANCHOR_WOOD = 20000f;
    private const float LOG_ANCHOR_STONE_MEAT = 20000f;
    private const float LOG_ANCHOR_POPULATION = 100f;
    private const float LOG_ANCHOR_UNIT_COUNT = 100f;


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
    private const int ACT_SMART_COMMAND = 10;

    public enum AgentState
    {
        SelectUnit = 0,   // Adım 1: KİM?
        SelectAction = 1, // Adım 2: NE?
        SelectTarget = 2  // Adım 3: NEREYE?
    }
    private AgentState _currentState = AgentState.SelectUnit;

    private int _selectedSourceIndex = -1; // 1. Adımda seçilen birim
    private int _selectedActionType = 0;   // 2. Adımda seçilen emir

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
        UnbindEvents();
        if (_unitSystem != null)
        {
            _unitSystem.OnUnitAttackedUnit += HandleUnitAttackedUnit;
            _unitSystem.OnUnitAttackedBuilding += HandleUnitAttackedBuilding;
            _unitSystem.OnUnitKilledEnemy += HandleUnitKilledEnemy;
            _unitSystem.OnUnitDestroyedBuilding += HandleUnitDestroyedBuilding;


        }
        SimResourceSystem.OnResourceGathered += HandleResourceGathered;
        SimBuildingSystem.OnBuildingFinished += HandleBuildingCompleted;
        SimBuildingSystem.OnUnitCreated += HandleUnitCreated;
    }

    private void UnbindEvents()
    {
        if (_unitSystem != null)
        {
            _unitSystem.OnUnitAttackedUnit -= HandleUnitAttackedUnit;
            _unitSystem.OnUnitAttackedBuilding -= HandleUnitAttackedBuilding;
            _unitSystem.OnUnitKilledEnemy -= HandleUnitKilledEnemy;
            _unitSystem.OnUnitDestroyedBuilding -= HandleUnitDestroyedBuilding;
        }

        // Statik eventlerden çık
        SimResourceSystem.OnResourceGathered -= HandleResourceGathered;
        SimBuildingSystem.OnBuildingFinished -= HandleBuildingCompleted;
        SimBuildingSystem.OnUnitCreated -= HandleUnitCreated;
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
        // RequestDecision();
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
    private float LogScale(int value, float anchor)
    {
        return (float)Math.Log(1f + value) / (float)Math.Log(1f + anchor);
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (_world == null || !_world.Players.ContainsKey(1))
        {
            for (int i = 0; i < 14; i++) sensor.AddObservation(0f); // Boyut 14'e çıkacak
            return;
        }

        if (_world.Players.ContainsKey(1))
        {
            var me = _world.Players[1];

            // 1. KAYNAKLAR
            sensor.AddObservation(LogScale(me.Wood, LOG_ANCHOR_WOOD));
            sensor.AddObservation(LogScale(me.Stone, LOG_ANCHOR_STONE_MEAT));
            sensor.AddObservation(LogScale(me.Meat, LOG_ANCHOR_STONE_MEAT));
            sensor.AddObservation(LogScale(me.CurrentPopulation, LOG_ANCHOR_POPULATION));

            // 2. STATE BİLGİSİ (One-Hot)
            sensor.AddObservation(_currentState == AgentState.SelectUnit ? 1f : 0f);
            sensor.AddObservation(_currentState == AgentState.SelectAction ? 1f : 0f);
            sensor.AddObservation(_currentState == AgentState.SelectTarget ? 1f : 0f);

            // 3. CONTEXT & HIGHLIGHT
            float contextObs = 0f;
            if (_currentState != AgentState.SelectUnit && _selectedSourceIndex != -1)
            {
                contextObs = _translator.GetEncodedTypeAt(_selectedSourceIndex);

                // *** YENİ EKLENEN KISIM: SENSÖRÜ GÜNCELLE ***
                if (_gridSensorComp != null)
                    _gridSensorComp.SetHighlight(_selectedSourceIndex);
            }
            else
            {
                // Seçim yoksa highlight'ı kaldır
                if (_gridSensorComp != null)
                    _gridSensorComp.SetHighlight(-1);
            }
            sensor.AddObservation(contextObs);

            // 4. BİRİM SAYILARI
            int soldierCount = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Soldier && u.State != SimTaskType.Dead);
            int workerCount = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Worker && u.State != SimTaskType.Dead);

            sensor.AddObservation(LogScale(soldierCount, LOG_ANCHOR_UNIT_COUNT));
            sensor.AddObservation(LogScale(workerCount, LOG_ANCHOR_UNIT_COUNT));
        }
    }

    // --- EYLEM UYGULAMA (Action + Source + Target) ---
    public override void OnActionReceived(ActionBuffers actions)
    {
        // Tek dal okuyoruz (Branch 0)
        int decision = actions.DiscreteActions[0];
        // --- İŞTE BU SATIR EKSİK OLABİLİR, BUNU EKLE ---
        if (ShowDebugLogs)
        {
            Debug.Log($"[AGENT] STATE: {_currentState} | KARAR: {decision}");
        }
        switch (_currentState)
        {
            case AgentState.SelectUnit:
                _selectedSourceIndex = decision;
                _currentState = AgentState.SelectAction; // Bir sonraki adıma geç
                RequestDecision(); // Vakit kaybetmeden yeni karar iste
                break;

            case AgentState.SelectAction:
                _selectedActionType = decision;
                _currentState = AgentState.SelectTarget; // Bir sonraki adıma geç
                RequestDecision(); // Vakit kaybetmeden yeni karar iste
                break;

            case AgentState.SelectTarget:
                int targetIndex = decision;

                // --- FİNAL: EMRİ UYGULA ---
                // Artık elimizde Source, Action ve Target var.
                bool success = _translator.ExecuteAction(_selectedActionType, _selectedSourceIndex, targetIndex);

                if (!success) AddReward(-0.01f); // Hata cezası (Maskelemeye rağmen olursa)

                // --- RESET ---
                // Zincir bitti, başa dön.
                _selectedSourceIndex = -1;
                _selectedActionType = 0;
                _currentState = AgentState.SelectUnit;
                break;
        }
    }

    // --- HEURISTIC (ÖĞRETMEN MODU) ---
    // Artık klavye okumuyor, RegisterExternalAction ile gelen veriyi kullanıyor.
    // RTSAgent.cs içine yapıştır (Mevcut Heuristic fonksiyonunu silip bunu koy)

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discreteActions = actionsOut.DiscreteActions;

        // Eğer State 0 (Birim Seçimi) ise, haritada geçerli bir birim bulmaya çalış
        if (_currentState == AgentState.SelectUnit)
        {
            // Birimlerimi tara
            foreach (var unit in _world.Units.Values)
            {
                if (unit.PlayerID == 1) // Benim birimim mi?
                {
                    // Koordinatı Index'e çevir: y * width + x
                    int idx = unit.GridPosition.y * _world.Map.Width + unit.GridPosition.x;
                    discreteActions[0] = idx;
                    return; // Bulduk, çık.
                }
            }
            // Birim yoksa binalara bak
            foreach (var b in _world.Buildings.Values)
            {
                if (b.PlayerID == 1)
                {
                    int idx = b.GridPosition.y * _world.Map.Width + b.GridPosition.x;
                    discreteActions[0] = idx;
                    return;
                }
            }

            // Hiçbir şey yoksa mecburen 0 (Ama muhtemelen maskelenir)
            discreteActions[0] = 0;
        }
        else
        {
            // State 1 (Action) veya State 2 (Target) ise
            // Şimdilik "Wait" (0) komutu gönderiyoruz. 
            // Wait komutu risksizdir, logların akmasını sağlar.
            discreteActions[0] = 0;
        }
    }

    // Maskeleme şimdilik kapalı kalabilir veya 3 kanallı yapıya göre güncellenmelidir.
    // Şimdilik boş bırakıyoruz, çünkü Source seçimi dinamik olduğu için maskeleme çok karmaşıklaşır.
    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        int totalMapSize = _world.Map.Width * _world.Map.Height; // Örn: 256

        // NOT: Unity'de "Discrete Branch 0 Size" değerini harita boyutu (örn: 256) yapmalısınız.
        // Tek bir output dalımız var artık.

        switch (_currentState)
        {
            case AgentState.SelectUnit:
                // Sadece BENİM BİRİMLERİMİN olduğu kareler seçilebilir.
                // Diğer tüm kareleri (boş, düşman, engel) maskele.
                for (int i = 0; i < totalMapSize; i++)
                {
                    bool isMyUnit = _translator.IsUnitOwnedByPlayer(i, 1); // Bu fonksiyonu Translator'da tanımlamalıyız
                    if (!isMyUnit) actionMask.SetActionEnabled(0, i, false);
                }
                break;

            case AgentState.SelectAction:
                // Output [0..10] arası aksiyon tipleridir. [11..255] arasını kapatmalıyız.
                // Ayrıca seçilen birimin yapamayacağı aksiyonları da kapatmalıyız.

                // Önce tüm harita indexlerini kapat (çünkü biz action ID arıyoruz)
                for (int i = 11; i < totalMapSize; i++) actionMask.SetActionEnabled(0, i, false);

                // Seçilen birime özel kurallar
                SimUnitType selectedUnitType = _translator.GetUnitTypeAt(_selectedSourceIndex);

                if (selectedUnitType == SimUnitType.Worker)
                {
                    // İşçi: Saldırı yapamaz (örneğin), ama bina yapar.
                    // actionMask.SetActionEnabled(0, ACT_ATTACK, false);
                }
                else if (selectedUnitType == SimUnitType.Soldier)
                {
                    // Asker: Bina yapamaz.
                    actionMask.SetActionEnabled(0, ACT_BUILD_HOUSE, false);
                    actionMask.SetActionEnabled(0, ACT_BUILD_BARRACKS, false);
                    // ...
                }
                break;

            case AgentState.SelectTarget:
                // Seçilen Action'a göre haritadaki uygunsuz yerleri kapat.
                // Örn: ACT_BUILD_HOUSE seçildiyse, DOLU kareleri kapat.
                for (int i = 0; i < totalMapSize; i++)
                {
                    bool isValidTarget = _translator.IsTargetValidForAction(_selectedActionType, i);
                    if (!isValidTarget) actionMask.SetActionEnabled(0, i, false);
                }
                break;
        }
    }

    public void HandleResourceGathered(int playerID, int amount) // Bunu ResourceSystem'den çağırtın
    {
        if (playerID == 1)
        {
            // 10 birim odun için 0.01 ödül (Küçük ama sürekli)
            AddReward(amount * 0.001f);
        }
    }

    // 2. ÜRETİM ÖDÜLÜ
    // Kaynağı harcamaya teşvik eder.
    private void HandleUnitCreated(SimUnitData unit)
    {
        if (unit.PlayerID == 1)
        {
            if (unit.UnitType == SimUnitType.Worker)
                AddReward(0.2f); // İşçi basmak iyidir (Ekonomiyi büyütür)
            else if (unit.UnitType == SimUnitType.Soldier)
                AddReward(0.5f); // Asker basmak daha iyidir (Güvenlik)
        }
    }

    // 3. İNŞAAT ÖDÜLÜ
    // Nüfus ve Teknoloji gelişimi için.
    private void HandleBuildingCompleted(SimBuildingData building)
    {
        if (building.PlayerID == 1)
        {
            switch (building.Type)
            {
                case SimBuildingType.House:
                    AddReward(0.2f); // Nüfus artışı
                    break;
                case SimBuildingType.Barracks:
                    AddReward(1.0f); // Teknoloji (Kritik adım)
                    break;
                case SimBuildingType.Tower:
                    AddReward(0.5f); // Savunma
                    break;
                case SimBuildingType.Farm:
                case SimBuildingType.WoodCutter:
                case SimBuildingType.StonePit:
                    AddReward(0.3f); // Ekonomi
                    break;
            }
        }
    }
}