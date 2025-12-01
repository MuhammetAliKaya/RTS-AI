using UnityEngine;
using System.Collections.Generic;
using System.Linq; // Sum hatası için
using RTS.Simulation.Agents;
using RTS.Simulation.Scenarios;
using RTS.Simulation.RL;
using RTS.Simulation.Data;
using RTS.Simulation.Core; // Context
using System.IO;

namespace RTS.Simulation.Orchestrator
{
    public class ExperimentManager : MonoBehaviour
    {
        public static ExperimentManager Instance;

        // --- ECONOMY RUSH İÇİN GEREKLİ DEĞİŞKENLER (Ger Eklendi) ---
        [Header("Ekonomi Ayarları")]
        [Range(0.01f, 0.5f)] public float ResourceDensity = 0.1f;
        public int ResourceAmountPerNode = 250;

        [Header("Harita")]
        public int MapWidth = 50;
        public int MapHeight = 50;
        public int MapSeed = 12345;
        public bool RandomSeedPerEpisode = true;
        // -----------------------------------------------------------

        [Header("Eğitim Modu")]
        public bool RunInferenceMode = false;
        public string QTableFileName = "qtable_experiment.csv";

        [Header("RL Hiperparametreleri")]
        public int TotalEpisodes = 5000;
        public int MaxStepsPerEpisode = 500;
        [Range(0f, 1f)] public float LearningRate = 0.1f;
        [Range(0f, 1f)] public float DiscountFactor = 0.99f;
        [Range(0f, 1f)] public float Epsilon = 1.0f;
        [Range(0f, 1f)] public float EpsilonMin = 0.01f;
        [Range(0f, 1f)] public float EpsilonDecay = 0.999f;

        [Header("Simülasyon Ayarları")]
        public bool RunFast = true;
        [Range(1, 100)] public int EpisodesPerFrame = 10;
        [Range(1f, 50f)] public float VisualSimulationSpeed = 1.0f;

        [Header("Referanslar")]
        public ScenarioManager ScenarioLoader;

        // UI için Public Veriler
        public int CurrentEpisode { get; private set; } = 0;
        public float WinRate { get; private set; } = 0f;
        public float LastEpisodeReward { get; private set; } = 0f;

        // --- EconomyRushScenario BU DEĞİŞKENİ ARIYORDU: ---
        public int CurrentEpisodeSteps => _stepsInEpisode;

        public IAgentController Agent => _currentAgent;

        // Internal
        private IAgentController _currentAgent;
        private IScenario _currentScenario;
        public SimRLEnvironment Environment { get; private set; }

        private bool _isRunning = false;
        private int _stepsInEpisode = 0;
        private float _currentReward = 0;
        private float _visualTimer = 0f;

        public string MetricsFileName = "training_metrics.csv";
        private List<string> _trainingHistory = new List<string>();
        private Queue<int> _winHistory = new Queue<int>();
        private const int HISTORY_SIZE = 100;

        void Awake()
        {
            Instance = this;
            if (ScenarioLoader == null) ScenarioLoader = GetComponent<ScenarioManager>();
            if (ScenarioLoader == null) ScenarioLoader = gameObject.AddComponent<ScenarioManager>();
        }

        void Start() { StartExperiment(); }

        public void StartExperiment()
        {
            Debug.Log("🧪 RL EĞİTİMİ BAŞLATILIYOR...");

            Environment = new SimRLEnvironment();
            Environment.Reset(MapWidth, MapHeight);

            // Context'e kaydet
            SimGameContext.ActiveWorld = Environment.World;

            // Sadece Economy Rush
            _currentScenario = new EconomyRushScenario();

            SetupRLAgent();

            _trainingHistory.Clear();
            _trainingHistory.Add("Episode,Reward,Steps,WinRate");
            CurrentEpisode = 0;
            _winHistory.Clear();
            _isRunning = true;

            StartNewEpisode();
        }

        // ... (Kalan fonksiyonlar aynı, previous message'daki gibi) ...
        // SetupRLAgent, Update, ProcessStep, StartNewEpisode, EndEpisode...
        // Bunları önceki cevabımdaki gibi kopyalayabilirsin, önemli olan değişkenleri eklemekti.

