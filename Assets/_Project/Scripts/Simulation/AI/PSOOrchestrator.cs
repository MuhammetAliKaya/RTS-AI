// using UnityEngine;
// using System.Collections.Generic;
// using RTS.Simulation.Data;
// using RTS.Simulation.Systems;
// using RTS.Simulation.Scenarios;
// using RTS.Simulation.AI;
// using RTS.Simulation.Core; // SimGameContext için
// using System.Linq;

// namespace RTS.Simulation.Orchestrator
// {
//     public class PSOOrchestrator : MonoBehaviour
//     {
//         public enum OrchestratorState
//         {
//             Idle,
//             Training,
//             Visualizing // İzleme Modu
//         }

//         [Header("Controls")]
//         public bool StartTraining = false;
//         public bool VisualizeBestBetweenGens = false; // Her jenerasyon sonu en iyiyi izlet
//         [Range(1f, 50f)] public float VisualTimeScale = 1.0f; // Görsel mod hızı

//         [Header("PSO Settings")]
//         public int PopulationSize = 20;
//         public int MaxGenerations = 50;

//         // [WorkerLimit, SoldierLimit, AttackThreshold, BarracksPriority]
//         public int Dimensions = 4;

//         // ÖNEMLİ: İşçi sayısı en az 1 olsun ki AI hiç üretmemeyi seçmesin!
//         public float MinGeneVal = 1f;
//         public float MaxGeneVal = 20f;

//         [Header("Simulation Settings")]
//         public int MapWidth = 50;
//         public int MapHeight = 50;
//         public int MaxStepsPerGame = 1000;

//         // Eğitim sırasında görselleri oluşturmayız, referans gerekmez
//         // Ancak görsel mod için GameVisualizer veya benzeri bir yapı gerekebilir.

//         private PSOAlgorithm _pso;
//         private IScenario _scenario;

//         private int _currentGeneration = 0;
//         private OrchestratorState _state = OrchestratorState.Idle;

//         // Görsel Mod Değişkenleri
//         private SimWorldState _visualWorld;
//         private ParametricMacroAI _visualAI;
//         private float _visualTimer = 0f;
//         private bool _visualMatchFinished = false;

//         // İstatistikler
//         public float CurrentBestFitness = 0;
//         public float[] CurrentBestGenes;

//         void Start()
//         {
//             _scenario = new EconomyRushScenario();
//         }

//         void Update()
//         {
//             // Başlatma Tetikleyicisi
//             if (StartTraining && _state == OrchestratorState.Idle)
//             {
//                 StartTraining = false;
//                 StartPSO();
//             }

//             if (_state == OrchestratorState.Training)
//             {
//                 // Headless Eğitim Döngüsü
//                 RunGeneration();

//                 // Eğitim bitti mi?
//                 if (_currentGeneration >= MaxGenerations)
//                 {
//                     _state = OrchestratorState.Idle;
//                     Debug.Log("🏁 EĞİTİM BİTTİ!");
//                     LogBestResult();
//                 }
//                 // Görsel izleme isteniyor mu?
//                 else if (VisualizeBestBetweenGens)
//                 {
//                     Debug.Log("👀 Görsel Mod: En iyi ajan izleniyor...");
//                     StartVisualMatch(_pso.GlobalBestPosition);
//                 }
//             }
//             else if (_state == OrchestratorState.Visualizing)
//             {
//                 // Görsel Mod Döngüsü (Update hızında akar)
//                 UpdateVisualMatch();
//             }
//         }

//         public void StartPSO()
//         {
//             Debug.Log("🧬 PSO Başlatılıyor...");
//             _pso = new PSOAlgorithm(PopulationSize, Dimensions, MinGeneVal, MaxGeneVal);
//             _currentGeneration = 0;
//             _state = OrchestratorState.Training;
//         }

//         // --- HEADLESS TRAINING LOOP ---
//         private void RunGeneration()
//         {
//             var positions = _pso.GetPositions();

//             // Tüm popülasyonu döngüye sok
//             for (int i = 0; i < positions.Count; i++)
//             {
//                 float[] genes = positions[i];
//                 float fitness = EvaluateGenomeHeadless(genes);
//                 _pso.ReportFitness(i, fitness);
//             }

//             _pso.Step();

//             CurrentBestFitness = _pso.GlobalBestFitness;
//             CurrentBestGenes = _pso.GlobalBestPosition;

//             _currentGeneration++;
//             Debug.Log($"Gen {_currentGeneration} | Best: {CurrentBestFitness:F2}");
//         }

//         private float EvaluateGenomeHeadless(float[] genes)
//         {
//             SimWorldState world = new SimWorldState(MapWidth, MapHeight);
//             int seed = UnityEngine.Random.Range(0, 100000);
//             _scenario.SetupMap(world, seed);

//             ParametricMacroAI ai = new ParametricMacroAI(world, 1, genes);

//             int step = 0;
//             bool isDone = false;
//             float dt = SimConfig.TICK_RATE;

