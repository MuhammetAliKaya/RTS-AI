using UnityEngine;
using UnityEngine.UI;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
using System.Linq; // Linq ekledik

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

    // --- İNŞAAT BUTONLARI ---
    public void OnClickBuildHouse() { SelectBuild(SimBuildingType.House); }
    public void OnClickBuildFarm() { SelectBuild(SimBuildingType.Farm); }
    public void OnClickBuildWoodCutter() { SelectBuild(SimBuildingType.WoodCutter); }
    public void OnClickBuildStonePit() { SelectBuild(SimBuildingType.StonePit); }
    public void OnClickBuildBarracks() { SelectBuild(SimBuildingType.Barracks); }
    public void OnClickBuildTower() { SelectBuild(SimBuildingType.Tower); }
    public void OnClickBuildWall() { SelectBuild(SimBuildingType.Wall); }

    private void SelectBuild(SimBuildingType type)
    {
        if (BuildingPlacer != null) BuildingPlacer.SelectBuildingToPlace(type);
        CloseAllMenus();
    }

    // --- AKILLI ÜRETİM BUTONLARI (GÜNCELLENDİ) ---

    public void OnClickTrainWorker()
    {
        TryTrainUnitSmart(SimBuildingType.Base, SimUnitType.Worker);
    }

    public void OnClickTrainSoldier()
    {
        TryTrainUnitSmart(SimBuildingType.Barracks, SimUnitType.Soldier);
    }

    // --- YENİ FONKSİYON: AKILLI ÜRETİM ---
    private void TryTrainUnitSmart(SimBuildingType buildingType, SimUnitType unitType)
    {
        var world = SimGameContext.ActiveWorld;
        if (world == null) return;

        // 1. ÖNCE SEÇİLİ BİNAYI KONTROL ET
        // Eğer oyuncu özellikle bir binayı seçtiyse, öncelik ondadır.
        int selectedID = SimInputManager.Instance.SelectedBuildingID;
        if (selectedID != -1 && world.Buildings.TryGetValue(selectedID, out SimBuildingData selectedB))
        {
            // Seçili bina doğru tipte, benim ve boşta ise -> Buradan bas
            if (selectedB.PlayerID == 1 && selectedB.Type == buildingType && selectedB.IsConstructed && !selectedB.IsTraining)
            {
                SimBuildingSystem.StartTraining(selectedB, world, unitType);
                Debug.Log($"🎯 Seçili binadan üretim: {unitType}");
                return;
            }
        }

        // 2. SEÇİLİ DEĞİLSE (VEYA DOLUYSA), HARİTADAKİ DİĞER BİNALARA BAK
        // Benim olan, bitmiş ve ŞU AN ÜRETİM YAPMAYAN ilk binayı bul.
        var idleBuilding = world.Buildings.Values.FirstOrDefault(b =>
            b.PlayerID == 1 &&
            b.Type == buildingType &&
            b.IsConstructed &&
            !b.IsTraining // <-- Kritik nokta: Boş olanı bul
        );

        if (idleBuilding != null)
        {
            SimBuildingSystem.StartTraining(idleBuilding, world, unitType);
            Debug.Log($"🤖 Otomatik binadan üretim: {unitType} (ID: {idleBuilding.ID})");
        }
        else
        {
            // Hiç boş bina yoksa veya kaynak yetmiyorsa
            Debug.LogWarning($"❌ Üretim yapılamadı. Ya boş {buildingType} yok ya da kaynak yetersiz.");
        }
    }
}