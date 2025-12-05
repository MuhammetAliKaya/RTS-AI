using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics; // Zaman ölçümü için

namespace RTS.Simulation.Orchestrator
{
    public class PSOManager : MonoBehaviour
    {
        [Header("Eğitim Ayarları")]
        public bool VisualizeOnlyFinalBest = false;
        [Tooltip("İşaretlenirse APSO-SL özellikleri kapanır, Standart PSO çalışır (Kıyaslama için).")]
        public bool ForceStandardPSO = false;
        public int PopulationSize = 20;
        public int MaxGenerations = 5000;
        public int MaxTicksPerGame = 2000;

        [Header("Veri Kaydı (Logging)")]
        public bool EnableLogging = true;

        [Header("Rastgelelik (Seed System)")]
        public bool UseRandomMasterSeed = true;
        public int MasterSeed = 12345;
        [Tooltip("Bu jenerasyondaki her bireyin kullandığı seedler")]
        public List<int> CurrentGenerationSeeds;
        public int LastBestSeed;

        [Header("Görsel Ayarlar")]
        public GameVisualizer Visualizer;
        [Range(1f, 10f)] public float VisualSpeed = 1f;
        public bool UseFixedSeedForVisual = true;
        public int VisualFixedSeed = 42;

        // --- GÖZLEM PANELİ (Inspector) ---
        [Header("APSO-SL Durum Paneli")]
        public string CurrentState;
        public float Inertia_W;
        public float Cognitive_C1;
        public float Social_C2;
        public float CurrentBestScore;
        // ---------------------------------

        private PSOAlgorithm _pso;
        private int _currentGen = 0;
        private float _bestFitnessAllTime = float.MinValue;
        private float[] _bestGenesAllTime;
        private bool _isTraining = false;
        private string _statusText = "Hazır";
        private System.Random _masterRng;
        private DataLogger _logger;

        void Start()
        {
            if (Visualizer == null) Visualizer = FindObjectOfType<GameVisualizer>();

            // Logger Başlat
            if (EnableLogging) _logger = new DataLogger();

            // Master Seed Kurulumu
            if (UseRandomMasterSeed) MasterSeed = UnityEngine.Random.Range(0, 9999999);
            _masterRng = new System.Random(MasterSeed);

            UnityEngine.Debug.Log($"🌱 EĞİTİM BAŞLADI - Master Seed: {MasterSeed}");
            StartCoroutine(TrainingRoutine());
        }

        void Update()
        {
            // PSO parametrelerini Inspector'da canlı göster
            if (_pso != null)
            {
                // Anahtarı anlık olarak algoritmaya ilet
                _pso.UseStandardPSO = ForceStandardPSO;

                CurrentState = _pso.CurrentState.ToString();
                Inertia_W = _pso.W;
                Cognitive_C1 = _pso.C1;
                Social_C2 = _pso.C2;
                CurrentBestScore = _bestFitnessAllTime;
            }
        }

        IEnumerator TrainingRoutine()
        {
            _isTraining = true;

            // 14 GENLİ YAPI: [Worker, Soldier, Attack, Def, Barrack, Eco, Farm, Wood, Stone, House, TowerPos, BaseDef, SaveThres, Bloodlust]
            // Algoritmayı MasterSeed'den türetilen bir seed ile başlatıyoruz.
            _pso = new PSOAlgorithm(PopulationSize, 14, 0, 40, _masterRng.Next());

            CurrentGenerationSeeds = new List<int>(new int[PopulationSize]);
            Stopwatch genTimer = new Stopwatch();

            while (_currentGen < MaxGenerations)
            {
                genTimer.Restart();
                _statusText = $"Eğitiliyor... %{(float)_currentGen / MaxGenerations * 100:F1} (Gen: {_currentGen})";

                // --- PARALEL EĞİTİM VE İSTATİSTİK TOPLAMA ---
                var genStats = RunGenerationHeadlessParallelWithStats();

                genTimer.Stop();
                float timeTaken = genTimer.ElapsedMilliseconds / 1000f;

                // --- LOGLAMA ---
                if (EnableLogging && _logger != null)
                {
                    _logger.LogGeneration(
                        _currentGen,
                        _pso.GlobalBestFitness,
                        genStats.AvgFitness,
                        genStats.WinRate,
                        timeTaken,
                        genStats.AverageStats
                    );

                    _logger.LogBestGenome(_currentGen, _pso.GlobalBestFitness, _pso.GlobalBestPosition);
                }

                // --- GBEST GÜNCELLEME ---
                if (_pso.GlobalBestFitness > _bestFitnessAllTime)
                {
                    _bestFitnessAllTime = _pso.GlobalBestFitness;
                    _bestGenesAllTime = (float[])_pso.GlobalBestPosition.Clone();
                }

                if (_currentGen % 10 == 0)
                    UnityEngine.Debug.Log($"🚀 Gen {_currentGen} | Best: {_bestFitnessAllTime:F0} | Time: {timeTaken:F2}s");

                // --- GÖRSEL İZLEME ---
                if (!VisualizeOnlyFinalBest)
                {
                    _statusText = $"İzleniyor... Gen: {_currentGen}";
                    int visualSeed = UseFixedSeedForVisual ? VisualFixedSeed : _masterRng.Next();
                    yield return StartCoroutine(RunVisualMatch(_pso.GlobalBestPosition, visualSeed));
                }
                else
                {
                    yield return null; // Donmayı engelle
                }

                _currentGen++;
            }

            _isTraining = false;
            _statusText = "EĞİTİM BİTTİ!";
            LogFinalResults();

            // Sonsuz döngüde en iyiyi izlet
            while (true)
            {
                int finalSeed = UseFixedSeedForVisual ? VisualFixedSeed : UnityEngine.Random.Range(0, 99999);
                yield return StartCoroutine(RunVisualMatch(_bestGenesAllTime, finalSeed));
                yield return new WaitForSeconds(2f);
            }
        }