//             while (!isDone && step < MaxStepsPerGame)
//             {
//                 SimBuildingSystem.UpdateAllBuildings(world, dt);
//                 var units = world.Units.Values.ToList();
//                 foreach (var unit in units) SimUnitSystem.UpdateUnit(unit, world, dt);

//                 ai.Update(dt);

//                 if (_scenario.CheckWinCondition(world, 1)) isDone = true;
//                 step++;
//             }

//             return CalculateFitness(world, step, isDone);
//         }

//         // --- VISUALIZATION LOOP (YENİ EKLENDİ) ---
//         private void StartVisualMatch(float[] genes)
//         {
//             _state = OrchestratorState.Visualizing;
//             Time.timeScale = VisualTimeScale;

//             // 1. Görsel dünyayı oluştur
//             _visualWorld = new SimWorldState(MapWidth, MapHeight);
//             SimGameContext.ActiveWorld = _visualWorld; // Global erişim için (UI vb. varsa)

//             // 2. Haritayı kur (Burası GameVisualizer'ı tetiklemeli)
//             // Not: Mevcut yapınızda GameVisualizer Update içinde ActiveWorld'ü dinliyorsa otomatik çalışır.
//             // Değilse, burada map oluşumunu görselleştiren bir kod gerekebilir.
//             int seed = 12345; // Sabit seed ile izleyelim ki performans farkı net olsun
//             _scenario.SetupMap(_visualWorld, seed);

//             // 3. AI
//             _visualAI = new ParametricMacroAI(_visualWorld, 1, genes);

//             _visualMatchFinished = false;

//             // Eğer sahnede GameVisualizer varsa resetleyelim (Varsayım)
//             var visualizer = FindObjectOfType<GameVisualizer>();
//             if (visualizer != null)
//             {
//                 // Visualizer'ın Init veya Reset fonksiyonu varsa çağırılmalı
//                 // visualizer.Init(_visualWorld); 
//                 Debug.Log("🎥 Visualizer Bulundu, İzleme Başlıyor.");
//             }
//         }

//         private void UpdateVisualMatch()
//         {
//             float dt = Time.deltaTime;

//             SimBuildingSystem.UpdateAllBuildings(_visualWorld, dt);
//             var units = _visualWorld.Units.Values.ToList();
//             foreach (var unit in units) SimUnitSystem.UpdateUnit(unit, _visualWorld, dt);

//             _visualAI.Update(dt);

//             if (_scenario.CheckWinCondition(_visualWorld, 1) || Input.GetKeyDown(KeyCode.Space)) // Space ile geçebil
//             {
//                 EndVisualMatch();
//             }
//         }

//         private void EndVisualMatch()
//         {
//             Debug.Log("⏹️ Görsel Maç Bitti. Eğitime devam ediliyor...");
//             Time.timeScale = 1.0f; // Hızı sıfırla
//             _state = OrchestratorState.Training;

//             // İsterseniz burada VisualizeBestBetweenGens = false yapıp her seferinde sormasını sağlayabilirsiniz
//             // VisualizeBestBetweenGens = false; 
//         }

//         // --- FITNESS FUNCTION (GÜNCELLENDİ) ---
//         private float CalculateFitness(SimWorldState world, int steps, bool won)
//         {
//             // Kaynaklara daha yüksek puan verelim ki "hiçbir şey yapmama"yı yensin
//             float resourceScore = SimResourceSystem.GetResourceAmount(world, 1, SimResourceType.Wood) * 1.0f +
//                                   SimResourceSystem.GetResourceAmount(world, 1, SimResourceType.Stone) * 1.5f +
//                                   SimResourceSystem.GetResourceAmount(world, 1, SimResourceType.Meat) * 2.0f;

//             int soldierCount = world.Units.Values.Count(u => u.UnitType == SimUnitType.Soldier && u.PlayerID == 1);
//             int workerCount = world.Units.Values.Count(u => u.UnitType == SimUnitType.Worker && u.PlayerID == 1); // İşçi de puan versin

//             // Kazanma bonusu
//             float winBonus = won ? (20000f - steps * 2) : 0;

//             // CEZA: Hiç işçisi yoksa puanı sıfırla veya eksi puan ver!
//             if (workerCount == 0) return 0f;

//             return resourceScore + (workerCount * 50) + (soldierCount * 100) + winBonus;
//         }

//         private void LogBestResult()
//         {
//             Debug.Log($"🏆 EN İYİ SKOR: {CurrentBestFitness}");
//             Debug.Log($"🧬 EN İYİ GENLER: [{string.Join(", ", CurrentBestGenes)}]");
//         }
//     }
// }+



//{ 0.00f, 28.94f, 0.00f, 0.00f, 40.00f, 40.00f, 4.82f, 40.00f, 18.56f, 14.31f, 2.56f, 34.90f, 0.00f, 5.83f }
// { 5.14f, 0.00f, 5.01f, 0.00f, 33.32f, 40.00f, 3.13f, 21.28f, 40.00f, 16.66f, 0.00f, 40.00f, 28.41f, 0.00f };