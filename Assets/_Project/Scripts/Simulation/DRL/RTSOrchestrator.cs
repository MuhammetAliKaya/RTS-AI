using UnityEngine;
using RTS.Simulation.Core;
using RTS.Simulation.Systems;
using RTS.Simulation.Data;

public class RTSOrchestrator : MonoBehaviour
{
    public static RTSOrchestrator Instance;

    [Header("Sub-Agents")]
    // Alt Ajan Referansları (Inspector'dan atayın)
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

    // Anlık Karar Verileri (Context - Alt ajanlar buradan okur)
    [HideInInspector] public int SelectedSourceIndex = -1;
    [HideInInspector] public int SelectedActionType = 0;

    // Durum Makinesi
    public enum OrchestratorState { Idle, WaitingUnit, WaitingAction, WaitingTarget }
    private OrchestratorState _state = OrchestratorState.Idle;

    private void Awake()
    {
        Instance = this;
    }

    public void Setup(SimWorldState world, SimGridSystem gridSys, SimUnitSystem unitSys, SimBuildingSystem buildSys)
    {
        _world = world;
        _gridSystem = gridSys;
        _unitSystem = unitSys;
        _buildingSystem = buildSys;

        // Translator'ı oluştur
        Translator = new DRLActionTranslator(_world, _unitSystem, _buildingSystem, _gridSystem, MyPlayerID);

        // --- ALT AJANLARI BAŞLAT ---

        // 1. UnitAgent: Sadece world ve translator yeterli (veya ihtiyaca göre gridSys)
        UnitAgent.Setup(this, _world, Translator);

        // 2. ActionAgent: Sadece context'e bakar, grid'e ihtiyacı yoktur.
        ActionAgent.Setup(this, _world, Translator);

        // 3. TargetAgent: HARİTAYI GÖRMELİ! Bu yüzden gridSys'i buraya paslıyoruz.
        // (TargetSelectionAgent.cs içindeki Setup fonksiyonunu buna göre güncellemiştik)
        TargetAgent.Setup(this, _world, Translator, _gridSystem);
    }

    public void RequestFullDecision()
    {
        // Zinciri Başlat: Adım 1 - Kim?
        _state = OrchestratorState.WaitingUnit;
        SelectedSourceIndex = -1;
        SelectedActionType = 0;

        UnitAgent.RequestDecision();
    }

    // Adım 1 Tamamlandığında Çağrılır (UnitSelectionAgent'tan gelir)
    public void OnUnitSelected(int sourceIndex)
    {
        SelectedSourceIndex = sourceIndex;

        // Adım 2'ye Geç - Ne Yapacak?
        _state = OrchestratorState.WaitingAction;
        ActionAgent.RequestDecision();
    }

    // Adım 2 Tamamlandığında Çağrılır (ActionSelectionAgent'tan gelir)
    public void OnActionSelected(int actionType)
    {
        SelectedActionType = actionType;

        // Adım 3'e Geç - Nereye?
        _state = OrchestratorState.WaitingTarget;
        TargetAgent.RequestDecision();
    }

    // Adım 3 Tamamlandığında (TargetSelectionAgent'tan gelir -> Zincir Biter)
    public void OnTargetSelected(int targetIndex)
    {
        _state = OrchestratorState.Idle;

        // Eylemi Uygula
        bool success = Translator.ExecuteAction(SelectedActionType, SelectedSourceIndex, targetIndex);

        // --- ÖDÜL MEKANİZMASI ---
        // Sadece basit bir işlem ödülü/cezası veriyoruz. 
        // Asıl büyük ödüller (Savaş/Ekonomi) AdversarialTrainerRunner üzerinden verilecek.
        float stepReward = success ? 0.01f : -0.02f; // Hata yaparsa biraz daha fazla ceza
        AddGroupReward(stepReward);
    }

    // Tüm ajanlara ortak ödül ekler (Cooperative Multi-Agent)
    // Runner scripti, kill/resource ödüllerini buraya gönderir.
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
}