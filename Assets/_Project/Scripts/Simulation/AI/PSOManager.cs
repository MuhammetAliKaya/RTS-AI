using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using RTS.Simulation.AI;

namespace RTS.Simulation.Orchestrator
{
    public class PSOManager : MonoBehaviour
    {
        [Header("Eğitim Ayarları")]
        public bool VisualizeOnlyFinalBest = false;
        public bool ForceStandardPSO = false;
        public int PopulationSize = 20;
        public int MaxGenerations = 5000;
        public int MaxTicksPerGame = 2000;

        [Header("Uzmanlık Hedefi")]
        public AIStrategyMode CurrentTrainingGoal;

        [Header("Veri Kaydı (Logging)")]
        public bool EnableLogging = true;

        [Header("Rastgelelik (Seed System)")]
        public bool UseRandomMasterSeed = true;
        public int MasterSeed = 12345;
        public List<int> CurrentGenerationSeeds;
        public int LastBestSeed;

        [Header("Görsel Ayarlar")]
        public GameVisualizer Visualizer;
        [Range(1f, 10f)] public float VisualSpeed = 1f;
        public bool UseFixedSeedForVisual = true;
        public int VisualFixedSeed = 42;

        [Header("APSO-SL Durum Paneli")]
        public string CurrentState;
        public float Inertia_W;
        public float Cognitive_C1;
        public float Social_C2;
        public float CurrentBestScore;

        private PSOAlgorithm _pso;
        private int _currentGen = 0;
        private float _bestFitnessAllTime = float.MinValue;
        private float[] _bestGenesAllTime;
        private bool _isTraining = false;
        private string _statusText = "Hazır";
        private System.Random _masterRng;
        private DataLogger _logger;

        [Header("Güvenlik")]
        public float MaxRealTimePerGame = 5.0f; // Bir maç en fazla 5 saniye sürsün (Donmayı engeller)

        void Start()
        {
            if (Visualizer == null) Visualizer = FindObjectOfType<GameVisualizer>();
            if (EnableLogging) _logger = new DataLogger();
            if (UseRandomMasterSeed) MasterSeed = UnityEngine.Random.Range(0, 9999999);
            _masterRng = new System.Random(MasterSeed);

            UnityEngine.Debug.Log($"🌱 EĞİTİM BAŞLADI - Master Seed: {MasterSeed}");
            StartCoroutine(TrainingRoutine());
        }

        void Update()
        {
            if (_pso != null)
            {
                _pso.UseStandardPSO = ForceStandardPSO;
                CurrentState = _pso.CurrentState.ToString();
                Inertia_W = _pso.W;
                Cognitive_C1 = _pso.C1;
                Social_C2 = _pso.C2;
                CurrentBestScore = _bestFitnessAllTime;
            }
        }

        private AIStrategyMode GetEnemyStrategy(AIStrategyMode myGoal)
        {
            switch (myGoal)
            {
                case AIStrategyMode.Defensive: return AIStrategyMode.Aggressive;
                case AIStrategyMode.Aggressive: return AIStrategyMode.Defensive;
                case AIStrategyMode.Economic: return AIStrategyMode.Economic;
                case AIStrategyMode.General: return AIStrategyMode.General;

                default: return AIStrategyMode.Aggressive;
            }
        }

