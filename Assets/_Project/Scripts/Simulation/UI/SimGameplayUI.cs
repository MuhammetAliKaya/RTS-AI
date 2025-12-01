using UnityEngine;
using UnityEngine.UI;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;

public class SimGameplayUI : MonoBehaviour
{
    [Header("Sistemler")]
    public SimBuildingPlacer BuildingPlacer;

    [Header("Menü Panelleri")]
    public GameObject ConstructionPanel;
    public GameObject ProductionPanel;

    // --- MENÜ KONTROLÜ ---
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

    // --- İNŞAAT BUTONLARI (HEPSİ EKLENDİ) ---

    public void OnClickBuildHouse() { SelectBuild(SimBuildingType.House); }
    public void OnClickBuildFarm() { SelectBuild(SimBuildingType.Farm); }
    public void OnClickBuildWoodCutter() { SelectBuild(SimBuildingType.WoodCutter); }
    public void OnClickBuildStonePit() { SelectBuild(SimBuildingType.StonePit); }
    public void OnClickBuildBarracks() { SelectBuild(SimBuildingType.Barracks); }
    public void OnClickBuildTower() { SelectBuild(SimBuildingType.Tower); }
    public void OnClickBuildWall() { SelectBuild(SimBuildingType.Wall); }

    // Yardımcı (Kod tekrarını önlemek için)
    private void SelectBuild(SimBuildingType type)
    {
        if (BuildingPlacer != null) BuildingPlacer.SelectBuildingToPlace(type);
        CloseAllMenus();
    }

    // --- ÜRETİM BUTONLARI ---

    public void OnClickTrainWorker()
    {
        var world = SimGameContext.ActiveWorld;
        if (world == null) return;

        // 1. SEÇİLİ BİNAYI AL
        int buildingID = SimInputManager.Instance.SelectedBuildingID;

        if (buildingID == -1)
        {
            Debug.LogWarning("⚠️ Önce bir Ana Üs (Base) seçmelisin!");
            return;
        }

        if (world.Buildings.TryGetValue(buildingID, out SimBuildingData b))
        {
            // 2. KONTROLLER (Base mi? Benim mi? Boş mu?)
            if (b.PlayerID == 1 && b.Type == SimBuildingType.Base && b.IsConstructed && !b.IsTraining)
            {
                SimBuildingSystem.StartTraining(b, world, SimUnitType.Worker);
                Debug.Log("👷 Seçili üsten işçi üretiliyor.");
            }
            else
            {
                Debug.LogWarning("❌ Seçili bina uygun değil (Dolu veya Base değil).");
            }
        }
    }

    public void OnClickTrainSoldier()
    {
        var world = SimGameContext.ActiveWorld;
        if (world == null) return;

        // 1. SEÇİLİ BİNAYI AL
        int buildingID = SimInputManager.Instance.SelectedBuildingID;

        if (buildingID == -1)
        {
            Debug.LogWarning("⚠️ Önce bir Kışla (Barracks) seçmelisin!");
            return;
        }

        if (world.Buildings.TryGetValue(buildingID, out SimBuildingData b))
        {
            // 2. KONTROLLER (Barracks mı? Benim mi? Boş mu?)
            if (b.PlayerID == 1 && b.Type == SimBuildingType.Barracks && b.IsConstructed && !b.IsTraining)
            {
                SimBuildingSystem.StartTraining(b, world, SimUnitType.Soldier);
                Debug.Log("⚔️ Seçili kışladan asker üretiliyor.");
            }
            else
            {
                Debug.LogWarning("❌ Seçili bina uygun değil (Dolu veya Kışla değil).");
            }
        }
    }
}