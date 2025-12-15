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

    public SelfPlayTrainerRunner SelfPlayRunner;

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
    // YENİ AYRIŞTIRILMIŞ KOMUTLAR
    private const int ACT_ATTACK_ENEMY = 10; // Sadece Düşmana Tıklar
    private const int ACT_MOVE_TO = 11;      // Sadece Boş Yere Tıklar
    private const int ACT_GATHER_RES = 12;   // Sadece Kaynağa Tıklar
    private float _collectedWoodReward = 0f;
    private float _collectedStoneReward = 0f;
    private float _collectedMeatReward = 0f;
    private const float MAX_RESOURCE_REWARD = 2.0f;

    private bool _hasPendingDemoInput = false;

    private Queue<int> _actionHistory = new Queue<int>();

    public int MyPlayerID = 1;

    // --- KAYNAK LİMİT DEĞİŞKENLERİ ---
    private int _gatheredWood = 0;
    private int _gatheredStone = 0;
    private int _gatheredMeat = 0;

    // Her kaynak türünden en fazla kaç tane toplanırsa ödül verilsin?
    private const int MAX_RESOURCE_GATHER_LIMIT = 1000;
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
        UnbindEvents();

        _world = world;
        _gridSystem = gridSys;
        _unitSystem = unitSys;
        _buildingSystem = buildSys;

        if (_gridSensorComp == null)
            _gridSensorComp = GetComponent<RTSGridSensorComponent>();

        if (_gridSensorComp != null)
        {
            _gridSensorComp.InitializeSensor(_world, _gridSystem);
        }

        // --- HATA DÜZELTME: MyPlayerID EKLENDİ ---
        _translator = new DRLActionTranslator(_world, _unitSystem, _buildingSystem, _gridSystem, MyPlayerID);
        // ------------------------------------------

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
        if (attacker.PlayerID == MyPlayerID) AddReward(damage * 0.002f);
    }

    private void HandleUnitAttackedBuilding(SimUnitData attacker, SimBuildingData building, float damage)
    {
        if (attacker.PlayerID == MyPlayerID)
        {
            float multiplier = (building.Type == SimBuildingType.Base) ? 0.05f : 0.002f;
            AddReward(damage * multiplier);
        }
    }

    private void HandleUnitKilledEnemy(SimUnitData attacker, SimUnitData victim)
    {
        if (attacker.PlayerID == 1) AddReward(1.0f);
    }

    private void HandleUnitDestroyedBuilding(SimUnitData attacker, SimBuildingData building)
    {
        if (attacker.PlayerID == MyPlayerID)
        {
            if (building.Type == SimBuildingType.Base)
            {
                AddReward(100.0f);
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

        _hasPendingDemoInput = true; // Yeni demo girdisi var!

        // Ajanı uyandırıp karar vermesini (Heuristic'i tetiklemesini) sağlıyoruz.
        RequestDecision();
    }

    public override void OnEpisodeBegin()
    {
        // --- GÜNCELLENMİŞ HALİ (SelfPlay Desteği) ---
        if (SelfPlayRunner != null)
        {
            SelfPlayRunner.ResetSimulation();
        }
        else if (Runner != null)
        {
            Runner.ResetSimulation();
        }
        else if (CombatRunner != null)
        {
            CombatRunner.ResetSimulation();
        }

        // Buffer'ı temizle
        _overrideActionType = 0;
        _overrideSourceIndex = 0;
        _overrideTargetIndex = 0;
        _collectedWoodReward = 0f;
        _collectedStoneReward = 0f;
        _collectedMeatReward = 0f;
        _gatheredWood = 0;
        _gatheredStone = 0;
        _gatheredMeat = 0;
    }
    private float LogScale(int value, float anchor)
    {
        return (float)Math.Log(1f + value) / (float)Math.Log(1f + anchor);
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (_world == null || !_world.Players.ContainsKey(MyPlayerID))
        {
            for (int i = 0; i < 10; i++) sensor.AddObservation(0f); // Boyut 14'e çıkacak
            return;
        }

        if (_world.Players.ContainsKey(MyPlayerID))
        {
            var me = _world.Players[MyPlayerID];

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

            // 4. BİRİM SAYILARI (DÜZELTİLDİ)
            int soldierCount = _world.Units.Values.Count(u => u.PlayerID == MyPlayerID && u.UnitType == SimUnitType.Soldier && u.State != SimTaskType.Dead);
            int workerCount = _world.Units.Values.Count(u => u.PlayerID == MyPlayerID && u.UnitType == SimUnitType.Worker && u.State != SimTaskType.Dead);

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

                // --- BURAYI EKLE: İşlem geçmişini kaydet ---
                if (_actionHistory.Count >= 4) _actionHistory.Dequeue();
                _actionHistory.Enqueue(_selectedActionType);
                // ------------------------------------------

                // --- YENİ EKLEME: TOPLAMA CEZASI ---
                // Seçilen üniteyi bul
                var unit = _translator.GetUnitAtPosIndex(_selectedSourceIndex);

                // Eğer ünite şu an kaynak topluyorsa (Gathering)
                if (unit != null && unit.State == SimTaskType.Gathering)
                {
                    // Ve verilen emir "Bekle" değilse (veya aynı kaynağa tekrar tıklamak değilse)
                    // Burada basitçe: Eğer yeni bir emir geldiyse ve bu emir işçiyi bozuyorsa ceza ver.
                    // Not: ActionType 12 (Gather) olsa bile, sürekli emir verip animasyonu resetlemek kötü olabilir.

                    // Ciddi bir ceza ver ki gereksiz yere işçileri dürtmesin.
                    if (_selectedActionType != ACT_WAIT)
                    {
                        AddReward(-0.5f); // "Rahat bırak şu işçiyi" cezası
                        if (ShowDebugLogs) Debug.Log("Toplayan işçi bölündü! Ceza verildi.");
                    }
                }
                // -----------------------------------
                // --- FİNAL: EMRİ UYGULA ---
                // Artık elimizde Source, Action ve Target var.
                bool success = _translator.ExecuteAction(_selectedActionType, _selectedSourceIndex, targetIndex);

                if (!success)
                {
                    if (ShowDebugLogs) Debug.Log("maskelemeye rağmen hatalı işlem");
                    AddReward(-0.01f); // Hata cezası (Maskelemeye rağmen olursa)
                }

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

        // Eğer bekleyen bir demo girdisi yoksa "Bekle" (0) gönderip çıkıyoruz.
        if (!_hasPendingDemoInput)
        {
            discreteActions[0] = 0;
            return;
        }

        // Mevcut durumumuza (State) göre, sakladığımız inputun hangi parçasını vereceğimizi seçiyoruz.
        switch (_currentState)
        {
            case AgentState.SelectUnit: // Adım 1: Kaynak (Source) Seçimi
                discreteActions[0] = _overrideSourceIndex;
                break;

            case AgentState.SelectAction: // Adım 2: Eylem (Action) Seçimi
                discreteActions[0] = _overrideActionType;
                break;

            case AgentState.SelectTarget: // Adım 3: Hedef (Target) Seçimi
                discreteActions[0] = _overrideTargetIndex;

                // Zincir tamamlanacağı için bayrağı indiriyoruz. 
                // (Not: OnActionReceived çalıştıktan sonra state başa dönecek)
                _hasPendingDemoInput = false;
                break;
        }
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        if (_world == null || _world.Map == null || !_world.Players.ContainsKey(MyPlayerID)) return;

        int totalMapSize = _world.Map.Width * _world.Map.Height;
        var me = _world.Players[MyPlayerID];

        switch (_currentState)
        {
            case AgentState.SelectUnit:
                for (int i = 0; i < totalMapSize; i++)
                {
                    bool canSelect = false;

                    // 1. Birim benim mi?
                    if (_translator.IsUnitOwnedByPlayer(i, MyPlayerID))
                    {
                        var unit = _translator.GetUnitAtPosIndex(i);

                        // --- KRİTİK DÜZELTME: MEŞGUL İŞÇİYİ SEÇME ---
                        // Eğer birim inşaat yapıyorsa (Building), onu seçime kapat (Maskele).
                        // Böylece ajan "Build" emrini yarıda kesemez.
                        if (unit != null)
                        {
                            if (unit.State != SimTaskType.Idle)
                            {
                                canSelect = false;
                            }
                            else
                            {
                                canSelect = true;
                            }
                        }
                        else
                        {
                            // Bina seçimi (Kışla vb. üretim için her zaman seçilebilir)
                            canSelect = true;
                        }
                    }

                    actionMask.SetActionEnabled(0, i, canSelect);
                }
                break;

            // ----------------------------------------------------------------
            // ADIM 2: EYLEM SEÇİMİ (WHAT?) - (3 KANALLI AYRIŞTIRMA + KAYNAK KONTROLÜ)
            // ----------------------------------------------------------------
            case AgentState.SelectAction:

                // 1. Önce eylem ID'si olmayan tüm harita indekslerini kapat (13'ten sonrasını)
                // (Wait=0 ... Gather=12 => Toplam 13 eylem)
                for (int i = 13; i < totalMapSize; i++) actionMask.SetActionEnabled(0, i, false);

                SimUnitType unitType = _translator.GetUnitTypeAt(_selectedSourceIndex);
                SimBuildingType buildingType = _translator.GetBuildingTypeAt(_selectedSourceIndex);

                // Yardımcı Fonksiyon: Kaynak Yetiyor mu?
                bool CanAfford(int w, int s, int m) => me.Wood >= w && me.Stone >= s && me.Meat >= m;

                // --- İŞÇİ (WORKER) ---
                if (unitType == SimUnitType.Worker)
                {
                    // Üretim yapamaz (Bina işidir)
                    actionMask.SetActionEnabled(0, ACT_TRAIN_WORKER, false);
                    actionMask.SetActionEnabled(0, ACT_TRAIN_SOLDIER, false);

                    // Temel Aksiyonlar: HEPSİ AÇIK
                    actionMask.SetActionEnabled(0, ACT_ATTACK_ENEMY, false); // 10: Saldır
                    actionMask.SetActionEnabled(0, ACT_MOVE_TO, true);      // 11: Yürü
                    actionMask.SetActionEnabled(0, ACT_GATHER_RES, true);   // 12: Topla

                    // İnşaat: KAYNAK VARSA AÇIK
                    actionMask.SetActionEnabled(0, ACT_BUILD_HOUSE, CanAfford(SimConfig.HOUSE_COST_WOOD, SimConfig.HOUSE_COST_STONE, SimConfig.HOUSE_COST_MEAT));
                    actionMask.SetActionEnabled(0, ACT_BUILD_BARRACKS, CanAfford(SimConfig.BARRACKS_COST_WOOD, SimConfig.BARRACKS_COST_STONE, SimConfig.BARRACKS_COST_MEAT));
                    actionMask.SetActionEnabled(0, ACT_BUILD_WOODCUTTER, CanAfford(SimConfig.WOODCUTTER_COST_WOOD, SimConfig.WOODCUTTER_COST_STONE, SimConfig.WOODCUTTER_COST_MEAT));
                    actionMask.SetActionEnabled(0, ACT_BUILD_STONEPIT, CanAfford(SimConfig.STONEPIT_COST_WOOD, SimConfig.STONEPIT_COST_STONE, SimConfig.STONEPIT_COST_MEAT));
                    actionMask.SetActionEnabled(0, ACT_BUILD_FARM, CanAfford(SimConfig.FARM_COST_WOOD, SimConfig.FARM_COST_STONE, SimConfig.FARM_COST_MEAT));
                    actionMask.SetActionEnabled(0, ACT_BUILD_TOWER, CanAfford(SimConfig.TOWER_COST_WOOD, SimConfig.TOWER_COST_STONE, SimConfig.TOWER_COST_MEAT));
                    actionMask.SetActionEnabled(0, ACT_BUILD_WALL, CanAfford(SimConfig.WALL_COST_WOOD, SimConfig.WALL_COST_STONE, SimConfig.WALL_COST_MEAT));
                }
                // --- ASKER (SOLDIER) ---
                else if (unitType == SimUnitType.Soldier)
                {
                    // İnşaat Yapamaz (1-9 Kapat)
                    for (int k = 1; k <= 9; k++) actionMask.SetActionEnabled(0, k, false);

                    // Üretim Yapamaz
                    actionMask.SetActionEnabled(0, ACT_TRAIN_WORKER, false);
                    actionMask.SetActionEnabled(0, ACT_TRAIN_SOLDIER, false);

                    // Kaynak Toplayamaz (ÖNEMLİ)
                    actionMask.SetActionEnabled(0, ACT_GATHER_RES, false); // 12 Kapalı

                    // Sadece Saldır ve Yürü
                    actionMask.SetActionEnabled(0, ACT_ATTACK_ENEMY, true); // 10
                    actionMask.SetActionEnabled(0, ACT_MOVE_TO, true);      // 11
                }
                // --- BİNA (BUILDING) ---
                else if (buildingType != SimBuildingType.None)
                {
                    // Hareket, Saldırı, Toplama, İnşaat -> HEPSİ KAPALI
                    actionMask.SetActionEnabled(0, ACT_ATTACK_ENEMY, false);
                    actionMask.SetActionEnabled(0, ACT_MOVE_TO, false);
                    actionMask.SetActionEnabled(0, ACT_GATHER_RES, false);
                    for (int k = 1; k <= 9; k++) actionMask.SetActionEnabled(0, k, false);

                    // Üretim: TÜRÜNE GÖRE ve KAYNAK VARSA AÇIK
                    if (buildingType == SimBuildingType.Base)
                    {
                        bool canTrain = CanAfford(SimConfig.WORKER_COST_WOOD, SimConfig.WORKER_COST_STONE, SimConfig.WORKER_COST_MEAT);
                        actionMask.SetActionEnabled(0, ACT_TRAIN_WORKER, canTrain);
                        actionMask.SetActionEnabled(0, ACT_TRAIN_SOLDIER, false);
                    }
                    else if (buildingType == SimBuildingType.Barracks)
                    {
                        bool canTrain = CanAfford(SimConfig.SOLDIER_COST_WOOD, SimConfig.SOLDIER_COST_STONE, SimConfig.SOLDIER_COST_MEAT);
                        actionMask.SetActionEnabled(0, ACT_TRAIN_SOLDIER, canTrain);
                        actionMask.SetActionEnabled(0, ACT_TRAIN_WORKER, false);
                    }
                    else
                    {
                        // Diğer binalar üretim yapamaz (Ev, Kule vb.)
                        actionMask.SetActionEnabled(0, ACT_TRAIN_WORKER, false);
                        actionMask.SetActionEnabled(0, ACT_TRAIN_SOLDIER, false);
                    }
                }
                else
                {
                    // Seçim hatalıysa veya boşsa her şeyi kapat (Sadece Bekle açık kalabilir)
                    for (int i = 1; i <= 12; i++) actionMask.SetActionEnabled(0, i, false);
                }
                break;

            // ----------------------------------------------------------------
            // ADIM 3: HEDEF SEÇİMİ (WHERE?)
            // ----------------------------------------------------------------
            case AgentState.SelectTarget:
                bool anyTargetAvailable = false; // EMNİYET KONTROLÜ

                for (int i = 0; i < totalMapSize; i++)
                {
                    bool isValid = _translator.IsTargetValidForAction(_selectedActionType, i);
                    if (isValid) anyTargetAvailable = true;
                    actionMask.SetActionEnabled(0, i, isValid);
                }

                // --- HATA DÜZELTME ---
                if (!anyTargetAvailable)
                {
                    actionMask.SetActionEnabled(0, 0, true);
                }
                // ---------------------
                break;
        }
    }
    public void HandleResourceGathered(int playerID, int amount, SimResourceType type)
    {
        if (playerID != MyPlayerID) return;

        float baseRewardFactor = 0.001f; // Çarpanı biraz düşürebilirsin (0.0005f gibi) eğer çok şişerse.
        float reward = 0f;

        switch (type)
        {
            case SimResourceType.Wood:
                reward = amount * baseRewardFactor;
                break;
            case SimResourceType.Stone:
                reward = amount * baseRewardFactor;
                break;
            case SimResourceType.Meat:
                reward = amount * (baseRewardFactor * 1.2f);
                break;
        }

        if (reward > 0) AddReward(reward);
    }


    private void HandleUnitCreated(SimUnitData unit)
    {
        if (unit.PlayerID == MyPlayerID) // DÜZELTİLDİ
        {
            if (unit.UnitType == SimUnitType.Worker)
                AddReward(0.2f); // İşçi basmak iyidir (Ekonomiyi büyütür)
            else if (unit.UnitType == SimUnitType.Soldier)
                AddReward(1f); // Asker basmak daha iyidir (Güvenlik)
        }
    }

    // 3. İNŞAAT ÖDÜLÜ
    // Nüfus ve Teknoloji gelişimi için.
    // RTSAgent.cs içinde HandleBuildingCompleted fonksiyonunu bu şekilde güncelle:

    private void HandleBuildingCompleted(SimBuildingData building)
    {
        if (building.PlayerID != MyPlayerID) return;

        // Count EXISTING constructed buildings of this type
        int buildingCount = _world.Buildings.Values.Count(b =>
            b.PlayerID == MyPlayerID &&
            b.Type == building.Type &&
            b.IsConstructed
        );

        // SAFETY FIX: If the current building hasn't been flagged as Constructed yet 
        // in the list or is the first one, buildingCount might be 0.
        // We want the divisor to be at least 1.
        int divisor = Mathf.Max(1, buildingCount);

        float reward = GetCostBasedReward(building.Type);

        if (building.Type == SimBuildingType.Barracks && divisor == 1)
        {
            reward = 5.0f;
            if (ShowDebugLogs) Debug.Log(">>> FIRST BARRACKS! STRATEGIC BONUS! (+5.0) <<<");
        }
        else
        {
            // Diminishing Returns: Reward / Count
            reward = reward / (float)divisor;
        }

        if (reward > 0)
        {
            // Double check for safety before adding
            if (float.IsInfinity(reward) || float.IsNaN(reward))
            {
                Debug.LogError($"[RTSAgent] Invalid Reward Detected! Divisor: {divisor}, BaseReward: {GetCostBasedReward(building.Type)}");
                return;
            }

            AddReward(reward);
            if (ShowDebugLogs)
                Debug.Log($"[Reward] Building: {building.Type} | Count: {divisor} | Awarded: {reward:F4}");
        }
    }
    private float GetCostBasedReward(SimBuildingType type)
    {
        float totalCost = 0f;
        switch (type)
        {
            case SimBuildingType.House:
                totalCost = SimConfig.HOUSE_COST_WOOD + SimConfig.HOUSE_COST_STONE + SimConfig.HOUSE_COST_MEAT; break;
            case SimBuildingType.Farm:
                totalCost = SimConfig.FARM_COST_WOOD + SimConfig.FARM_COST_STONE + SimConfig.FARM_COST_MEAT; break;
            case SimBuildingType.WoodCutter:
                totalCost = SimConfig.WOODCUTTER_COST_WOOD + SimConfig.WOODCUTTER_COST_STONE + SimConfig.WOODCUTTER_COST_MEAT; break;
            case SimBuildingType.StonePit:
                totalCost = SimConfig.STONEPIT_COST_WOOD + SimConfig.STONEPIT_COST_STONE + SimConfig.STONEPIT_COST_MEAT; break;
            case SimBuildingType.Barracks:
                totalCost = SimConfig.BARRACKS_COST_WOOD + SimConfig.BARRACKS_COST_STONE + SimConfig.BARRACKS_COST_MEAT; break;
            case SimBuildingType.Tower:
                totalCost = SimConfig.TOWER_COST_WOOD + SimConfig.TOWER_COST_STONE + SimConfig.TOWER_COST_MEAT; break;
            case SimBuildingType.Wall:
                totalCost = SimConfig.WALL_COST_WOOD + SimConfig.WALL_COST_STONE + SimConfig.WALL_COST_MEAT; break;
        }
        return totalCost * 0.001f;
    }
    private void OnDrawGizmos()
    {
        // Ajanın seçtiği kaynak (Source) ve hedef (Target) belli mi?
        if (_world != null && _currentState == AgentState.SelectUnit) return; // Henüz seçim yok

        // Seçilen Kaynak Pozisyonu
        if (_selectedSourceIndex != -1)
        {
            Vector3 sourcePos = GetWorldPos(_selectedSourceIndex);
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(sourcePos, 0.5f); // Seçilen birimin etrafına yeşil halka

            // Eğer hedef de seçildiyse (Action aşamasından sonra)
            // Not: State machine hızlı aktığı için bunu yakalamak zor olabilir ama
            // RegisterExternalAction veya son kararı bir değişkende tutarsan çizebilirsin.
        }
    }

    // Yardımcı: Grid Index -> World Position
    private Vector3 GetWorldPos(int index)
    {
        int w = _world.Map.Width;
        int x = index % w;
        int y = index / w;
        // Senin grid sistemine göre offset eklemen gerekebilir
        return new Vector3(x, 0, y) + Vector3.one * 0.5f;
    }

    public bool IsIdleSpamming()
    {
        // Henüz 4 işlem birikmediyse spam sayılmaz, kayda devam.
        if (_actionHistory.Count < 4) return false;

        // Kuyruktaki tüm elemanlar 0 mı diye bak
        foreach (int action in _actionHistory)
        {
            if (action != 0) return false; // Arada 0 olmayan bir işlem varsa spam değildir.
        }
        return true; // Hepsi 0 ise spam yapıyor demektir.
    }
}