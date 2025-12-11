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
        if (_world == null)
        {
            for (int i = 0; i < 6; i++) sensor.AddObservation(0f);
            return;
        }

        if (_world.Players.ContainsKey(1))
        {
            var me = _world.Players[1];

            // KAYNAK GÖZLEMLERİ: Logaritmik Ölçekleme ile sınırlandırılmamış normalizasyon
            sensor.AddObservation(LogScale(me.Wood, LOG_ANCHOR_WOOD));
            sensor.AddObservation(LogScale(me.Stone, LOG_ANCHOR_STONE_MEAT));
            sensor.AddObservation(LogScale(me.Meat, LOG_ANCHOR_STONE_MEAT));

            // Nüfus Gözlemi (Aynı logaritmik ölçekleme kullanılabilir)
            sensor.AddObservation(LogScale(me.CurrentPopulation, LOG_ANCHOR_POPULATION));

            // Birim Sayısı Gözlemleri
            int soldierCount = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Soldier && u.State != SimTaskType.Dead);
            int workerCount = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Worker && u.State != SimTaskType.Dead);

            sensor.AddObservation(LogScale(soldierCount, LOG_ANCHOR_UNIT_COUNT));
            sensor.AddObservation(LogScale(workerCount, LOG_ANCHOR_UNIT_COUNT));
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
        // if (!isSuccess && actionType != 0) AddReward(-0.001f); 
        // Geçersiz hamle
        AddReward(-0.001f); // Zaman cezası
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
        // 1. Dünya ve Oyuncu Kontrolü
        if (_world == null || !_world.Players.ContainsKey(1)) return;

        var me = _world.Players[1];

        // 2. Mevcut Varlık Sayılarını Hesapla (Anlık Durum)
        // NOT: Performans için bu sayılar SimWorldState içinde tutulabilir, şimdilik LINQ ile sayıyoruz.
        int workerCount = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Worker && u.State != SimTaskType.Dead);
        int soldierCount = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Soldier && u.State != SimTaskType.Dead);

        // Base ve Kışla için sadece inşaatı bitmiş (IsConstructed) olanları saymak daha güvenlidir
        int baseCount = _world.Buildings.Values.Count(b => b.PlayerID == 1 && b.Type == SimBuildingType.Base && b.IsConstructed && b.Health > 0);
        int barracksCount = _world.Buildings.Values.Count(b => b.PlayerID == 1 && b.Type == SimBuildingType.Barracks && b.IsConstructed && b.Health > 0);

        bool hasWorker = workerCount > 0;
        bool hasAnyUnit = (workerCount + soldierCount) > 0;

        // --- BRANCH 0: ACTION TYPE MASKELEME ---

        // 0. ACT_WAIT (Her zaman mümkün)
        // actionMask.SetActionEnabled(0, ACT_WAIT, true); 

        // 1. İNŞAAT EYLEMLERİ (İşçi Gerekir + Kaynak Gerekir)
        if (!hasWorker)
        {
            // İşçi yoksa hiçbir bina yapılamaz
            actionMask.SetActionEnabled(0, ACT_BUILD_HOUSE, false);
            actionMask.SetActionEnabled(0, ACT_BUILD_BARRACKS, false);
            actionMask.SetActionEnabled(0, ACT_BUILD_WOODCUTTER, false);
            actionMask.SetActionEnabled(0, ACT_BUILD_STONEPIT, false);
            actionMask.SetActionEnabled(0, ACT_BUILD_FARM, false);
            actionMask.SetActionEnabled(0, ACT_BUILD_TOWER, false);
            actionMask.SetActionEnabled(0, ACT_BUILD_WALL, false);
        }
        else
        {
            // İşçi var, maliyet kontrolü yap

            // House (Ev)
            if (me.Wood < SimConfig.HOUSE_COST_WOOD || me.Stone < SimConfig.HOUSE_COST_STONE || me.Meat < SimConfig.HOUSE_COST_MEAT)
                actionMask.SetActionEnabled(0, ACT_BUILD_HOUSE, false);

            // Barracks (Kışla)
            if (me.Wood < SimConfig.BARRACKS_COST_WOOD || me.Stone < SimConfig.BARRACKS_COST_STONE || me.Meat < SimConfig.BARRACKS_COST_MEAT)
                actionMask.SetActionEnabled(0, ACT_BUILD_BARRACKS, false);

            // WoodCutter (Oduncu)
            if (me.Wood < SimConfig.WOODCUTTER_COST_WOOD || me.Stone < SimConfig.WOODCUTTER_COST_STONE || me.Meat < SimConfig.WOODCUTTER_COST_MEAT)
                actionMask.SetActionEnabled(0, ACT_BUILD_WOODCUTTER, false);

            // StonePit (Taş Ocağı)
            if (me.Wood < SimConfig.STONEPIT_COST_WOOD || me.Stone < SimConfig.STONEPIT_COST_STONE || me.Meat < SimConfig.STONEPIT_COST_MEAT)
                actionMask.SetActionEnabled(0, ACT_BUILD_STONEPIT, false);

            // Farm (Çiftlik)
            if (me.Wood < SimConfig.FARM_COST_WOOD || me.Stone < SimConfig.FARM_COST_STONE || me.Meat < SimConfig.FARM_COST_MEAT)
                actionMask.SetActionEnabled(0, ACT_BUILD_FARM, false);

            // Tower (Kule)
            if (me.Wood < SimConfig.TOWER_COST_WOOD || me.Stone < SimConfig.TOWER_COST_STONE || me.Meat < SimConfig.TOWER_COST_MEAT)
                actionMask.SetActionEnabled(0, ACT_BUILD_TOWER, false);

            // Wall (Duvar)
            if (me.Wood < SimConfig.WALL_COST_WOOD || me.Stone < SimConfig.WALL_COST_STONE || me.Meat < SimConfig.WALL_COST_MEAT)
                actionMask.SetActionEnabled(0, ACT_BUILD_WALL, false);
        }

        // 2. ÜRETİM EYLEMLERİ (Bina Gerekir + Kaynak Gerekir + Bina Müsaitliği)
        // Not: Burada "Hangi binanın boş olduğu" Source index seçiminde önemlidir. 
        // Ancak genel Action Type maskelemesi için "En az bir müsait bina var mı?" bakabiliriz.
        // Basitlik adına sadece bina varlığına ve kaynağa bakıyoruz.

        // Train Worker (İşçi Bas) -> Base Gerekir
        if (baseCount == 0 ||
            me.Wood < SimConfig.WORKER_COST_WOOD ||
            me.Stone < SimConfig.WORKER_COST_STONE ||
            me.Meat < SimConfig.WORKER_COST_MEAT)
        {
            actionMask.SetActionEnabled(0, ACT_TRAIN_WORKER, false);
        }

        // Train Soldier (Asker Bas) -> Kışla Gerekir
        if (barracksCount == 0 ||
            me.Wood < SimConfig.SOLDIER_COST_WOOD ||
            me.Stone < SimConfig.SOLDIER_COST_STONE ||
            me.Meat < SimConfig.SOLDIER_COST_MEAT)
        {
            actionMask.SetActionEnabled(0, ACT_TRAIN_SOLDIER, false);
        }

        // 3. KOMUT EYLEMLERİ (Birim Gerekir)
        // Smart Command (Yürü/Saldır/Topla) -> En az 1 birimim olmalı
        if (!hasAnyUnit)
        {
            actionMask.SetActionEnabled(0, ACT_SMART_COMMAND, false);
        }

        // --- BRANCH 1: SOURCE INDEX MASKING (HIZLI & DİNAMİK VERSİYON) ---

        // 1. Harita bilgilerini DOĞRUDAN _world üzerinden alıyoruz (SimConfig yerine)
        // Çünkü SimWorldState zaten Map verisini tutuyor.
        int mapWidth = _world.Map.Width;
        int totalCells = _world.Map.Width * _world.Map.Height;

        // 2. Geçerli olan (Bana ait) indexleri bir listede topla
        HashSet<int> validSourceIndices = new HashSet<int>();

        // A) Benim Ünitelerimi (Units Dictionary'sinden) bul
        foreach (var unit in _world.Units.Values)
        {
            // Sadece bana ait (PlayerID == 1) ve ölü olmayanları seç
            if (unit.PlayerID == 1 && unit.State != SimTaskType.Dead)
            {
                // Koordinatı (x, y) tekil Index'e çevir: y * width + x
                int index = unit.GridPosition.y * mapWidth + unit.GridPosition.x;
                validSourceIndices.Add(index);
            }
        }

        // B) Benim Binalarımı (Buildings Dictionary'sinden) bul
        foreach (var building in _world.Buildings.Values)
        {
            // Sadece bana ait ve yıkılmamış binaları seç
            if (building.PlayerID == 1 && building.Health > 0)
            {
                int index = building.GridPosition.y * mapWidth + building.GridPosition.x;
                validSourceIndices.Add(index);
            }
        }

        // 3. Tüm haritayı gez ve listemizde OLMAYAN her şeyi kapat
        // ML-Agents "Source Index" olarak haritadaki herhangi bir kareyi seçebilir.
        // Biz sadece bizim adamlarımızın olduğu kareleri seçmesine izin veriyoruz.
        for (int i = 0; i < totalCells; i++)
        {
            // Eğer bu 'i' indexi benim geçerli listemde yoksa, o kareye tıklamayı yasakla.
            if (!validSourceIndices.Contains(i))
            {
                actionMask.SetActionEnabled(1, i, false);
            }
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