        // --- PARALEL EĞİTİM FONKSİYONU ---
        private (float AvgFitness, float WinRate, SimStats AverageStats) RunGenerationHeadlessParallelWithStats()
        {
            // Headless modda logları kapat (Hız için)
            SimConfig.EnableLogs = false;

            var positions = _pso.GetPositions();
            SimStats[] allStats = new SimStats[positions.Count];

            // 1. Seedleri Belirle (Tekrarlanabilirlik için)
            for (int i = 0; i < positions.Count; i++)
            {
                CurrentGenerationSeeds[i] = _masterRng.Next();
            }

            // 2. Paralel İşleme
            Parallel.For(0, positions.Count, i =>
            {
                int seed = CurrentGenerationSeeds[i];
                System.Random rng = new System.Random(seed);

                // Simülasyonu çalıştır ve istatistikleri al
                allStats[i] = SimulateGame(positions[i], false, rng);
            });

            // 3. PSO'ya Raporla ve İstatistik Hesapla
            float totalFit = 0;
            int wins = 0;
            SimStats avgStats = new SimStats();

            for (int i = 0; i < positions.Count; i++)
            {
                float fitness = allStats[i].Fitness;

                // Rekor takibi (Seed için)
                if (fitness > _bestFitnessAllTime)
                {
                    LastBestSeed = CurrentGenerationSeeds[i];
                }

                _pso.ReportFitness(i, fitness);

                totalFit += fitness;
                if (allStats[i].IsWin) wins++;

                // Ortalama istatistikler
                avgStats.GatheredWood += allStats[i].GatheredWood;
                avgStats.SoldierCount += allStats[i].SoldierCount;
                avgStats.WorkerCount += allStats[i].WorkerCount;
            }
            _pso.Step(); // Algoritmayı bir adım ilerlet

            // Ortalamaları al
            avgStats.GatheredWood /= positions.Count;
            avgStats.SoldierCount /= positions.Count;
            avgStats.WorkerCount /= positions.Count;

            return (totalFit / positions.Count, (float)wins / positions.Count, avgStats);
        }