        IEnumerator TrainingRoutine()
        {
            _isTraining = true;
            _pso = new PSOAlgorithm(PopulationSize, 14, 0, 40, _masterRng.Next());
            CurrentGenerationSeeds = new List<int>(new int[PopulationSize]);
            Stopwatch genTimer = new Stopwatch();

            while (_currentGen < MaxGenerations)
            {
                genTimer.Restart();
                _statusText = $"Eğitiliyor... %{(float)_currentGen / MaxGenerations * 100:F1} (Gen: {_currentGen})";

                var task = RunGenerationAsync();
                yield return new WaitUntil(() => task.IsCompleted);

                var genStats = task.Result; // Sonucu al

                genTimer.Stop();
                float timeTaken = genTimer.ElapsedMilliseconds / 1000f;

                if (EnableLogging && _logger != null)
                {
                    _logger.LogGeneration(_currentGen, _pso.GlobalBestFitness, genStats.AvgFitness, genStats.WinRate, timeTaken, genStats.AverageStats);
                    _logger.LogBestGenome(_currentGen, _pso.GlobalBestFitness, _pso.GlobalBestPosition);
                }

                if (_pso.GlobalBestFitness > _bestFitnessAllTime)
                {
                    _bestFitnessAllTime = _pso.GlobalBestFitness;
                    _bestGenesAllTime = (float[])_pso.GlobalBestPosition.Clone();
                }

                if (_currentGen % 10 == 0)
                    UnityEngine.Debug.Log($"🚀 Gen {_currentGen} | Best: {_bestFitnessAllTime:F0} | Time: {timeTaken:F2}s");

                if (!VisualizeOnlyFinalBest)
                {
                    _statusText = $"İzleniyor... Gen: {_currentGen}";
                    int visualSeed = UseFixedSeedForVisual ? VisualFixedSeed : _masterRng.Next();
                    yield return StartCoroutine(RunVisualMatch(_pso.GlobalBestPosition, visualSeed));
                }
                else
                {
                    yield return null;
                }
                _currentGen++;
            }

            _isTraining = false;
            _statusText = "EĞİTİM BİTTİ!";
            LogFinalResults();

            while (true)
            {
                int finalSeed = UseFixedSeedForVisual ? VisualFixedSeed : UnityEngine.Random.Range(0, 99999);
                yield return StartCoroutine(RunVisualMatch(_bestGenesAllTime, finalSeed));
                yield return new WaitForSeconds(2f);
            }
        }

        private async Task<(float AvgFitness, float WinRate, SimStats AverageStats)> RunGenerationAsync()
        {
            SimConfig.EnableLogs = false;
            var positions = _pso.GetPositions();
            SimStats[] allStats = new SimStats[positions.Count];

            // Seedleri hazırla
            for (int i = 0; i < positions.Count; i++) CurrentGenerationSeeds[i] = _masterRng.Next();

            // İşlemi ThreadPool'a atıyoruz ki Unity Main Thread donmasın
            await Task.Run(() =>
            {
                Parallel.For(0, positions.Count, i =>
                {
                    try
                    {
                        int seed = CurrentGenerationSeeds[i];
                        System.Random rng = new System.Random(seed);
                        allStats[i] = SimulateGame(positions[i], false, rng);
                    }
                    catch (System.Exception e)
                    {
                        // Hata olursa en azından fitness'ı 0 yapıp devam etsin, donmasın
                        UnityEngine.Debug.LogError($"Thread Error in Sim {i}: {e.Message}");
                        allStats[i] = new SimStats(); // Boş istatistik
                    }
                });
            });

            // Sonuçları toplama (Main Thread'de yapılabilir artık)
            float totalFit = 0;
            int wins = 0;
            SimStats avgStats = new SimStats();

            for (int i = 0; i < positions.Count; i++)
            {
                float fitness = allStats[i].Fitness;
                if (fitness > _bestFitnessAllTime) LastBestSeed = CurrentGenerationSeeds[i];
                _pso.ReportFitness(i, fitness);
                totalFit += fitness;
                if (allStats[i].IsWin) wins++;
                // ... istatistik toplama kodların aynen kalabilir ...
            }
            _pso.Step();

            return (totalFit / positions.Count, (float)wins / positions.Count, avgStats);
        }

