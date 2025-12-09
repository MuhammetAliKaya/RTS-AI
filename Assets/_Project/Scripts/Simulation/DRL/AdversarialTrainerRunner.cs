using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
using Unity.MLAgents;

public class AdversarialTrainerRunner : MonoBehaviour
{
    [Header("Ayarlar")]
    public RTSAgent Agent;
    public int MapSize = 20;
    public int MaxSteps = 5000;

    [Range(1f, 100f)]
    [Tooltip("Simülasyonun Çalışma Hızı. Unity Editor'de oyunu hızlandırmak için.")]
    public float SimulationTimeScale = 100.0f;

    // Simülasyonun iç mantık adım süresi (Saniyede 10 karar)
    // 20.0f ÇOK YÜKSEKTİ, 0.1f olarak düzeltildi.
    private float dt = 0.1f;

    [Header("Görselleştirme")]
    public GameVisualizer Visualizer;

    [Header("Rakip Ayarları")]
    public bool UseMacroAI = true;
    [Tooltip("Eğitim sırasında bu değer Curriculum (YAML) tarafından yönetilir.")]
    public AIDifficulty EnemyDifficulty = AIDifficulty.Passive;

    // SİSTEMLER
    private SimWorldState _world;
    private SimGridSystem _gridSys;
    private SimUnitSystem _unitSys;
    private SimBuildingSystem _buildSys;
    private SimResourceSystem _resSys;

    // TAKİP DEĞİŞKENLERİ (Ödüller için)
    private int _lastEnemyUnitCount = 0;
    private int _lastEnemyBuildingCount = 0;
    private float _lastEnemyBaseHealth = 1000f;

    // Ekonomi takibi (Sadece Passive modda ödül vermek için)
    private int _lastWood = 0;
    private int _lastMeat = 0;
    private int _lastStone = 0;
    private int _lastWorkerCount = 0;

    // RAKİP
    private SimpleMacroAI _enemyAI;
    private int _currentStep = 0;

    // Oyun bitti mi kontrolü
    private bool _gameEnded = false;

    void Start()
    {
        if (Agent == null) Agent = GetComponentInChildren<RTSAgent>();

        // Unity Zaman Ayarı
        Application.targetFrameRate = -1;
        Time.timeScale = SimulationTimeScale;

        ResetSimulation();
    }

    void Update()
    {
        // Bir karede 10 simülasyon adımı işlet (GPU/CPU izin verdiği sürece)
        for (int i = 0; i < 50; i++)
        {
            if (_world != null && !_gameEnded)
            {
                // dt burada sabit 0.1f kalmalı!
                SimulationStep();
            }
        }
    }

    public void SimulationStep()
    {
        // 1. Düşman AI Hamlesi
        if (_enemyAI != null)
        {
            _enemyAI.Update(dt);
        }

        // 2. Agent Karar İsteği
        if (Agent != null) Agent.RequestDecision();

        // 3. Simülasyonu İlerlet
        if (_buildSys != null) _buildSys.UpdateAllBuildings(dt);

        // Ödül Hesaplamaları
        CalculateCombatRewards();
        CalculateEconomyRewards(); // YENİ: Başlangıç seviyesi için ekonomi teşviki

        var unitIds = _world.Units.Keys.ToList();
        foreach (var uid in unitIds)
        {
            if (_world.Units.TryGetValue(uid, out SimUnitData unit))
                if (_unitSys != null) _unitSys.UpdateUnit(unit, dt);
        }

        // 4. Bitiş Kontrolü
        CheckGameResult();

        _currentStep++;
        if (_currentStep >= MaxSteps && !_gameEnded)
        {
            // Zaman doldu - Berabere
            // Pasif modda zamanın dolması kötüdür (saldırması lazım), Aggressive'de hayatta kalmak iyidir.
            // Şimdilik nötr bitirelim.
            EndGame(0);
        }
    }

    private void CalculateEconomyRewards()
    {
        if (Agent == null) return;

        // Ekonomi ödülleri SADECE PASSIVE modda (Eğitimin en başında) verilir.
        // Amaç ajana "Odun topla, işçi bas" mantığını öğretmektir.
        // İleri seviyelerde bu ödüller kapatılır ki ajan "savaşmak yerine zengin olmaya" çalışmasın.
        if (EnemyDifficulty != AIDifficulty.Passive) return;

        var myPlayer = _world.Players[1];

        // Kaynak Toplama Ödülü (Her 1 birim kaynak için çok ufak puan)
        int woodDelta = myPlayer.Wood - _lastWood;
        int meatDelta = myPlayer.Meat - _lastMeat;
        int stoneDelta = myPlayer.Stone - _lastStone;

        if (woodDelta > 0) Agent.AddReward(woodDelta * 0.001f);
        if (meatDelta > 0) Agent.AddReward(meatDelta * 0.001f);
        if (stoneDelta > 0) Agent.AddReward(stoneDelta * 0.001f);

        // İşçi Basma Ödülü (Ekonomiyi büyütmesi için teşvik)
        // Mevcut işçi sayısını say
        int currentWorkers = _world.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Worker);
        if (currentWorkers > _lastWorkerCount)
        {
            Agent.AddReward(0.05f); // Her yeni işçi için ufak bir "Aferin"
        }