        private void SetupRLAgent()
        {
            _currentAgent = new QLearningAgentController();
            var qAgent = _currentAgent as QLearningAgentController;
            if (qAgent != null)
            {
                qAgent.SetHyperparameters(LearningRate, DiscountFactor, Epsilon, EpsilonMin, EpsilonDecay);
            }
            _currentAgent.Initialize(Environment);

            if (RunInferenceMode)
            {
                RunFast = false;
                string filePath = Path.Combine(Application.dataPath, QTableFileName);
                _currentAgent.LoadModel(filePath);
            }
        }

        void Update()
        {
            if (!_isRunning) return;

            if (RunFast)
            {
                Time.timeScale = 1f;
                for (int i = 0; i < EpisodesPerFrame; i++)
                {
                    if (CurrentEpisode >= TotalEpisodes) { FinishExperiment(); return; }
                    RunCompleteEpisodeHeadless();
                }
            }
            else
            {
                Time.timeScale = VisualSimulationSpeed;
                _visualTimer += Time.deltaTime;
                float step = RTS.Simulation.Systems.SimConfig.TICK_RATE;

                while (_visualTimer >= step)
                {
                    _visualTimer -= step;
                    if (CurrentEpisode >= TotalEpisodes) { FinishExperiment(); return; }
                    if (IsEpisodeDone()) EndEpisode();
                    else ProcessStep();
                }
            }
        }

        void ProcessStep()
        {
            int state = Environment.GetState();
            int action = _currentAgent.GetAction(state);

            float envReward = Environment.Step(action, 0.1f);
            float scenarioReward = _currentScenario.CalculateReward(Environment.World, 1, action);
            float totalReward = envReward + scenarioReward;

            _currentReward += totalReward;
            _stepsInEpisode++;

            int nextState = Environment.GetState();
            bool done = IsEpisodeDone();

            _currentAgent.Train(state, action, totalReward, nextState, done);
        }

        void StartNewEpisode()
        {
            int currentSeed = RandomSeedPerEpisode ? Random.Range(0, 999999) : MapSeed;
            ScenarioLoader.LoadScenario(_currentScenario, Environment.World, currentSeed);
            _currentReward = 0;
            _stepsInEpisode = 0;
        }

        bool IsEpisodeDone()
        {
            if (Environment.IsTerminal()) return true;
            if (_currentScenario.CheckWinCondition(Environment.World, 1)) return true;
            if (_stepsInEpisode >= MaxStepsPerEpisode) return true;
            return false;
        }

        void EndEpisode()
        {
            _currentAgent.OnEpisodeEnd();

            bool isWin = _currentScenario.CheckWinCondition(Environment.World, 1);
            if (_winHistory.Count >= HISTORY_SIZE) _winHistory.Dequeue();
            _winHistory.Enqueue(isWin ? 1 : 0);

            int totalWins = (_winHistory.Count > 0) ? _winHistory.Sum() : 0;
            WinRate = (_winHistory.Count > 0) ? (float)totalWins / _winHistory.Count : 0f;

            LastEpisodeReward = _currentReward;
            CurrentEpisode++;

            string logLine = $"{CurrentEpisode},{_currentReward:F2},{_stepsInEpisode},{WinRate:F2}";
            _trainingHistory.Add(logLine);

            if (!RunFast) StartNewEpisode();
        }

        void RunCompleteEpisodeHeadless()
        {
            StartNewEpisode();
            while (!IsEpisodeDone()) ProcessStep();
            EndEpisode();
        }

        void FinishExperiment()
        {
            _isRunning = false;
            Time.timeScale = 1f;
            Debug.Log("🏁 EĞİTİM TAMAMLANDI!");

            string metricsPath = Path.Combine(Application.dataPath, MetricsFileName);
            try
            {
                File.WriteAllLines(metricsPath, _trainingHistory);
                _currentAgent.SaveModel(Path.Combine(Application.dataPath, QTableFileName));
            }
            catch (System.Exception e) { Debug.LogError("Dosya hatası: " + e.Message); }
        }
    }
}