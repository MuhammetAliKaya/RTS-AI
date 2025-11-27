using UnityEngine;
using UnityEngine.UI; // Button ve Panel işlemleri için
using RTS.Simulation.Data;
using RTS.Simulation.Systems;

public class SimGameplayUI : MonoBehaviour
{
    [Header("Sistemler")]
    public SimBuildingPlacer BuildingPlacer;
    public SimRunner Runner;

    [Header("Menü Panelleri (Collapsible)")]
    public GameObject ConstructionPanel; // Bina Butonlarının olduğu panel
    public GameObject ProductionPanel;   // Asker Üretim butonlarının olduğu panel

    // --- MENÜ KONTROLÜ ---

    public void ToggleConstructionMenu()
    {
        bool isActive = ConstructionPanel.activeSelf;
        CloseAllMenus(); // Önce hepsini kapat
        ConstructionPanel.SetActive(!isActive); // Tıklananı tersine çevir
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

    // --- İNŞAAT BUTONLARI (On Click Eventleri) ---

    public void OnClickBuildFarm()
    {
        BuildingPlacer.SelectBuildingToPlace(SimBuildingType.Farm);
        CloseAllMenus(); // Seçim yapınca menüyü kapat
    }

    public void OnClickBuildBarracks()
    {
        BuildingPlacer.SelectBuildingToPlace(SimBuildingType.Barracks);
        CloseAllMenus();
    }

    public void OnClickBuildTower()
    {
        BuildingPlacer.SelectBuildingToPlace(SimBuildingType.Tower);
        CloseAllMenus();
    }

    // --- ÜRETİM BUTONLARI ---

    public void OnClickTrainWorker()
    {
        // Base binasını bulup üretim emri verelim
        // (Gerçek oyunda seçili binaya emir verilir, şimdilik bulduğumuz ilk Base'e verelim)

        foreach (var b in Runner.World.Buildings.Values)
        {
            if (b.Type == SimBuildingType.Base && b.PlayerID == 1 && b.IsConstructed)
            {
                // Maliyet Kontrolü (50 Et)
                if (SimResourceSystem.SpendResources(Runner.World, 1, 0, 0, 50))
                {
                    b.IsTraining = true;
                    b.UnitInProduction = SimUnitType.Worker;
                    b.TrainingTimer = 0f;
                    Debug.Log("👷 İşçi üretimi başladı!");
                }
                else
                {
                    Debug.LogWarning("❌ Yetersiz Kaynak (50 Et lazım)");
                }
                return; // Bir tanesine emir verdik, çık
            }
        }
    }
}