        // --- SİMÜLASYON ÇEKİRDEĞİ ---
        private SimStats SimulateGame(float[] genes, bool isVisual, System.Random rng)
        {
            SimWorldState world = CreateWorldWithResources(rng);
            if (!isVisual) SimGameContext.ActiveWorld = world;

            // AI Kurulumu (System.Random ile)
            ParametricMacroAI myAI = new ParametricMacroAI(world, 1, genes, rng);

            // Düşman AI da artık SimConfig kullanıyor ve eşit şartlarda
            SimpleMacroAI enemyAI = new SimpleMacroAI(world, 2);

            float dt = 0.25f;
            int tick = 0;
            int maxTicks = MaxTicksPerGame;
            bool win = false;
            bool gameOver = false;

            // Thread-Safe Cache
            List<SimUnitData> localUnitCache = new List<SimUnitData>(100);

            while (tick < maxTicks)
            {
                world.TickCount++;
                SimBuildingSystem.UpdateAllBuildings(world, dt);

                localUnitCache.Clear();
                localUnitCache.AddRange(world.Units.Values);
                for (int i = 0; i < localUnitCache.Count; i++)
                {
                    SimUnitSystem.UpdateUnit(localUnitCache[i], world, dt);
                }

                // Tick Atlatma (Optimiasyon)
                if (tick % 5 == 0)
                {
                    myAI.Update(dt * 5f);
                    enemyAI.Update(dt * 5f);
                }

                // Kazanma Kontrolü
                bool enemyAlive = false;
                bool meAlive = false;
                foreach (var b in world.Buildings.Values)
                {
                    if (b.Type == SimBuildingType.Base)
                    {
                        if (b.PlayerID == 2) enemyAlive = true;
                        if (b.PlayerID == 1) meAlive = true;
                    }
                }

                if (!enemyAlive) { win = true; gameOver = true; break; }
                if (!meAlive) { win = false; gameOver = true; break; }

                tick++;
            }

            // --- SONUÇLARI PAKETLE ---
            SimStats stats = new SimStats();
            stats.IsWin = win;
            stats.MatchDuration = tick * dt;

            // Kaynaklar
            var p1 = SimResourceSystem.GetPlayer(world, 1);
            if (p1 != null)
            {
                stats.GatheredWood = p1.Wood;
                stats.GatheredMeat = p1.Meat;
                stats.GatheredStone = p1.Stone;
            }

            // Birimler ve Binalar
            foreach (var u in world.Units.Values)
            {
                if (u.PlayerID == 1)
                {
                    if (u.UnitType == SimUnitType.Soldier) stats.SoldierCount++;
                    if (u.UnitType == SimUnitType.Worker) stats.WorkerCount++;
                }
            }

            float myBaseHealth = 0;
            float enemyBaseHealth = 0;
            foreach (var b in world.Buildings.Values)
            {
                if (b.Type == SimBuildingType.Base)
                {
                    if (b.PlayerID == 1) myBaseHealth = b.Health;
                    if (b.PlayerID == 2) enemyBaseHealth = b.Health;
                }
                if (b.PlayerID == 1 && b.Type == SimBuildingType.Tower && b.IsConstructed) stats.TowersBuilt++;
                if (b.PlayerID == 1 && b.Type == SimBuildingType.Barracks) stats.BarracksBuilt++;
            }
            stats.BaseHealthRemaining = myBaseHealth;

            // --- FITNESS HESABI ---
            // 1. Kaynak Puanı
            float resScore = (stats.GatheredWood + stats.GatheredMeat) * 0.5f;

            // 2. Asker Puanı
            float soldierScore = stats.SoldierCount * 100f;

            // 3. Savaş Puanı
            float damageDealt = (SimConfig.BASE_MAX_HEALTH - enemyBaseHealth);
            float damageTaken = (SimConfig.BASE_MAX_HEALTH - myBaseHealth);
            float defensePenalty = damageTaken * 100f;
            float combatScore = (damageDealt * 50f) - defensePenalty;

            // 4. Bonuslar
            float towerBonus = (win) ? (stats.TowersBuilt * 2000f) : 0f;
            float winBonus = win ? 100000f : 0f;
            float timeBonus = win ? (maxTicks - tick) * 20f : 0f;
            float deathPenalty = (gameOver && !win) ? -50000f : 0f;

            stats.Fitness = resScore + soldierScore + combatScore + towerBonus + winBonus + deathPenalty + timeBonus;

            return stats;
        }