        // Değerleri güncelle
        _lastWood = myPlayer.Wood;
        _lastMeat = myPlayer.Meat;
        _lastStone = myPlayer.Stone;
        _lastWorkerCount = currentWorkers;
    }

    private void CalculateCombatRewards()
    {
        if (Agent == null) return;

        int currentEnemyUnits = 0;
        int currentEnemyBuildings = 0;
        float currentEnemyBaseHealth = 0;

        foreach (var u in _world.Units.Values)
            if (u.PlayerID == 2 && u.State != SimTaskType.Dead) currentEnemyUnits++;

        foreach (var b in _world.Buildings.Values)
        {
            if (b.PlayerID == 2)
            {
                currentEnemyBuildings++;
                if (b.Type == SimBuildingType.Base) currentEnemyBaseHealth = b.Health;
            }
        }

        // 1. Düşman Öldürme (Aynı kaldı)
        if (currentEnemyUnits < _lastEnemyUnitCount)
        {
            int killCount = _lastEnemyUnitCount - currentEnemyUnits;
            Agent.AddReward(0.5f * killCount);
        }

        // 2. Bina Yıkma (GÜÇLENDİRİLDİ: 1.0 -> 2.0)
        if (currentEnemyBuildings < _lastEnemyBuildingCount)
        {
            int destroyCount = _lastEnemyBuildingCount - currentEnemyBuildings;
            // Bina yıkmak artık çok daha değerli, üsse giden yolu temizlemeyi teşvik eder.
            Agent.AddReward(2.0f * destroyCount);
        }

        // 3. Üsse Hasar Verme (Aynı kaldı)
        if (currentEnemyBaseHealth < _lastEnemyBaseHealth)
        {
            float damage = _lastEnemyBaseHealth - currentEnemyBaseHealth;
            Agent.AddReward(damage * 0.001f);
        }

        _lastEnemyUnitCount = currentEnemyUnits;
        _lastEnemyBuildingCount = currentEnemyBuildings;
        _lastEnemyBaseHealth = currentEnemyBaseHealth;
    }

    public void ResetSimulation()
    {
        _currentStep = 0;
        _gameEnded = false;

        // --- CURRICULUM (ZORLUK) AYARLAMASI ---
        // Config dosyasından 'enemy_difficulty_level' parametresini okuyoruz.
        // Varsayılan 0.0 (Passive)
        float difficultyLevel = Academy.Instance.EnvironmentParameters.GetWithDefault("enemy_difficulty_level", 0.0f);

        if (difficultyLevel < 0.5f) EnemyDifficulty = AIDifficulty.Passive;
        else if (difficultyLevel < 1.5f) EnemyDifficulty = AIDifficulty.Defensive;
        else EnemyDifficulty = AIDifficulty.Aggressive;

        // Debug.Log($"Environment Reset. Difficulty set to: {EnemyDifficulty} (Param: {difficultyLevel})");
        // ---------------------------------------

        // 1. Yeni Dünya Oluştur (Parallel Eğitim için Instance)
        _world = new SimWorldState(MapSize, MapSize);
        GenerateMap();

        // 2. Oyuncu Verilerini Başlat
        if (_world.Players.ContainsKey(1))
        {
            var p1 = _world.Players[1];
            p1.Wood = 500; p1.Stone = 500; p1.Meat = 500; p1.MaxPopulation = 20;

            // Takip değişkenlerini sıfırla
            _lastWood = 500; _lastStone = 500; _lastMeat = 500; _lastWorkerCount = 0;
        }

        if (!_world.Players.ContainsKey(2))
        {
            _world.Players.Add(2, new SimPlayerData { PlayerID = 2, Wood = 500, Stone = 500, Meat = 500, MaxPopulation = 20 });
        }

        // 3. Üsleri Kur
        SetupBase(1, new int2(2, 2));
        SetupBase(2, new int2(MapSize - 3, MapSize - 3));

        // 4. Sistemleri Kur
        _gridSys = new SimGridSystem(_world);
        _unitSys = new SimUnitSystem(_world);
        _buildSys = new SimBuildingSystem(_world);
        _resSys = new SimResourceSystem(_world);

        // Agent'a yeni dünyayı ver
        if (Agent != null)
        {
            Agent.Setup(_world, _gridSys, _unitSys, _buildSys);
        }

        // 5. Rakip AI
        if (UseMacroAI)
        {
            _enemyAI = new SimpleMacroAI(_world, 2, EnemyDifficulty);
        }
        else
        {
            _enemyAI = null;
        }

        // 6. Görselleştirme (Opsiyonel - Sadece gerekliyse açın)
        if (Visualizer != null)
        {
            // Paralel eğitimde 20 tane visualizer açılmasın diye basit bir kontrol yapılabilir
            // Veya sadece sahnedeki ilk Agent için visualizer atanabilir.
            Visualizer.Initialize(_world);
        }

        // Combat Sayaçları Sıfırla
        _lastEnemyUnitCount = 0;
        _lastEnemyBuildingCount = 1;
        _lastEnemyBaseHealth = 1000f;
    }

    private void CheckGameResult()
    {
        if (_gameEnded) return;

        var myBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == 1 && b.Type == SimBuildingType.Base);
        var enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == 2 && b.Type == SimBuildingType.Base);

        if (myBase == null) // Kaybettik
        {
            EndGame(-2.0f); // Kaybetme cezası sabit
        }
        else if (enemyBase == null) // Kazandık (Düşman Ana Binası Yıkıldı)
        {
            // --- YENİ: ERKEN KAZANMA BONUSU ---
            // MaxSteps: 5000
            // Eğer 1000. adımda bitirirse: (5000 - 1000) / 5000 = 0.8 (%80 Bonus)
            // Eğer 4900. adımda bitirirse: (5000 - 4900) / 5000 = 0.02 (%2 Bonus)

            float timeFactor = (float)(MaxSteps - _currentStep) / (float)MaxSteps;

            // Taban Puan: 2.0
            // Maksimum Hız Bonusu: +2.0 (Eğer anında yenerse toplam 4.0 alır)
            // EndGame içeride bunu 10 ile çarpıyor, yani Toplam Puan: 20 ile 40 arasında değişecek.

            float speedBonus = timeFactor * 2.0f;
            float totalWinReward = 2.0f + speedBonus;

            // Loglayalım ki bonusu görelim (İsterseniz sonra kapatırsınız)
            Debug.Log($"🏆 KAZANDIN! Taban: 2.0 + Hız Bonusu: {speedBonus:F2} (Adım: {_currentStep})");

            EndGame(totalWinReward);
        }
    }

    private void EndGame(float reward)
    {
        if (_gameEnded) return;
        _gameEnded = true;

        if (Agent != null)
        {
            // Eğer reward 0 ise (zaman doldu), Passive modda bunu ceza gibi görebiliriz
            // Çünkü passive düşmanı bile yenemediyse başarısızdır.
            if (reward == 0 && EnemyDifficulty == AIDifficulty.Passive) reward = -1.0f;

            Agent.AddReward(reward);
            Agent.EndEpisode();
        }
    }

    // --- HARİTA OLUŞTURMA ---
    private void GenerateMap()
    {
        for (int x = 0; x < MapSize; x++)
        {
            for (int y = 0; y < MapSize; y++)
            {
                _world.Map.Grid[x, y] = new SimMapNode
                {
                    x = x,
                    y = y,
                    Type = SimTileType.Grass,
                    IsWalkable = true,
                    OccupantID = -1
                };
            }
        }

        int resourceCount = 45;
        for (int i = 0; i < resourceCount; i++)
        {
            int x = UnityEngine.Random.Range(0, MapSize);
            int y = UnityEngine.Random.Range(0, MapSize);

            if ((x < 5 && y < 5) || (x > MapSize - 5 && y > MapSize - 5)) continue;

            if (_world.Map.Grid[x, y].IsWalkable)
            {
                var res = new SimResourceData { ID = _world.NextID(), GridPosition = new int2(x, y), AmountRemaining = 500 };
                float r = UnityEngine.Random.value;
                if (r < 0.33f) { res.Type = SimResourceType.Wood; _world.Map.Grid[x, y].Type = SimTileType.Forest; }
                else if (r < 0.66f) { res.Type = SimResourceType.Stone; _world.Map.Grid[x, y].Type = SimTileType.Stone; }
                else { res.Type = SimResourceType.Meat; _world.Map.Grid[x, y].Type = SimTileType.MeatBush; }

                _world.Resources.Add(res.ID, res);
                _world.Map.Grid[x, y].OccupantID = res.ID;
                _world.Map.Grid[x, y].IsWalkable = false;
            }
        }
    }

    private void SetupBase(int pid, int2 pos)
    {
        var building = new SimBuildingData
        {
            ID = _world.NextID(),
            PlayerID = pid,
            Type = SimBuildingType.Base,
            GridPosition = pos,
            Health = 1000,
            MaxHealth = 1000,
            IsConstructed = true
        };
        SimBuildingSystem.InitializeBuildingStats(building);
        _world.Buildings.Add(building.ID, building);
        _world.Map.Grid[pos.x, pos.y].IsWalkable = false;
        _world.Map.Grid[pos.x, pos.y].OccupantID = building.ID;

        // Başlangıç işçileri
        for (int i = 0; i < 3; i++)
        {
            int2? spawnPos = SimGridSystem.FindWalkableNeighbor(_world, pos);
            if (spawnPos.HasValue)
            {
                var unit = new SimUnitData
                {
                    ID = _world.NextID(),
                    PlayerID = pid,
                    UnitType = SimUnitType.Worker,
                    GridPosition = spawnPos.Value,
                    Health = 50,
                    MaxHealth = 50,
                    State = SimTaskType.Idle,
                    MoveSpeed = 5.0f
                };
                _world.Units.Add(unit.ID, unit);
                _world.Map.Grid[spawnPos.Value.x, spawnPos.Value.y].OccupantID = unit.ID;
            }
        }
    }
}