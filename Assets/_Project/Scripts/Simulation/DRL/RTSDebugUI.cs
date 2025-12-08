using UnityEngine;
using RTS.Simulation.Systems;
using RTS.Simulation.Data;
using System.Linq;
using System.Reflection; // Reflection için gerekli

public class RTSDebugUI : MonoBehaviour
{
    [HideInInspector]
    public SimWorldState World;
    public DRLSimRunner Runner;

    // GUI Stilleri (Önbellek)
    private GUIStyle _headerStyle;
    private GUIStyle _textStyle;
    private bool _stylesInitialized = false;

    private void Start()
    {
        // Runner'ı bulmaya çalış
        if (Runner == null)
            Runner = FindObjectOfType<DRLSimRunner>();
    }

    private void InitStyles()
    {
        // Başlık Stili (Büyük ve Kalın)
        _headerStyle = new GUIStyle(GUI.skin.label);
        _headerStyle.fontSize = 24; // <-- OKUNABİLİRLİK İÇİN BÜYÜTÜLDÜ
        _headerStyle.fontStyle = FontStyle.Bold;
        _headerStyle.alignment = TextAnchor.MiddleCenter;
        _headerStyle.normal.textColor = Color.cyan;

        // Normal Metin Stili
        _textStyle = new GUIStyle(GUI.skin.label);
        _textStyle.fontSize = 20; // <-- OKUNABİLİRLİK İÇİN BÜYÜTÜLDÜ
        _textStyle.fontStyle = FontStyle.Normal;
        _textStyle.normal.textColor = Color.white;

        _stylesInitialized = true;
    }

    private void OnGUI()
    {
        if (!_stylesInitialized) InitStyles();

        // 1. Runner Kontrolü
        if (Runner == null)
        {
            Runner = FindObjectOfType<DRLSimRunner>();
            if (Runner == null)
            {
                GUI.Box(new Rect(10, 10, 400, 100), "HATA");
                GUI.Label(new Rect(20, 40, 380, 50), "DRLSimRunner Bulunamadı!", _textStyle);
                return;
            }
        }

        // 2. World Referansını Sürekli Güncelle (Çünkü ResetSimulation yeni bir World yaratır)
        // Reflection ile private _world değişkenini çekiyoruz
        FieldInfo field = typeof(DRLSimRunner).GetField("_world", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null)
        {
            World = (SimWorldState)field.GetValue(Runner);
        }

        // 3. Veri Kontrolü
        if (World == null || World.Players == null || !World.Players.ContainsKey(1))
        {
            GUI.Box(new Rect(10, 10, 400, 150), "BEKLİYOR");
            GUI.Label(new Rect(20, 50, 360, 100), "⚠️ Simülasyon Verisi Yok.\nReset bekleniyor...", _textStyle);
            return;
        }

        // --- VERİLERİ ÇEK VE GÖSTER ---
        var player = World.Players[1];

        // Sayımlar
        int totalWorkers = World.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Worker);
        int idleWorkers = World.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Worker && u.State == SimTaskType.Idle);
        int soldierCount = World.Units.Values.Count(u => u.PlayerID == 1 && u.UnitType == SimUnitType.Soldier);

        int totalBuildings = World.Buildings.Values.Count(b => b.PlayerID == 1);
        int constructedBuildings = World.Buildings.Values.Count(b => b.PlayerID == 1 && b.IsConstructed);
        int underConstruction = totalBuildings - constructedBuildings;

        // UI Çizimi (Sol Üst Köşe)
        float boxWidth = 350f;
        float boxHeight = 320f;

        // Arkaplan Kutusu
        GUI.Box(new Rect(10, 10, boxWidth, boxHeight), ""); // Boş kutu, üzerine label koyacağız

        // Başlık
        GUI.Label(new Rect(10, 20, boxWidth, 40), $"DERS: {Runner.CurrentLevel}", _headerStyle);

        string content =
            $"🌲 Odun  : {player.Wood}\n" +
            $"🪨 Taş   : {player.Stone}\n" +
            $"🍖 Et    : {player.Meat} / 600\n" + // Hedefi de görelim
            $"-------------------------\n" +
            $"👷 İşçi  : {totalWorkers} (Boşta: {idleWorkers})\n" +
            $"⚔️ Asker : {soldierCount}\n" +
            $"-------------------------\n" +
            $"🏠 Bina  : {totalBuildings} (İnşaat: {underConstruction})\n" +
            $"📈 Nüfus : {player.CurrentPopulation}/{player.MaxPopulation}";

        GUI.Label(new Rect(25, 60, boxWidth - 20, boxHeight - 60), content, _textStyle);

        // Uyarı Mesajları (En Alta)
        if (totalWorkers == 0)
        {
            GUIStyle warningStyle = new GUIStyle(_textStyle);
            warningStyle.normal.textColor = Color.red;
            GUI.Label(new Rect(25, 270, boxWidth, 40), "❌ İŞÇİ YOK! KAYBEDİYOR.", warningStyle);
        }
        else if (idleWorkers == 0)
        {
            GUIStyle yellowStyle = new GUIStyle(_textStyle);
            yellowStyle.normal.textColor = Color.yellow;
            GUI.Label(new Rect(25, 270, boxWidth, 40), "⚠️ Boşta işçi yok (İnşaat Durdu)", yellowStyle);
        }
    }
}