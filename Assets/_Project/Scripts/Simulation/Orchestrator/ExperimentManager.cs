using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using RTS.Simulation.Agents;
using RTS.Simulation.Scenarios;
using RTS.Simulation.RL;
using RTS.Simulation.Data;

namespace RTS.Simulation.Orchestrator
{
    public class ExperimentManager : MonoBehaviour
    {
        public static ExperimentManager Instance;

        [Header("Model Yükleme (Inference)")]
        public bool RunInferenceMode = false;
        public string QTableFileName = "qtable_experiment.csv";

        [Header("Genel Ayarlar")]
        public int MapSeed = 12345;
        public bool RandomSeedPerEpisode = true;
        public int TotalEpisodes = 5000;

        // --- YENİ EKLENEN: RL HİPERPARAMETRELERİ ---
        [Header("RL Hiperparametreleri")]
        [Range(0f, 1f)] public float LearningRate = 0.1f;       // Alpha
        [Range(0f, 1f)] public float DiscountFactor = 0.99f;    // Gamma
        [Range(0f, 1f)] public float Epsilon = 1.0f;            // Başlangıç Keşfetme
        [Range(0f, 1f)] public float EpsilonMin = 0.01f;        // Min Keşfetme
        [Range(0f, 1f)] public float EpsilonDecay = 0.999f;     // Unutma Hızı
        // -------------------------------------------

        // --- YENİ EKLENEN: ADIM LİMİTİ ---
        [Tooltip("Bir bölüm en fazla kaç adım sürebilir? (Aşarsa başarısız sayılır)")]
        public int MaxStepsPerEpisode = 500;
        // ---------------------------------

        [Header("Hız Ayarları")]
        public bool RunFast = true;
        [Range(1, 500)] public int EpisodesPerFrame = 10;
        [Range(1f, 50f)] public float VisualSimulationSpeed = 1.0f;

        [Header("Harita Ayarları")]
        public int MapWidth = 50;
        public int MapHeight = 50;

        [Header("Kaynak Ayarları")]
        [Range(0.01f, 0.5f)]
        public float ResourceDensity = 0.1f; // %10 Doluluk oranı
        public int ResourceAmountPerNode = 250; // Bir ağaçta kaç odun var?

        [Header("Bağlantılar")]
        public ScenarioManager ScenarioLoader;

        public int CurrentEpisode { get; private set; } = 0;
        public float LastEpisodeReward { get; private set; } = 0;
        public float WinRate { get; private set; } = 0;
        public int LastEpisodeSteps { get; private set; } = 0;

        private Queue<int> _winHistory = new Queue<int>();
        private const int HISTORY_SIZE = 100;

        private IAgentController _currentAgent;
        private IScenario _currentScenario;

        public SimRLEnvironment Environment { get; private set; }
        public SimWorldState World => Environment?.World;

        public IAgentController Agent => _currentAgent;

        private bool _isRunning = false;
        private float _currentEpisodeReward = 0;
        private int _currentEpisodeSteps = 0;
        public int CurrentEpisodeSteps => _currentEpisodeSteps;
        private float _visualTimer = 0f;

        void Awake()
        {
            Instance = this;
            if (ScenarioLoader == null) ScenarioLoader = GetComponent<ScenarioManager>();
            if (ScenarioLoader == null) ScenarioLoader = gameObject.AddComponent<ScenarioManager>();
        }

        void Start()
        {
            StartExperiment();
        }

        public void StartExperiment()
        {
            Debug.Log("🧪 DENEY BAŞLATILIYOR...");

            Environment = new SimRLEnvironment();
            Environment.Reset(MapWidth, MapHeight);

            _currentScenario = new EconomyRushScenario();
            _currentAgent = new QLearningAgentController();

            // --- YENİ: PARAMETRELERİ AJANA AKTAR ---
            // Eğer elimizdeki ajan bir QLearning ajanıysa, ayarları gönder
            if (_currentAgent is QLearningAgentController qAgent)
            {
                qAgent.SetHyperparameters(LearningRate, DiscountFactor, Epsilon, EpsilonMin, EpsilonDecay);
            }
            // --------------------------------------

            _currentAgent.Initialize(Environment);

            // Inference Modu
            if (RunInferenceMode)
            {
                RunFast = false;
                TotalEpisodes = 10;
                string filePath = Application.dataPath + "/" + QTableFileName;
                _currentAgent.LoadModel(filePath);
            }

            CurrentEpisode = 0;
            _winHistory.Clear();
            _isRunning = true;

            StartNewEpisode();
        }

