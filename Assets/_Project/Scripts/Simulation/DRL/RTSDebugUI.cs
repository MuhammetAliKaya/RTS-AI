using UnityEngine;
using RTS.Simulation.Systems;
using RTS.Simulation.Data;
using RTS.Simulation.Orchestrator; // PSOManager için gerekli
using RTS.Simulation.Core; // SimGameContext için gerekli
using System.Linq;
using System.Reflection;

public class RTSDebugUI : MonoBehaviour
{
    // --- STATİK LOG DEĞİŞKENLERİ ---
    public static string AI_LogString = "AI Bekleniyor...";
    public static string AI_QueueStatus = "";
    public static string AI_GSF_Log = "GSF: Hesaplanıyor...";
    public static string AI_Fitness_Log = "Fitness: Maç Sonu...";
    // -------------------------------

    [HideInInspector]
    public SimWorldState World;

    [Header("Runner Referansları")]
    public PSOManager PsoRunner;              // <--- YENİ: PSO Yöneticisi
    public BenchmarkRunner BenchRunner;
    public DRLSimRunner Runner;
    public AdversarialTrainerRunner CombatRunner;
    public SelfPlayTrainerRunner SelfPlayRunner;

    private MonoBehaviour _activeRunner;
    private SimWorldState _worldCache;
    private int _playerID = 1;

    // İstatistikler
    private int _winCount = 0;
    private int _lossCount = 0;
    private int _drawCount = 0;
    private bool _isEpisodeFinished = false;

    // Stiller
    private GUIStyle _headerStyle, _textStyle, _statStyle, _logStyle, _queueStyle, _gsfStyle;
    private bool _stylesInitialized = false;

    private void Start()
    {
        // Sahnedeki aktif runner'ı bul
        if (PsoRunner == null) PsoRunner = FindObjectOfType<PSOManager>(); // <--- YENİ
        if (BenchRunner == null) BenchRunner = FindObjectOfType<BenchmarkRunner>();
        if (Runner == null) Runner = FindObjectOfType<DRLSimRunner>();
        if (CombatRunner == null) CombatRunner = FindObjectOfType<AdversarialTrainerRunner>();
        if (SelfPlayRunner == null) SelfPlayRunner = FindObjectOfType<SelfPlayTrainerRunner>();
    }

    private void Update()
    {
        if (World != null && World.Players != null && !_isEpisodeFinished)
        {
            CheckGameResult();
        }

        // Oyun resetlendiyse skoru saymayı bekle
        if (World != null && World.TickCount < 5)
        {
            _isEpisodeFinished = false;
        }
    }

    private void CheckGameResult()
    {
        var p1Base = World.Buildings.Values.FirstOrDefault(b => b.PlayerID == 1 && b.Type == SimBuildingType.Base);
        var p2Base = World.Buildings.Values.FirstOrDefault(b => b.PlayerID == 2 && b.Type == SimBuildingType.Base);

        if (p1Base == null && p2Base == null) { _drawCount++; _isEpisodeFinished = true; }
        else if (p1Base == null) { _lossCount++; _isEpisodeFinished = true; }
        else if (p2Base == null) { _winCount++; _isEpisodeFinished = true; }
    }

    private void InitStyles()
    {
        _headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        _headerStyle.normal.textColor = Color.cyan;

        _textStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Normal };
        _textStyle.normal.textColor = Color.white;

        _statStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
        _statStyle.normal.textColor = Color.yellow;

        _logStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
        _logStyle.normal.textColor = new Color(1f, 0.5f, 1f); // Magenta

