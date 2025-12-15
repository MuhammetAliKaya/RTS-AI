using UnityEngine;
using RTS.Simulation.Systems;
using RTS.Simulation.Data;
using System.Linq;
using System.Reflection; // Reflection

public class RTSDebugUI : MonoBehaviour
{
    [HideInInspector]
    public SimWorldState World;

    [Header("Runner Referansları")]
    public DRLSimRunner Runner;               // Klasik Eğitim
    public AdversarialTrainerRunner CombatRunner; // Bot'a Karşı (Adversarial)
    public SelfPlayTrainerRunner SelfPlayRunner;  // Kendi Kendine (Self-Play)

    // Takip Değişkenleri
    private MonoBehaviour _activeRunner;
    private SimWorldState _worldCache;
    private int _playerID = 1;

    // İstatistikler (Win/Lose/Draw)
    private int _winCount = 0;
    private int _lossCount = 0;
    private int _drawCount = 0;
    private bool _isEpisodeFinished = false; // Çifte saymayı engellemek için

    // GUI Stilleri
    private GUIStyle _headerStyle;
    private GUIStyle _textStyle;
    private GUIStyle _statStyle; // Yeni istatistik stili
    private bool _stylesInitialized = false;

    // UI Ayarları
    private float _uiScale = 1.5f; // UI Büyütme Çarpanı

    private void Start()
    {
        // Sahnede hangi Runner varsa onu bulmaya çalış
        if (Runner == null) Runner = FindObjectOfType<DRLSimRunner>();
        if (CombatRunner == null) CombatRunner = FindObjectOfType<AdversarialTrainerRunner>();
        if (SelfPlayRunner == null) SelfPlayRunner = FindObjectOfType<SelfPlayTrainerRunner>();
    }

    private void Update()
    {
        // Skor Takibi Mantığı (Her frame çalışır)
        if (World != null && World.Players != null && !_isEpisodeFinished)
        {
            CheckGameResult();
        }

        // Dünya resetlendiyse (Step sayısı düştüyse), bayrağı indir
        if (_activeRunner != null && World != null && World.TickCount < 5)
        {
            _isEpisodeFinished = false;
        }
    }

    private void CheckGameResult()
    {
        // Basitçe Base varlığını kontrol ediyoruz
        var p1Base = World.Buildings.Values.FirstOrDefault(b => b.PlayerID == 1 && b.Type == SimBuildingType.Base);
        var p2Base = World.Buildings.Values.FirstOrDefault(b => b.PlayerID == 2 && b.Type == SimBuildingType.Base);

        if (p1Base == null && p2Base == null)
        {
            _drawCount++;
            _isEpisodeFinished = true;
        }
        else if (p1Base == null) // P1 Kaybetti
        {
            _lossCount++;
            _isEpisodeFinished = true;
        }
        else if (p2Base == null) // P2 Kaybetti (Biz Kazandık)
        {
            _winCount++;
            _isEpisodeFinished = true;
        }
    }

    private void InitStyles()
    {
        // Başlık Stili (Daha Büyük)
        _headerStyle = new GUIStyle(GUI.skin.label);
        _headerStyle.fontSize = 30; // 24 -> 30
        _headerStyle.fontStyle = FontStyle.Bold;
        _headerStyle.alignment = TextAnchor.MiddleCenter;
        _headerStyle.normal.textColor = Color.cyan;

        // Normal Metin Stili
        _textStyle = new GUIStyle(GUI.skin.label);
        _textStyle.fontSize = 24; // 20 -> 24
        _textStyle.fontStyle = FontStyle.Normal;
        _textStyle.normal.textColor = Color.white;

        // İstatistik Stili (Yeşil/Kırmızı vurgular için)
        _statStyle = new GUIStyle(GUI.skin.label);
        _statStyle.fontSize = 24;
        _statStyle.fontStyle = FontStyle.Bold;
        _statStyle.normal.textColor = Color.yellow;

        _stylesInitialized = true;
    }

