using UnityEngine;
using RTS.Simulation.Systems;
using RTS.Simulation.Data;
using System.Linq;
using System.Reflection; // Reflection için gerekli

public class RTSDebugUI : MonoBehaviour
{
    [HideInInspector]
    public SimWorldState World;

    // Her iki Runner'a da referans tutuyoruz
    public DRLSimRunner Runner; // Eski eğitim ortamı
    public AdversarialTrainerRunner CombatRunner; // Yeni savaş ortamı

    private MonoBehaviour _activeRunner; // Aktif olan Runner'ı tutacak
    private SimWorldState _worldCache; // WorldState'i saklayacak
    private int _playerID = 1;

    // GUI Stilleri (Önbellek)
    private GUIStyle _headerStyle;
    private GUIStyle _textStyle;
    private bool _stylesInitialized = false;

    private void Start()
    {
        // Runner'ı bulmaya çalış
        if (Runner == null) Runner = FindObjectOfType<DRLSimRunner>();
        if (CombatRunner == null) CombatRunner = FindObjectOfType<AdversarialTrainerRunner>();
    }

    private void InitStyles()
    {
        // Başlık Stili (24pt)
        _headerStyle = new GUIStyle(GUI.skin.label);
        _headerStyle.fontSize = 24;
        _headerStyle.fontStyle = FontStyle.Bold;
        _headerStyle.alignment = TextAnchor.MiddleCenter;
        _headerStyle.normal.textColor = Color.cyan;

        // Normal Metin Stili (20pt)
        _textStyle = new GUIStyle(GUI.skin.label);
        _textStyle.fontSize = 20;
        _textStyle.fontStyle = FontStyle.Normal;
        _textStyle.normal.textColor = Color.white;

        _stylesInitialized = true;
    }

    private bool TryUpdateRunnerAndWorld()
    {
        // 1. Aktif Runner'ı Belirle
        if (CombatRunner != null)
        {
            _activeRunner = CombatRunner;
        }
        else if (Runner != null)
        {
            _activeRunner = Runner;
        }
        else
        {
            return false; // Hiçbir runner yok
        }

        // 2. World Referansını Çek
        string fieldName = "_world";
        FieldInfo field = _activeRunner.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);

        if (field != null)
        {
            _worldCache = (SimWorldState)field.GetValue(_activeRunner);
            World = _worldCache; // Public World'ü de set edelim
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
            GUI.Label(new Rect(20, 40, 380, 50), "Runner Bulunamadı!", _textStyle);
            return;
        }

        if (World == null || World.Players == null || !World.Players.ContainsKey(_playerID))
        {
            GUI.Box(new Rect(10, 10, 400, 150), "BEKLİYOR");
            GUI.Label(new Rect(20, 50, 360, 100), "⚠️ Simülasyon Verisi Yok.\nWorld State Boş...", _textStyle);
            return;
        }

        // --- GÖRÜNTÜLENECEK VERİLERİ ÇEK ---
        var player = World.Players[_playerID];
        bool isCombatMode = _activeRunner is AdversarialTrainerRunner;

        // Player 1 Verileri
        int totalWorkers = World.Units.Values.Count(u => u.PlayerID == _playerID && u.UnitType == SimUnitType.Worker);
        int idleWorkers = World.Units.Values.Count(u => u.PlayerID == _playerID && u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle);
        int soldierCount = World.Units.Values.Count(u => u.PlayerID == _playerID && u.UnitType == SimUnitType.Soldier);
        int totalBuildings = World.Buildings.Values.Count(b => b.PlayerID == _playerID);
        int underConstruction = World.Buildings.Values.Count(b => b.PlayerID == _playerID && !b.IsConstructed);

        // Player 2 Verileri (Sadece Combat Mode'da)
        int enemyUnits = 0;
        float enemyBaseHealth = 0;
        if (isCombatMode)
        {
            enemyUnits = World.Units.Values.Count(u => u.PlayerID == 2);
            var enemyBase = World.Buildings.Values.FirstOrDefault(b => b.PlayerID == 2 && b.Type == SimBuildingType.Base);
            enemyBaseHealth = enemyBase != null ? enemyBase.Health : 0;
        }

        // --- UI ÇİZİMİ ---
        float boxWidth = 350f;
        float boxHeight = isCombatMode ? 400f : 320f;
        float currentY = 10;

        // Arkaplan Kutusu
        GUI.Box(new Rect(10, currentY, boxWidth, boxHeight), "");

        // Başlık
        string title = isCombatMode ? "⚔️ SAVAŞ MODU" : $"DERS: {Runner.CurrentLevel}";
        GUI.Label(new Rect(10, currentY += 10, boxWidth, 40), title, _headerStyle);
        currentY += 40;

        // Kaynaklar
        string content =
            $"🌲 Odun  : {player.Wood}\n" +
            $"🪨 Taş   : {player.Stone}\n" +
            $"🍖 Et    : {player.Meat}\n" +
            $"-------------------------\n" +
            $"👷 İşçi  : {totalWorkers} (Boşta: {idleWorkers})\n" +
            $"⚔️ Asker : {soldierCount}\n" +
            $"🏠 Bina  : {totalBuildings} (İnşaat: {underConstruction})\n" +
            $"📈 Nüfus : {player.CurrentPopulation}/{player.MaxPopulation}\n";

        if (isCombatMode)
        {
            content += $"-------------------------\n" +
                       $"DÜŞMAN STATÜSÜ (P2):\n" +
                       $"💀 Ünite Sayısı: {enemyUnits}\n" +
                       $"🚩 Üs Canı    : {enemyBaseHealth:F0}"; // Düşman statüsü eklendi
        }


        GUI.Label(new Rect(25, currentY, boxWidth - 20, boxHeight - currentY), content, _textStyle);

        // Uyarı Mesajları (En Alta)
        GUIStyle warningStyle = new GUIStyle(_textStyle);
        warningStyle.normal.textColor = Color.red;

        if (totalWorkers == 0 && totalBuildings > 0)
        {
            GUI.Label(new Rect(25, boxHeight - 40, boxWidth, 40), "❌ İŞÇİ YOK! KAYBEDİYOR.", warningStyle);
        }
        else if (idleWorkers == 0 && totalWorkers > 0 && underConstruction == 0)
        {
            GUIStyle yellowStyle = new GUIStyle(_textStyle);
            yellowStyle.normal.textColor = Color.yellow;
            GUI.Label(new Rect(25, boxHeight - 40, boxWidth, 40), "⚠️ Boşta işçi yok (Üretim yap)", yellowStyle);
        }
        else if (idleWorkers > 0 && isCombatMode)
        {
            GUIStyle greenStyle = new GUIStyle(_textStyle);
            greenStyle.normal.textColor = Color.green;
            GUI.Label(new Rect(25, boxHeight - 40, boxWidth, 40), "✅ İşçiler Boşta! Görev Ver.", greenStyle);
        }
    }
}