        private SimStats SimulateGame(float[] genes, bool isVisual, System.Random rng)
        {
            // --- GÜVENLİK ZAMANLAYICISI BAŞLAT ---
            Stopwatch safetyTimer = Stopwatch.StartNew();
            // -------------------------------------
            SimWorldState world = CreateWorldWithResources(rng);
            // if (!isVisual) SimGameContext.ActiveWorld = world;
            if (isVisual) SimGameContext.ActiveWorld = world;
            SpecializedMacroAI myAI = new SpecializedMacroAI(world, 1, genes, CurrentTrainingGoal, rng);
            AIStrategyMode enemyMode = GetEnemyStrategy(CurrentTrainingGoal);
            SpecializedMacroAI enemyAI = new SpecializedMacroAI(world, 2, null, enemyMode, rng);

            float dt = 0.25f;
            int tick = 0;
            int maxTicks = MaxTicksPerGame;
            bool win = false;
            bool gameOver = false;

            // --- SAVAŞ TAKİBİ İÇİN ÖNBELLEKLER ---
            HashSet<int> knownEnemyUnits = new HashSet<int>();
            HashSet<int> knownEnemyBuildings = new HashSet<int>();
            int killedUnits = 0;
            int destroyedBuildings = 0;

            List<SimUnitData> localUnitCache = new List<SimUnitData>(100);

            while (tick < maxTicks)
            {
                // --- KRİTİK KONTROL: SÜRE DOLDU MU? ---
                if (safetyTimer.Elapsed.TotalSeconds > MaxRealTimePerGame && !isVisual) // Görsel modda kesme
                {
                    // Süre doldu, maçı olduğu yerde bitir!
                    break;
                }
                // --------------------------------------

                world.TickCount++;
                SimBuildingSystem.UpdateAllBuildings(world, dt);
                localUnitCache.Clear();
                localUnitCache.AddRange(world.Units.Values);
                for (int i = 0; i < localUnitCache.Count; i++) SimUnitSystem.UpdateUnit(localUnitCache[i], world, dt);

                if (tick % 5 == 0)
                {
                    myAI.Update(dt * 5f);
                    enemyAI.Update(dt * 5f);
                }

                // --- SAVAŞ İSTATİSTİĞİ TAKİBİ (DÜŞMAN KAYIPLARI) ---

                // 1. Düşman Birlikleri Kontrolü
                // Mevcut tüm düşmanları bul
                var currentEnemies = world.Units.Values.Where(u => u.PlayerID == 2).Select(u => u.ID).ToHashSet();

                // Önceden bildiğimiz ama şimdi olmayanlar = ÖLDÜRÜLENLER
                foreach (var knownID in knownEnemyUnits)
                {
                    if (!currentEnemies.Contains(knownID)) killedUnits++;
                }
                // Listeyi güncelle (Yeni doğanları ekle)
                knownEnemyUnits = currentEnemies;

                // 2. Düşman Binaları Kontrolü
                var currentEnemyBuildings = world.Buildings.Values.Where(b => b.PlayerID == 2 && b.Type != SimBuildingType.Base).Select(b => b.ID).ToHashSet();

                foreach (var knownID in knownEnemyBuildings)
                {
                    if (!currentEnemyBuildings.Contains(knownID)) destroyedBuildings++;
                }
                knownEnemyBuildings = currentEnemyBuildings;
                // ----------------------------------------------------

                if (CurrentTrainingGoal != AIStrategyMode.Economic)
                {
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
                }
                tick++;
            }

            SimStats stats = new SimStats();
            stats.IsWin = win;
            stats.MatchDuration = tick * dt;
            stats.EnemyUnitsKilled = killedUnits;           // Skor için kritik
            stats.EnemyBuildingsDestroyed = destroyedBuildings; // Skor için kritik

            var p1 = SimResourceSystem.GetPlayer(world, 1);
            if (p1 != null)
            {
                stats.GatheredWood = p1.Wood;
                stats.GatheredMeat = p1.Meat;
                stats.GatheredStone = p1.Stone;
            }
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
                if (b.PlayerID == 1)
                {
                    if (b.Type == SimBuildingType.Tower && b.IsConstructed) stats.TowersBuilt++;
                    if (b.Type == SimBuildingType.Barracks) stats.BarracksBuilt++;
                    if (b.Type == SimBuildingType.Farm) stats.FarmsBuilt++;
                    if (b.Type == SimBuildingType.WoodCutter) stats.WoodCuttersBuilt++;
                    if (b.Type == SimBuildingType.StonePit) stats.StonePitsBuilt++;
                }
            }
            stats.BaseHealthRemaining = myBaseHealth;
            stats.Fitness = CalculateSpecializedFitness(stats, CurrentTrainingGoal, enemyBaseHealth, myBaseHealth);
            return stats;
        }