    private bool TryUpdateRunnerAndWorld()
    {
        // 1. Aktif Runner'ı Belirle (Öncelik Sırası)
        if (SelfPlayRunner != null && SelfPlayRunner.isActiveAndEnabled)
        {
            _activeRunner = SelfPlayRunner;
        }
        else if (CombatRunner != null && CombatRunner.isActiveAndEnabled)
        {
            _activeRunner = CombatRunner;
        }
        else if (Runner != null && Runner.isActiveAndEnabled)
        {
            _activeRunner = Runner;
        }
        else
        {
            return false;
        }

        // 2. Reflection ile private '_world' değişkenini çek
        // (Çünkü runnerların hepsinde _world private tanımlı)
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

        if (!TryUpdateRunnerAndWorld())
        {
            GUI.Box(new Rect(10, 10, 400, 100), "HATA");
            GUI.Label(new Rect(20, 40, 380, 50), "Aktif Runner Bulunamadı!", _textStyle);
            return;
        }

        if (World == null || World.Players == null || !World.Players.ContainsKey(_playerID))
        {
            GUI.Box(new Rect(10, 10, 400, 150), "BEKLİYOR");
            GUI.Label(new Rect(20, 50, 360, 100), "⏳ Simülasyon Başlatılıyor...", _textStyle);
            return;
        }

        // --- VERİ HAZIRLIĞI ---
        var player = World.Players[_playerID];
        bool isCombatMode = _activeRunner is AdversarialTrainerRunner;
        bool isSelfPlay = _activeRunner is SelfPlayTrainerRunner;

        // Player 1 (Bizim Ajan)
        int totalWorkers = World.Units.Values.Count(u => u.PlayerID == _playerID && u.UnitType == SimUnitType.Worker);
        int idleWorkers = World.Units.Values.Count(u => u.PlayerID == _playerID && u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle);
        int soldierCount = World.Units.Values.Count(u => u.PlayerID == _playerID && u.UnitType == SimUnitType.Soldier);
        int totalBuildings = World.Buildings.Values.Count(b => b.PlayerID == _playerID);
        int underConstruction = World.Buildings.Values.Count(b => b.PlayerID == _playerID && !b.IsConstructed);

        // Player 2 (Rakip)
        int enemyUnits = 0;
        float enemyBaseHealth = 0;

        // Hem SelfPlay hem Combat modunda düşman vardır
        if (isCombatMode || isSelfPlay)
        {
            enemyUnits = World.Units.Values.Count(u => u.PlayerID == 2);
            var enemyBase = World.Buildings.Values.FirstOrDefault(b => b.PlayerID == 2 && b.Type == SimBuildingType.Base);
            enemyBaseHealth = enemyBase != null ? enemyBase.Health : 0;
        }

        // --- UI ÇİZİMİ (DAHA BÜYÜK KUTU) ---
        float boxWidth = 450f;  // 350 -> 450
        float boxHeight = (isCombatMode || isSelfPlay) ? 550f : 400f; // Yükseklik arttı
        float currentY = 10;
        float leftPadding = 25f;

        // Arkaplan
        GUI.Box(new Rect(10, currentY, boxWidth, boxHeight), "");

        // 1. BAŞLIK
        string title = "EĞİTİM MODU";
        if (isSelfPlay) title = "🤖 SELF-PLAY (P1 vs P2)";
        else if (isCombatMode) title = "⚔️ ADVERSARIAL (P1 vs AI)";

        GUI.Label(new Rect(10, currentY += 10, boxWidth, 40), title, _headerStyle);
        currentY += 50;

        // 2. SKOR TABLOSU (WIN/LOSE)
        if (isCombatMode || isSelfPlay)
        {
            string scoreText = $"🏆 W: {_winCount} | 💀 L: {_lossCount} | 🤝 D: {_drawCount}";
            float winRate = (_winCount + _lossCount) > 0 ? (float)_winCount / (_winCount + _lossCount) * 100f : 0f;
            scoreText += $"\nWin Rate: %{winRate:F1}";

            GUI.Label(new Rect(leftPadding, currentY, boxWidth, 60), scoreText, _statStyle);
            currentY += 70;
        }

        // 3. EKONOMİ & DURUM
        string content =
            $"🌲 Odun  : {player.Wood}\n" +
            $"🪨 Taş   : {player.Stone}\n" +
            $"🍖 Et    : {player.Meat}\n" +
            $"-----------------------------\n" +
            $"👷 İşçi  : {totalWorkers} (Boşta: {idleWorkers})\n" +
            $"⚔️ Asker : {soldierCount}\n" +
            $"🏠 Bina  : {totalBuildings} (İnşaat: {underConstruction})\n" +
            $"📈 Nüfus : {player.CurrentPopulation}/{player.MaxPopulation}\n";

        GUI.Label(new Rect(leftPadding, currentY, boxWidth - 20, 250), content, _textStyle);
        currentY += 220; // İçerik kadar aşağı in

        // 4. RAKİP DURUMU
        if (isCombatMode || isSelfPlay)
        {
            string enemyInfo =
                $"-----------------------------\n" +
                $"🔴 RAKİP (PLAYER 2):\n" +
                $"💀 Toplam Ünite: {enemyUnits}\n" +
                $"🚩 Üs Canı     : {enemyBaseHealth:F0}";

            GUI.Label(new Rect(leftPadding, currentY, boxWidth - 20, 150), enemyInfo, _textStyle);
            currentY += 120;
        }

        // 5. UYARILAR (EN ALTTA)
        GUIStyle warningStyle = new GUIStyle(_textStyle);
        warningStyle.normal.textColor = Color.red;
        warningStyle.fontStyle = FontStyle.Bold;

        if (totalWorkers == 0 && totalBuildings > 0)
        {
            GUI.Label(new Rect(leftPadding, boxHeight - 50, boxWidth, 40), "❌ İŞÇİ YOK! KAYBEDİYOR.", warningStyle);
        }
        else if (idleWorkers == 0 && totalWorkers > 0 && underConstruction == 0)
        {
            GUIStyle yellowStyle = new GUIStyle(_textStyle);
            yellowStyle.normal.textColor = Color.yellow;
            GUI.Label(new Rect(leftPadding, boxHeight - 50, boxWidth, 40), "⚠️ Boşta işçi yok!", yellowStyle);
        }
        else if (idleWorkers > 0)
        {
            GUIStyle greenStyle = new GUIStyle(_textStyle);
            greenStyle.normal.textColor = Color.green;
            GUI.Label(new Rect(leftPadding, boxHeight - 50, boxWidth, 40), "✅ İşçiler Emre Hazır.", greenStyle);
        }
    }
}