        _queueStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Normal };
        _queueStyle.normal.textColor = new Color(1f, 0.6f, 0f); // Turuncu

        _gsfStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
        _gsfStyle.normal.textColor = new Color(0.6f, 0.8f, 1f); // Açık Mavi

        _stylesInitialized = true;
    }

    private bool TryUpdateRunnerAndWorld()
    {
        // 1. Aktif Runner'ı Belirle (Öncelik Sırası)
        if (PsoRunner != null && PsoRunner.isActiveAndEnabled) _activeRunner = PsoRunner; // <--- YENİ
        else if (BenchRunner != null && BenchRunner.isActiveAndEnabled) _activeRunner = BenchRunner;
        else if (SelfPlayRunner != null && SelfPlayRunner.isActiveAndEnabled) _activeRunner = SelfPlayRunner;
        else if (CombatRunner != null && CombatRunner.isActiveAndEnabled) _activeRunner = CombatRunner;
        else if (Runner != null && Runner.isActiveAndEnabled) _activeRunner = Runner;
        else return false;

        // 2. World Verisini Çek
        // PSOManager özel durum: _world değişkeni yok, SimGameContext kullanıyor.
        if (_activeRunner is PSOManager)
        {
            World = SimGameContext.ActiveWorld;
            return World != null;
        }

        // Diğerleri için Reflection ile private '_world' değişkenini çek
        string fieldName = "_world";
        FieldInfo field = _activeRunner.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);

        if (field != null)
        {
            _worldCache = (SimWorldState)field.GetValue(_activeRunner);
            World = _worldCache;
            return _worldCache != null;
        }
        return false;
    }

    private void OnGUI()
    {
        if (!_stylesInitialized) InitStyles();

        // Runner yoksa veya World yüklenmediyse bekle
        if (!TryUpdateRunnerAndWorld())
        {
            GUI.Box(new Rect(10, 10, 400, 100), "BEKLİYOR...");
            GUI.Label(new Rect(20, 40, 380, 50), "Aktif Simülasyon Aranıyor...", _textStyle);
            return;
        }

        if (World == null || World.Players == null || !World.Players.ContainsKey(_playerID)) return;

        var player = World.Players[_playerID];

        // Mod Kontrolü
        bool isPso = _activeRunner is PSOManager;
        bool isBenchmark = _activeRunner is BenchmarkRunner;
        bool isCombatMode = _activeRunner is AdversarialTrainerRunner;
        bool isSelfPlay = _activeRunner is SelfPlayTrainerRunner;

        int totalWorkers = World.Units.Values.Count(u => u.PlayerID == _playerID && u.UnitType == SimUnitType.Worker);
        int idleWorkers = World.Units.Values.Count(u => u.PlayerID == _playerID && u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle);
        int soldierCount = World.Units.Values.Count(u => u.PlayerID == _playerID && u.UnitType == SimUnitType.Soldier);
        int totalBuildings = World.Buildings.Values.Count(b => b.PlayerID == _playerID);

        // --- KUTU ÇİZİMİ ---
        float boxWidth = 550f;
        float boxHeight = 750f;
        float currentY = 10;
        float leftPadding = 25f;

        GUI.Box(new Rect(10, currentY, boxWidth, boxHeight), "");

        string title = "EĞİTİM MODU";
        if (isPso) title = "🧬 PSO TRAINING";
        else if (isBenchmark) title = "📊 BENCHMARK";
        else if (isSelfPlay) title = "🤖 SELF-PLAY";
        else if (isCombatMode) title = "⚔️ ADVERSARIAL";

        GUI.Label(new Rect(10, currentY += 10, boxWidth, 40), title, _headerStyle);
        currentY += 40;

        if (isCombatMode || isSelfPlay || isBenchmark || isPso)
        {
            string scoreText = $"🏆 W: {_winCount} | 💀 L: {_lossCount} | 🤝 D: {_drawCount}";
            GUI.Label(new Rect(leftPadding, currentY, boxWidth, 40), scoreText, _statStyle);
            currentY += 40;
        }

        string content =
            $"🌲 Odun: {player.Wood}  🪨 Taş: {player.Stone}  🍖 Et: {player.Meat}\n" +
            $"-----------------------------\n" +
            $"👷 İşçi: {totalWorkers} ({idleWorkers} Boş)  ⚔️ Asker: {soldierCount}  🏠 Bina: {totalBuildings}\n" +
            $"📈 Nüfus: {player.CurrentPopulation}/{player.MaxPopulation}\n";

        GUI.Label(new Rect(leftPadding, currentY, boxWidth - 20, 100), content, _textStyle);
        currentY += 100;

        // --- YENİ LOGLAR ---

        // 1. KARAR (Mor)
        GUI.Label(new Rect(leftPadding, currentY, boxWidth - 40, 30), $"🧠 KARAR: {AI_LogString}", _logStyle);
        currentY += 30;

        // 2. GSF DURUMU (Mavi)
        GUI.Label(new Rect(leftPadding, currentY, boxWidth - 40, 30), $"📊 DURUM: {AI_GSF_Log}", _gsfStyle);
        currentY += 30;

        // 3. KUYRUK (Turuncu)
        GUI.Label(new Rect(leftPadding, currentY, boxWidth - 40, 60), $"🔄 AKIŞ:\n{AI_QueueStatus}", _queueStyle);
        currentY += 60;

        // 4. FITNESS (Sarı - En Altta)
        GUI.Label(new Rect(leftPadding, currentY, boxWidth - 40, 200), $"⭐ PUAN:\n{AI_Fitness_Log}", _queueStyle);
    }
}