        private IEnumerator RunVisualMatch(float[] genes, int seed)
        {
            SimConfig.EnableLogs = true; // Görsel modda logları aç
            if (Visualizer) Visualizer.ResetVisuals();

            System.Random visualRng = new System.Random(seed);
            SimWorldState world = CreateWorldWithResources(visualRng);
            SimGameContext.ActiveWorld = world;

            ParametricMacroAI myAI = new ParametricMacroAI(world, 1, genes, visualRng);
            SimpleMacroAI enemyAI = new SimpleMacroAI(world, 2);

            bool isDone = false;
            float timer = 0;

            while (!isDone)
            {
                try
                {
                    float dt = Time.deltaTime * VisualSpeed;
                    world.TickCount++;
                    SimBuildingSystem.UpdateAllBuildings(world, dt);

                    var unitList = world.Units.Values.ToList();
                    foreach (var u in unitList) SimUnitSystem.UpdateUnit(u, world, dt);

                    myAI.Update(dt);
                    enemyAI.Update(dt);

                    bool enemyHasBase = world.Buildings.Values.Any(b => b.PlayerID == 2 && b.Type == SimBuildingType.Base);
                    bool meHasBase = world.Buildings.Values.Any(b => b.PlayerID == 1 && b.Type == SimBuildingType.Base);

                    if (!enemyHasBase || !meHasBase) isDone = true;
                    if (Input.GetKeyDown(KeyCode.Space)) isDone = true;
                    if (timer > 60f) isDone = true;
                }
                catch (System.Exception e) { UnityEngine.Debug.LogError(e); isDone = true; }

                timer += Time.deltaTime;
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
        }

        private SimWorldState CreateWorldWithResources(System.Random rng)
        {
            SimWorldState world = new SimWorldState(SimConfig.MAP_WIDTH, SimConfig.MAP_HEIGHT);
            if (!world.Players.ContainsKey(2)) world.Players.Add(2, new SimPlayerData { PlayerID = 2 });

            SpawnBaseForPlayer(world, 1, new int2(5, 5));
            SpawnBaseForPlayer(world, 2, new int2(44, 44));

            for (int i = 0; i < 30; i++) SpawnResource(world, SimResourceType.Wood, rng);
            for (int i = 0; i < 20; i++) SpawnResource(world, SimResourceType.Stone, rng);
            for (int i = 0; i < 20; i++) SpawnResource(world, SimResourceType.Meat, rng);

            return world;
        }

        private void SpawnResource(SimWorldState world, SimResourceType type, System.Random rng)
        {
            int x = rng.Next(2, SimConfig.MAP_WIDTH - 2);
            int y = rng.Next(2, SimConfig.MAP_HEIGHT - 2);

            int2 pos = new int2(x, y);
            if (SimGridSystem.IsWalkable(world, pos))
            {
                var r = new SimResourceData { ID = world.NextID(), Type = type, GridPosition = pos, AmountRemaining = 500 };
                world.Resources.Add(r.ID, r);
                world.Map.Grid[x, y].IsWalkable = false;

                if (type == SimResourceType.Wood) world.Map.Grid[x, y].Type = SimTileType.Forest;
                else if (type == SimResourceType.Stone) world.Map.Grid[x, y].Type = SimTileType.Stone;
                else if (type == SimResourceType.Meat) world.Map.Grid[x, y].Type = SimTileType.MeatBush;
            }
        }

        private void SpawnBaseForPlayer(SimWorldState world, int playerID, int2 pos)
        {
            var baseB = new SimBuildingData
            {
                ID = world.NextID(),
                PlayerID = playerID,
                Type = SimBuildingType.Base,
                GridPosition = pos,
                IsConstructed = true,
                Health = SimConfig.BASE_MAX_HEALTH,
                MaxHealth = SimConfig.BASE_MAX_HEALTH
            };
            SimBuildingSystem.InitializeBuildingStats(baseB);
            world.Buildings.Add(baseB.ID, baseB);
            world.Map.Grid[pos.x, pos.y].IsWalkable = false;

            SimResourceSystem.IncreaseMaxPopulation(world, playerID, SimConfig.POPULATION_BASE);
            for (int i = 0; i < SimConfig.START_WORKER_COUNT; i++)
                SimBuildingSystem.SpawnUnit(world, new int2(pos.x + 1 + i, pos.y), SimUnitType.Worker, playerID);

            SimResourceSystem.AddResource(world, playerID, SimResourceType.Wood, SimConfig.START_WOOD);
            SimResourceSystem.AddResource(world, playerID, SimResourceType.Meat, SimConfig.START_MEAT);
            SimResourceSystem.AddResource(world, playerID, SimResourceType.Stone, SimConfig.START_STONE);
        }

        private void LogFinalResults()
        {
            UnityEngine.Debug.Log("🏁🏁🏁 EĞİTİM TAMAMLANDI! 🏁🏁🏁");
            UnityEngine.Debug.Log($"🏆 EN YÜKSEK SKOR: {_bestFitnessAllTime:F2}");

            if (_bestGenesAllTime != null)
            {
                string genesStr = string.Join(", ", _bestGenesAllTime.Select(x => x.ToString("F2").Replace(',', '.') + "f"));
                UnityEngine.Debug.Log("⬇️ KOPYALANABİLİR GENLER: ⬇️");
                UnityEngine.Debug.Log($"public float[] BestGenes = new float[] {{ {genesStr} }};");
            }
        }

        // void OnGUI()
        // {
        //     GUI.Box(new Rect(10, 10, 250, 120), "PSO Master");
        //     GUI.Label(new Rect(20, 35, 230, 20), _statusText);
        //     GUI.Label(new Rect(20, 55, 230, 20), $"Best Score: {_bestFitnessAllTime:F0}");
        //     GUI.Label(new Rect(20, 75, 230, 20), $"Best Seed: {LastBestSeed}");
        //     string mode = ForceStandardPSO ? "STANDART PSO" : "APSO-SL";
        //     GUI.Label(new Rect(20, 95, 230, 20), $"Mod: {mode}");
        // }
    }
}