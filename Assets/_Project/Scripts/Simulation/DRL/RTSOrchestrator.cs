using UnityEngine;
using RTS.Simulation.Core;
using RTS.Simulation.Systems;
using RTS.Simulation.Data;

public class RTSOrchestrator : MonoBehaviour
{
    public static RTSOrchestrator Instance;

    [Header("Sub-Agents")]
    public UnitSelectionAgent UnitAgent;
    public ActionSelectionAgent ActionAgent;
    public TargetSelectionAgent TargetAgent;

    // Sistem Referansları
    private SimWorldState _world;
    private SimGridSystem _gridSystem;
    private SimUnitSystem _unitSystem;
    private SimBuildingSystem _buildingSystem;

    public DRLActionTranslator Translator;
    public int MyPlayerID = 1;

    // --- CONTEXT (Agentlar buradan okur) ---
    // Bu değerler anlık olarak değişecek, agentlar gözlem yaparken bunları okuyacak.
    [HideInInspector] public int SelectedSourceIndex = -1;
    [HideInInspector] public int SelectedActionType = 0;

    // --- DEMO BUFFER (İnsan Kararları Burada Bekler) ---
    private int _tempSourceIndex = -1;
    private int _tempActionType = 0;

    // Durum Makinesi
    public enum OrchestratorState { Idle, WaitingUnit, WaitingAction, WaitingTarget }
    private OrchestratorState _state = OrchestratorState.Idle;
    public OrchestratorState CurrentState => _state;

    private AdversarialTrainerRunner _runner;

    [Header("Demo Recording")]
    public bool IsHumanDemoMode = true; // Editörden aç!

    private void Awake()
    {
        Instance = this;
    }

    public void Setup(SimWorldState world, SimGridSystem gridSys, SimUnitSystem unitSys, SimBuildingSystem buildSys, AdversarialTrainerRunner runner)
    {
        _state = OrchestratorState.Idle;
        _world = world;
        _gridSystem = gridSys;
        _unitSystem = unitSys;
        _buildingSystem = buildSys;
        _runner = runner;

        Translator = new DRLActionTranslator(_world, _unitSystem, _buildingSystem, _gridSystem, MyPlayerID);

        UnitAgent.Setup(this, _world, Translator);
        ActionAgent.Setup(this, _world, Translator);
        TargetAgent.Setup(this, _world, Translator, _gridSystem, _runner);
    }

    // =================================================================================
    //  İNSAN GİRDİSİ YÖNETİMİ (BUFFERING)
    // =================================================================================

    // ADIM 1: Ünite Seçildi (Sadece hafızaya at)
    public void UserSelectUnit(int unitGridIndex)
    {
        if (!IsHumanDemoMode) return;

        // Yeni bir ünite seçildiyse önceki buffer'ı ez ve süreci başlat
        _tempSourceIndex = unitGridIndex;
        _tempActionType = 0; // Aksiyonu sıfırla

        _state = OrchestratorState.WaitingAction;

        // Görsel hata ayıklama
        // Debug.Log($"[Buffer] Ünite Hazır: {unitGridIndex}");
    }

    // ADIM 2: Aksiyon Seçildi (Sadece hafızaya at)
    public void UserSelectAction(int actionType)
    {
        if (!IsHumanDemoMode) return;

        // Eğer ünite seçmeden aksiyona basıldıysa (Hata koruması)
        if (_tempSourceIndex == -1)
        {
            Debug.LogWarning("Önce ünite seçmelisin!");
            return;
        }

        _tempActionType = actionType;
        _state = OrchestratorState.WaitingTarget;

        // Debug.Log($"[Buffer] Aksiyon Hazır: {actionType}");
    }

    // ADIM 3: Hedef Seçildi -> KOMPLE KAYIT (COMMIT)
    public void UserSelectTarget(int targetGridIndex)
    {
        if (!IsHumanDemoMode) return;

        if (_tempSourceIndex == -1 || _tempActionType == 0)
        {
            // Eğer ünite veya aksiyon eksikse, belki de sadece yürüme emridir (Smart Context).
            // SimInputManager zaten aksiyonu Move(11) veya Attack(10) olarak göndermeliydi.
            // Eğer hala eksikse işlem iptal.
            if (_tempSourceIndex != -1 && _tempActionType == 0)
            {
                // Varsayılan bir aksiyon ataması yapılabilir ama InputManager halletmeli.
                Debug.LogWarning("Eksik Aksiyon!");
                return;
            }
            return;
        }

        // --- ZİNCİRLEME KAYIT BAŞLIYOR ---
        // Buradaki hile şudur: Agentlara "karar ver" demeden önce ortam değişkenlerini (Context)
        // manuel olarak değiştiriyoruz ki Agent doğru gözlemi yapsın.

        // 1. UNIT AGENT KAYDI
        // Unit Agent karar verirken henüz kimse seçili değildir.
        SelectedSourceIndex = -1;
        SelectedActionType = 0;
        // Kaydı tetikle (Source: _tempSourceIndex)
        UnitAgent.RegisterExternalAction(0, _tempSourceIndex, 0);

        // 2. ACTION AGENT KAYDI
        // Action Agent karar verirken Ünite seçili olmalıdır.
        SelectedSourceIndex = _tempSourceIndex; // Context'i güncelle
        // Kaydı tetikle (Action: _tempActionType)
        ActionAgent.RegisterExternalAction(_tempActionType, _tempSourceIndex, 0);

        // 3. TARGET AGENT KAYDI
        // Target Agent karar verirken Ünite ve Aksiyon seçili olmalıdır.
        SelectedActionType = _tempActionType; // Context'i güncelle
        // Kaydı tetikle (Target: targetGridIndex)
        TargetAgent.RegisterExternalAction(_tempActionType, _tempSourceIndex, targetGridIndex);

        // 4. İŞLEMİ GERÇEKLEŞTİR
        bool success = Translator.ExecuteAction(_tempActionType, _tempSourceIndex, targetGridIndex);

        if (success) Debug.Log("[Demo] Zincir Başarıyla Kaydedildi ve Uygulandı.");

        // 5. TEMİZLİK (İsteğe bağlı, seri tıklama için temizlemeyebilirsin ama temizlemek güvenlidir)
        // _tempSourceIndex = -1; // Yorum satırı: Seri emir vermek için seçimi koruyabiliriz.
        _tempActionType = 0;
        _state = OrchestratorState.WaitingAction; // Tekrar aksiyon beklemeye dön (veya Idle)
    }