        void Update()
        {
            if (!_isRunning) return;

            // --- HIZLI MOD (EĞİTİM) ---
            if (RunFast)
            {
                Time.timeScale = 1f; // Unity zamanını bozma
                for (int i = 0; i < EpisodesPerFrame; i++)
                {
                    if (CurrentEpisode >= TotalEpisodes) { FinishExperiment(); return; }
                    RunCompleteEpisodeHeadless();
                }
            }
            // --- GÖRSEL MOD (İZLEME) - DÜZELTİLMİŞ ---
            else
            {
                Time.timeScale = VisualSimulationSpeed; // Slider ile Unity zamanını büküyoruz (Örn: 1x, 2x)

                // Simülasyonun kendi 'dt'si (0.1s) dolana kadar bekle
                // Böylece 60 FPS'de de olsa, 144 FPS'de de olsa oyun aynı hızda akar.

                _visualTimer += Time.deltaTime;

                // Simülasyon adımı 0.1s (SimConfig.TICK_RATE) olduğu için,
                // geçen süre 0.1'i aştıkça adım at.
                float simStep = RTS.Simulation.Systems.SimConfig.TICK_RATE;

                while (_visualTimer >= simStep)
                {
                    _visualTimer -= simStep;

                    if (CurrentEpisode >= TotalEpisodes) { FinishExperiment(); return; }
                    StepEpisodeVisual();
                }
            }
        }

        // --- GÜNCELLENEN: GÖRSEL MOD KONTROLÜ ---
        void StepEpisodeVisual()
        {
            // Hem Terminal (Bitti mi?) Hem de MaxStep (Süre doldu mu?) kontrolü
            if (Environment.IsTerminal() || _currentEpisodeSteps >= MaxStepsPerEpisode)
            {
                EndEpisode();
                return;
            }
            ProcessStep();
        }

        // --- GÜNCELLENEN: HIZLI MOD KONTROLÜ ---
        void RunCompleteEpisodeHeadless()
        {
            StartNewEpisode();

            // Döngüde "MaxStepsPerEpisode" değişkenini kullanıyoruz
            while (!Environment.IsTerminal() && _currentEpisodeSteps < MaxStepsPerEpisode)
            {
                ProcessStep();
            }

            EndEpisode();
        }

        void StartNewEpisode()
        {
            int currentSeed = RandomSeedPerEpisode ? Random.Range(0, 999999) : MapSeed;
            ScenarioLoader.LoadScenario(_currentScenario, Environment.World, currentSeed);

            _currentEpisodeReward = 0;
            _currentEpisodeSteps = 0;
        }

        void ProcessStep()
        {
            int state = Environment.GetState();
            int action = _currentAgent.GetAction(state);

            // 1. FİZİKSEL ÖDÜL (Hareket geçerli mi? Kaynak var mı?)
            float envReward = Environment.Step(action, 0.1f);

            // 2. STRATEJİK ÖDÜL (Kışla bitti mi? Hedefe ulaştık mı?)
            float scenarioReward = _currentScenario.CalculateReward(Environment.World, 1, action);

            // --- TOPLAM ÖDÜL ---
            float totalReward = envReward + scenarioReward;

            _currentEpisodeReward += totalReward;
            _currentEpisodeSteps++;

            int nextState = Environment.GetState();
            bool done = Environment.IsTerminal();

            if (_currentScenario.CheckWinCondition(Environment.World, 1)) done = true;
            if (_currentEpisodeSteps >= MaxStepsPerEpisode) done = true;

            // Ajanı toplam ödülle eğit
            _currentAgent.Train(state, action, totalReward, nextState, done);
        }

        void EndEpisode()
        {
            _currentAgent.OnEpisodeEnd();

            // Kazanıp kazanmadığını kontrol et (Süre bitip bitmediğine değil)
            bool isWin = _currentScenario.CheckWinCondition(Environment.World, 1);

            if (_winHistory.Count >= HISTORY_SIZE) _winHistory.Dequeue();
            _winHistory.Enqueue(isWin ? 1 : 0);

            WinRate = (float)_winHistory.Sum() / _winHistory.Count;
            LastEpisodeReward = _currentEpisodeReward;
            LastEpisodeSteps = _currentEpisodeSteps;

            CurrentEpisode++;

            if (!RunFast) StartNewEpisode();
        }

        void FinishExperiment()
        {
            _isRunning = false;
            Time.timeScale = 1f;
            Debug.Log("🏁 DENEY TAMAMLANDI!");
            _currentAgent.SaveModel(Application.dataPath + "/qtable_experiment.csv");
        }
    }
}