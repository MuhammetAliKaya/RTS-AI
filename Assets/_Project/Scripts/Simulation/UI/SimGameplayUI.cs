using UnityEngine;
using UnityEngine.UI;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
using System.Linq;

public class SimGameplayUI : MonoBehaviour
{
    [Header("Sistemler")]
    public SimBuildingPlacer BuildingPlacer;

    [Header("Menü Panelleri")]
    public GameObject ConstructionPanel;
    public GameObject ProductionPanel;

    // --- HELPER: BEN KİMİM? ---
    private int MyPlayerID => SimInputManager.Instance != null ? SimInputManager.Instance.LocalPlayerID : 1;

    // --- DÜZELTME: AJAN KONTROLÜNÜ GÜNCELLE ---
    private bool ShouldUseAgentMode()
    {
        // 1. SimInputManager yoksa manuel devam et
        if (SimInputManager.Instance == null) return false;

        // 2. Orchestrator (Yeni Sistem) varsa ve Demo Modu açıksa Agent modunu kullan
        // (Eğer Human oynuyorsa ama Demo kaydı kapalıysa FALSE döner -> Manuel oynanır)
        if (SimInputManager.Instance.Orchestrator != null)
        {
            // Sadece Demo modu açıksa ve sıra bizdeyse
            return SimInputManager.Instance.Orchestrator.IsHumanDemoMode;
        }

        // 3. Eski RTSAgent varsa (Legacy destek)
        if (RTSAgent.Instance != null && RTSAgent.Instance.MyPlayerID == MyPlayerID)
        {
            return true;
        }

        // Ajan yoksa manuel mod
        return false;
    }

    public void ToggleConstructionMenu()
    {
        bool isActive = ConstructionPanel.activeSelf;
        CloseAllMenus();
        ConstructionPanel.SetActive(!isActive);
    }

    public void ToggleProductionMenu()
    {
        bool isActive = ProductionPanel.activeSelf;
        CloseAllMenus();
        ProductionPanel.SetActive(!isActive);
    }

    private void CloseAllMenus()
    {
        if (ConstructionPanel) ConstructionPanel.SetActive(false);
        if (ProductionPanel) ProductionPanel.SetActive(false);
    }

    // --- İNŞAAT KOMUTLARI ---
    public void OnClickBuildHouse() { PrepareBuildOrder(1, SimBuildingType.House); }
    public void OnClickBuildBarracks() { PrepareBuildOrder(2, SimBuildingType.Barracks); }
    public void OnClickBuildTower() { PrepareBuildOrder(8, SimBuildingType.Tower); }
    public void OnClickBuildWall() { PrepareBuildOrder(9, SimBuildingType.Wall); }
    public void OnClickBuildWoodCutter() { PrepareBuildOrder(5, SimBuildingType.WoodCutter); }
    public void OnClickBuildStonePit() { PrepareBuildOrder(6, SimBuildingType.StonePit); }
    public void OnClickBuildFarm() { PrepareBuildOrder(7, SimBuildingType.Farm); }

    private void PrepareBuildOrder(int actionID, SimBuildingType type)
    {
        if (BuildingPlacer == null)
        {
            Debug.LogWarning("BuildingPlacer atanmamış!");
            return;
        }

        // Her durumda BuildingPlacer'ı aktifleştiriyoruz.
        // Ajan modu (Demo) olsa bile görsel olarak yeri seçmemiz lazım.
        // InputManager ve Placer, tıklama anında bunu Ajan'a kaydedecek.
        BuildingPlacer.SelectBuildingToPlace(type);

        // Eğer Demo modundaysak InputManager'a hangi aksiyonu yapacağımızı fısıldıyoruz
        if (ShouldUseAgentMode() && SimInputManager.Instance != null)
        {
            SimInputManager.Instance.SetPendingAction(actionID);
        }

        CloseAllMenus();
    }

    // --- ÜRETİM KOMUTLARI ---
    public void OnClickTrainWorker() { HandleTrainCommand(SimBuildingType.Base, SimUnitType.Worker, 3); }
    public void OnClickTrainSoldier() { HandleTrainCommand(SimBuildingType.Barracks, SimUnitType.Soldier, 4); }

    private void HandleTrainCommand(SimBuildingType bType, SimUnitType uType, int actionID)
    {
        var world = SimGameContext.ActiveWorld;
        if (world == null) return;

        // Benim uygun binamı bul
        var targetBuilding = world.Buildings.Values
            .FirstOrDefault(b => b.PlayerID == MyPlayerID && b.Type == bType && b.IsConstructed && !b.IsTraining);

        if (targetBuilding != null)
        {
            if (ShouldUseAgentMode())
            {
                // --- ORCHESTRATOR / AGENT ÜZERİNDEN KAYIT ---
                int sourceIndex = (targetBuilding.GridPosition.y * world.Map.Width) + targetBuilding.GridPosition.x;

                // Orchestrator varsa ona kaydet
                if (SimInputManager.Instance.Orchestrator != null)
                {
                    SimInputManager.Instance.Orchestrator.RecordHumanDemonstration(sourceIndex, actionID, 0);
                    SimBuildingSystem.StartTraining(targetBuilding, world, uType);
                    // Orchestrator genellikle kayıttan sonra işlemi kendi Translator'ı ile yapar.
                    // Eğer yapmıyorsa aşağıya bir "Execute" eklemek gerekebilir.
                }
                // Eski Agent varsa ona kaydet
                else if (RTSAgent.Instance != null)
                {
                    RTSAgent.Instance.RegisterExternalAction(actionID, sourceIndex, 0);
                }
            }
            else
            {
                // --- MANUEL MOD (Doğrudan Simülasyona Emir Ver) ---
                SimBuildingSystem.StartTraining(targetBuilding, world, uType);
                Debug.Log($"[UI] Manuel Üretim Başlatıldı: {uType}");
            }
        }
        else
        {
            Debug.LogWarning($"Player {MyPlayerID} için uygun {bType} bulunamadı veya meşgul!");
        }

        CloseAllMenus();
    }

    private int GetSelectedUnitSourceIndex()
    {
        var world = SimGameContext.ActiveWorld;
        int uid = SimInputManager.Instance.SelectedUnitID;

        if (uid != -1 && world != null && world.Units.TryGetValue(uid, out SimUnitData u))
        {
            if (u.PlayerID == MyPlayerID)
                return (u.GridPosition.y * world.Map.Width) + u.GridPosition.x;
        }
        return -1;
    }
}