    // =================================================================================
    //  AI OTOMATİK OYUN DÖNGÜSÜ (EĞİTİM / INFERENCE)
    // =================================================================================

    public void RequestFullDecision()
    {
        if (IsHumanDemoMode) return; // İnsan modundaysak AI karışmasın

        _state = OrchestratorState.WaitingUnit;
        SelectedSourceIndex = -1;
        SelectedActionType = 0;
        UnitAgent.RequestDecision();
    }

    public void OnUnitSelected(int sourceIndex)
    {
        SelectedSourceIndex = sourceIndex;
        _state = OrchestratorState.WaitingAction;
        // Eğer Demo Modu değilse devam et
        if (!IsHumanDemoMode) ActionAgent.RequestDecision();
    }

    public void OnActionSelected(int actionType)
    {
        SelectedActionType = actionType;
        _state = OrchestratorState.WaitingTarget;
        // Eğer Demo Modu değilse devam et
        if (!IsHumanDemoMode) TargetAgent.RequestDecision();
    }

    public void OnTargetSelected(int targetIndex)
    {
        _state = OrchestratorState.Idle;
        bool success = Translator.ExecuteAction(SelectedActionType, SelectedSourceIndex, targetIndex);

        float stepReward = success ? 0.01f : -0.02f;
        AddGroupReward(stepReward);
    }

    // --- YARDIMCI METOTLAR ---
    // RTSOrchestrator.cs sınıfının içine ekle:

    public void RecordHumanDemonstration(int sourceIndex, int actionType, int targetIndex)
    {
        // 1. Unit Selection Ajanını Tetikle
        // "Ben insan olarak BU kaynağı (sourceIndex) seçtim, bunu öğren" der.
        if (UnitAgent != null)
        {
            // Not: UnitAgent scriptine RegisterExternalAction fonksiyonu eklediğini varsayıyorum
            // (Verdiğin dosyalarda vardı)
            UnitAgent.RegisterExternalAction(0, sourceIndex, 0);
        }

        // 2. Action Selection Ajanını Tetikle
        // "Ben bu üniteyle ŞU işi (actionType) yapmaya karar verdim" der.
        if (ActionAgent != null)
        {
            // Action ajanı source'u Orchestrator üzerinden okur, biz actionType'ı veririz.
            ActionAgent.RegisterExternalAction(actionType, sourceIndex, targetIndex);
        }

        // 3. Target Selection Ajanını Tetikle
        // "Hedef olarak ŞU kareyi (targetIndex) seçtim" der.
        if (TargetAgent != null)
        {
            TargetAgent.RegisterExternalAction(actionType, sourceIndex, targetIndex);
        }

        Debug.Log($"[DEMO] Kayıt Alındı -> Source:{sourceIndex} Action:{actionType} Target:{targetIndex}");
    }
    public void AddGroupReward(float reward)
    {
        if (UnitAgent != null) UnitAgent.AddReward(reward);
        if (ActionAgent != null) ActionAgent.AddReward(reward);
        if (TargetAgent != null) TargetAgent.AddReward(reward);
    }

    public void EndGroupEpisode()
    {
        if (UnitAgent != null) UnitAgent.EndEpisode();
        if (ActionAgent != null) ActionAgent.EndEpisode();
        if (TargetAgent != null) TargetAgent.EndEpisode();
    }
    public void AddTargetRewardOnly(float reward) { if (TargetAgent != null) TargetAgent.AddReward(reward); }
    public void AddActionRewardOnly(float reward) { if (ActionAgent != null) ActionAgent.AddReward(reward); }
    public void AddUnitRewardOnly(float reward) { if (UnitAgent != null) UnitAgent.AddReward(reward); }
}