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

    [Range(1f, 100f)] // Unity Editor'de 1 ile 100 arasında bir kaydırma çubuğu sağlar
    [Tooltip("Simülasyonun Çalışma Hızı. 1.0 = Gerçek Zamanlı Hız, 20.0 = Hızlı Eğitim")]
    public float SimulationTimeScale = 20.0f; // <-- Bu yeni değişkendir
    public float dt = 20.0f; // <-- Bu yeni değişkendir


    [Header("Görselleştirme")] // <-- Bu bloğu ekle
    public GameVisualizer Visualizer; // <-- Bu alanı ekle

    [Header("Rakip Ayarları")]
    public bool UseMacroAI = true;

    // SİSTEMLER
    private SimWorldState _world;
    private SimGridSystem _gridSys;
    private SimUnitSystem _unitSys;
    private SimBuildingSystem _buildSys;
    private SimResourceSystem _resSys;

    // RAKİP
    private SimpleMacroAI _enemyAI;
    private int _currentStep = 0;

    // Oyun bitti mi kontrolü
    private bool _gameEnded = false;

    void Start()
    {
        if (Agent == null) Agent = GetComponentInChildren<RTSAgent>();

        // Hızı artır
        Application.targetFrameRate = -1;
        Time.timeScale = SimulationTimeScale;
        // Time.timeScale = 20.0f;

        ResetSimulation();
    }

    void Update()
    {
        if (_world != null && !_gameEnded)
        {
            SimulationStep();
        }
    }

    public void SimulationStep()
    {
        // float dt = 0.1f;

        // 1. Düşman AI Hamlesi
        if (_enemyAI != null)
        {
            _enemyAI.Update(dt);
        }

        // 2. Agent Karar İsteği
        if (Agent != null) Agent.RequestDecision();

        // 3. Simülasyonu İlerlet <-- DEĞİŞTİRİLDİ
        if (_buildSys != null) _buildSys.UpdateAllBuildings(dt);

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
            EndGame(0); // Berabere / Zaman Doldu
        }
    }

    public void ResetSimulation()
    {
        _currentStep = 0;
        _gameEnded = false;

        // 1. YENİ DÜNYA OLUŞTUR (Eski veriler silinir)
        _world = new SimWorldState(MapSize, MapSize);
        GenerateMap();

        // 2. OYUNCULARI EKLE
        // NOT: SimWorldState constructor'ı zaten Player 1'i ekliyor
        // Bu yüzden sadece Player 2'yi eklememiz yeterli

        // Player 1 zaten var, sadece kaynaklarını güncelle
        if (_world.Players.ContainsKey(1))
        {
            var p1 = _world.Players[1];
            p1.Wood = 500;
            p1.Stone = 500;
            p1.Meat = 500;
            p1.MaxPopulation = 20;
        }

        // Player 2'yi ekle (yeni dünya olduğu için bu güvenli)
        if (!_world.Players.ContainsKey(2))
        {
            _world.Players.Add(2, new SimPlayerData
            {
                PlayerID = 2,
                Wood = 500,
                Stone = 500,
                Meat = 500,
                MaxPopulation = 20
            });
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

        // 5. Rakip AI Başlat
        if (UseMacroAI)
        {
            _enemyAI = new SimpleMacroAI(_world, 2);
        }
        else
        {
            _enemyAI = null;
        }
        // 6. GÖRSELLEŞTİRİCİYİ BAĞLA <-- Yeni Eklenecek Kısım
        if (Visualizer != null)
        {
            Visualizer.Initialize(_world);
        }

    }

    private void CheckGameResult()
    {
        if (_gameEnded) return;

        // Ana bina kontrolü
        var myBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == 1 && b.Type == SimBuildingType.Base);
        var enemyBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == 2 && b.Type == SimBuildingType.Base);

        if (myBase == null) // Kaybettik
        {
            EndGame(-1.0f);
        }
        else if (enemyBase == null) // Kazandık
        {
            EndGame(1.0f);
        }
    }

    private void EndGame(float reward)
    {
        if (_gameEnded) return; // Çift çağrı önleme
        _gameEnded = true;

        if (Agent != null)
        {
            Agent.AddReward(reward * 10.0f);
            Agent.EndEpisode(); // Bu OnEpisodeBegin'i tetikler
        }
        // NOT: ResetSimulation() artık OnEpisodeBegin'den çağrılacak
    }

    // --- YARDIMCI METOTLAR ---
    // AdversarialTrainerRunner.cs dosyası içindeki GenerateMap() metodu
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

        // Rastgele kaynaklar ekle
        // System.Random rng = new System.Random(); // ARTIK GEREKSİZ
        int resourceCount = 45;

        // Rastgele 15 noktaya kaynak yerleştirmeye çalış
        for (int i = 0; i < resourceCount; i++)
        {
            // Random x ve y koordinatlarını UnityEngine.Random ile alalım
            int x = UnityEngine.Random.Range(0, MapSize);
            int y = UnityEngine.Random.Range(0, MapSize);

            // Üs bölgelerinden uzak tut
            if ((x < 5 && y < 5) || (x > MapSize - 5 && y > MapSize - 5)) continue;

            if (_world.Map.Grid[x, y].IsWalkable)
            {
                var res = new SimResourceData
                {
                    ID = _world.NextID(),
                    GridPosition = new int2(x, y),
                    AmountRemaining = 500
                };

                float r = UnityEngine.Random.value; // Rastgele kaynak türü seçimi

                if (r < 0.33f)
                {
                    res.Type = SimResourceType.Wood;
                    _world.Map.Grid[x, y].Type = SimTileType.Forest;
                }
                else if (r < 0.66f)
                {
                    res.Type = SimResourceType.Stone;
                    _world.Map.Grid[x, y].Type = SimTileType.Stone;
                }
                else
                {
                    res.Type = SimResourceType.Meat;
                    _world.Map.Grid[x, y].Type = SimTileType.MeatBush;
                }

                _world.Resources.Add(res.ID, res);
                _world.Map.Grid[x, y].OccupantID = res.ID;
                _world.Map.Grid[x, y].IsWalkable = false; // Kaynakların üzerine yürünemez
            }
        }
    }

    private void SetupBase(int pid, int2 pos)
    {
        // Ana bina oluştur
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