        private IEnumerator RunVisualMatch(float[] genes, int seed)
        {
            SimConfig.EnableLogs = true;
            if (Visualizer) Visualizer.ResetVisuals();

            System.Random visualRng = new System.Random(seed);
            SimWorldState world = CreateWorldWithResources(visualRng);
            SimGameContext.ActiveWorld = world;

            SpecializedMacroAI myAI = new SpecializedMacroAI(world, 1, genes, CurrentTrainingGoal, visualRng);
            AIStrategyMode enemyMode = GetEnemyStrategy(CurrentTrainingGoal);
            SpecializedMacroAI enemyAI = new SpecializedMacroAI(world, 2, null, enemyMode, visualRng);

            UnityEngine.Debug.Log($"⚔️ VS MATCH: Player({CurrentTrainingGoal}) vs Enemy({enemyMode})");

            bool isDone = false;
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

                    if (CurrentTrainingGoal == AIStrategyMode.Economic)
                    {
                        if (world.TickCount >= MaxTicksPerGame) isDone = true;
                    }
                    else
                    {
                        bool enemyHasBase = world.Buildings.Values.Any(b => b.PlayerID == 2 && b.Type == SimBuildingType.Base);
                        bool meHasBase = world.Buildings.Values.Any(b => b.PlayerID == 1 && b.Type == SimBuildingType.Base);
                        if (!enemyHasBase || !meHasBase) isDone = true;
                        if (world.TickCount >= MaxTicksPerGame) isDone = true;
                    }

                    if (Input.GetKeyDown(KeyCode.Space)) isDone = true;
                }
                catch (System.Exception e) { UnityEngine.Debug.LogError(e); isDone = true; }
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
            for (int i = 0; i < 50; i++) SpawnResource(world, SimResourceType.Wood, rng);
            for (int i = 0; i < 50; i++) SpawnResource(world, SimResourceType.Stone, rng);
            for (int i = 0; i < 50; i++) SpawnResource(world, SimResourceType.Meat, rng);
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
            var baseB = new SimBuildingData { ID = world.NextID(), PlayerID = playerID, Type = SimBuildingType.Base, GridPosition = pos, IsConstructed = true, Health = SimConfig.BASE_MAX_HEALTH, MaxHealth = SimConfig.BASE_MAX_HEALTH };
            SimBuildingSystem.InitializeBuildingStats(baseB, true);
            world.Buildings.Add(baseB.ID, baseB);
            if (world.Map.IsInBounds(pos))
            {
                world.Map.Grid[pos.x, pos.y].IsWalkable = false;
                world.Map.Grid[pos.x, pos.y].OccupantID = baseB.ID;
            }
            SimResourceSystem.IncreaseMaxPopulation(world, playerID, SimConfig.POPULATION_BASE);
            for (int i = 0; i < SimConfig.START_WORKER_COUNT; i++) SimBuildingSystem.SpawnUnit(world, new int2(pos.x + 1 + i, pos.y), SimUnitType.Worker, playerID);
            SimResourceSystem.AddResource(world, playerID, SimResourceType.Wood, SimConfig.START_WOOD);
            SimResourceSystem.AddResource(world, playerID, SimResourceType.Meat, SimConfig.START_MEAT);
            SimResourceSystem.AddResource(world, playerID, SimResourceType.Stone, SimConfig.START_STONE);
        }

        private void LogFinalResults()
        {
            UnityEngine.Debug.Log("🏁 EĞİTİM TAMAMLANDI! 🏁");
            if (_bestGenesAllTime != null)
            {
                string genesStr = string.Join(", ", _bestGenesAllTime.Select(x => x.ToString("F2").Replace(',', '.') + "f"));
                UnityEngine.Debug.Log($"Best Genes: {genesStr}");
            }
        }

        private float CalculateSpecializedFitness(SimStats stats, AIStrategyMode trainingGoal, float enemyBaseHealth, float myBaseHealth)
        {
            float score = 0;
            switch (trainingGoal)
            {
                case AIStrategyMode.Economic:
                    float totalResource = stats.GatheredWood + stats.GatheredMeat + stats.GatheredStone;
                    float minRes = Mathf.Min(stats.GatheredWood, Mathf.Min(stats.GatheredMeat, stats.GatheredStone));
                    float maxRes = Mathf.Max(stats.GatheredWood, Mathf.Max(stats.GatheredMeat, stats.GatheredStone));
                    float difference = maxRes - minRes;
                    float productionBuildingScore = (stats.FarmsBuilt + stats.WoodCuttersBuilt + stats.StonePitsBuilt) * 1000f;
                    float workerScore = stats.WorkerCount * 150f;
                    float resourceScore = (totalResource * 1.0f) - (difference * 2.0f);
                    score = productionBuildingScore + workerScore + resourceScore;
                    break;

                case AIStrategyMode.Defensive:
                    // --- GÜNCELLENMİŞ SAVUNMA PUANLAMASI (AKTİF SAVUNMA) ---

                    // 1. HAYATTA KALAN KULELER (Statik Savunma)
                    // Puanı biraz düşürdük ki asker basmaya da bütçe ayırsın.
                    float livingTowerScore = stats.TowersBuilt * 2000f; // (Eskiden 10.000'di, çok yüksekti)

                    // 2. ÖLDÜRÜLEN DÜŞMAN (AKTİF SAVUNMA - YENİ!)
                    // Savunma yapmak, sadece durmak değil tehdidi yok etmektir.
                    // Düşman öldürmeye puan verelim ki asker bassın.
                    float defenseKillScore = stats.EnemyUnitsKilled * 10000f;

                    // 3. SAVUNMA ORDUSU (YENİ!)
                    // Kuleleri koruyacak asker varlığı.
                    // float defenseArmyScore = stats.SoldierCount * 50f;

                    // 4. BASE SAĞLIĞI (KRİTİK)
                    float healthRatio = myBaseHealth / SimConfig.BASE_MAX_HEALTH;
                    float baseHealthScore = myBaseHealth * 5000f;

                    // 5. HAYATTA KALMA SÜRESİ
                    float survivalBonus = 0f;
                    if (myBaseHealth > 0 && stats.MatchDuration >= (MaxTicksPerGame * 0.25f * 0.95f))
                    {
                        survivalBonus = 150000f; // Maçı kaybetmemek asıl amaç
                    }

                    // TOPLAM SKOR
                    score = livingTowerScore + defenseKillScore
                    // + defenseArmyScore 
                    + baseHealthScore + survivalBonus;

                    // CEZALAR
                    if (stats.TowersBuilt == 0 && stats.SoldierCount == 0)
                    {
                        score -= 50000f; // Hiçbir şey yapmıyorsa büyük ceza
                    }
                    else if (healthRatio < 0.2f)
                    {
                        score *= 0.5f; // Base ölmek üzereyse puan kır
                    }
                    break;

                case AIStrategyMode.Aggressive:
                    // --- NİHAİ AGRESİF PUANLAMA ---

                    // 1. Öldürülen Asker Puanı (EN ÖNEMLİSİ - Combat)
                    score += stats.EnemyUnitsKilled * 300f;

                    // 2. Yıkılan Bina Puanı (Stratejik Hedef)
                    score += stats.EnemyBuildingsDestroyed * 600f;

                    // 3. Asker Üretimi (Ordu Kurma)
                    score += stats.SoldierCount * 100f;

                    // 4. Base Hasarı (Ana Hedef)
                    score += (SimConfig.BASE_MAX_HEALTH - enemyBaseHealth) * 5f;

                    // 5. Kazanma (Büyük Ödül)
                    if (stats.IsWin)
                    {
                        score += 10000f;
                        score += (MaxTicksPerGame - (stats.MatchDuration / 0.25f)) * 20f; // Hız Bonusu
                    }
                    else if (stats.SoldierCount == 0)
                    {
                        score = 0; // Asker basmayana puan yok!
                    }
                    break;

                case AIStrategyMode.General:
                    // --- GENERAL FITNESS ---

                    // 1. KAZANMA (En Büyük Ödül)
                    if (stats.IsWin)
                    {
                        score += 10000f;
                        float maxTime = MaxTicksPerGame * 0.25f;
                        if (stats.MatchDuration < maxTime) score += (maxTime - stats.MatchDuration) * 10f;
                    }

                    // 2. HASAR VERME VE YIKIM
                    score += stats.EnemyUnitsKilled * 200f;
                    score += stats.EnemyBuildingsDestroyed * 400f;

                    // 3. EKONOMİ (Sürdürülebilirlik)
                    float totalRes = stats.GatheredWood + stats.GatheredMeat + stats.GatheredStone;
                    score += totalRes * 0.2f;

                    // 4. GELİŞİM
                    score += stats.WorkerCount * 50f;
                    score += stats.SoldierCount * 60f;

                    // 5. CEZALAR
                    if (stats.SoldierCount == 0) score -= 2000f; // Asker basmayan kaybeder
                    if (stats.WorkerCount < 5) score -= 1000f;   // Ekonomi kurmayan kaybeder

                    break;
            }

            return score;
        }
    }
}