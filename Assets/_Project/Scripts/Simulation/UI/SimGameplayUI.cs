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
    // Agent Translator Kodları: House=1, Barracks=2, Tower=8, Wall=9

    public void OnClickBuildHouse() { HandleBuildCommand(1, SimBuildingType.House); }
    public void OnClickBuildBarracks() { HandleBuildCommand(2, SimBuildingType.Barracks); }
    public void OnClickBuildTower() { HandleBuildCommand(8, SimBuildingType.Tower); }
    public void OnClickBuildWall() { HandleBuildCommand(9, SimBuildingType.Wall); }

    // Translator'da henüz tanımlı olmayanlar (Eski usül devam eder)
    public void OnClickBuildFarm() { SelectBuild(SimBuildingType.Farm); }
    public void OnClickBuildWoodCutter() { SelectBuild(SimBuildingType.WoodCutter); }
    public void OnClickBuildStonePit() { SelectBuild(SimBuildingType.StonePit); }

    private void HandleBuildCommand(int actionID, SimBuildingType type)
    {
        // 1. Agent varsa ve bir İŞÇİ seçiliyse -> Agent'a "İnşa Et" emri ver
        // Not: Agent modunda "Target" 0 gönderiyoruz, Translator "Auto Build" mantığıyla en iyi yeri bulacak.
        if (RTSAgent.Instance != null)
        {
            int workerSource = GetSelectedUnitSourceIndex();
            if (workerSource != -1)
            {
                SendToAgent(actionID, workerSource, 0);
                CloseAllMenus();
                return;
            }
            // İşçi seçili değilse uyarı ver (Agent rastgele işçi seçmesin, oyuncu kimi seçtiyse o yapsın)
            Debug.Log("Agent Modu: İnşaat için önce bir işçi seçmelisin!");
        }

        // 2. Agent yoksa veya işçi seçilmediyse -> Manuel Yerleştirme Modunu Aç (BuildingPlacer)
        SelectBuild(type);
    }

    private void SelectBuild(SimBuildingType type)
    {
        if (BuildingPlacer != null) BuildingPlacer.SelectBuildingToPlace(type);
        CloseAllMenus();
    }

    // --- ÜRETİM BUTONLARI ---
    // Agent Translator Kodları: Worker=3, Soldier=4

    public void OnClickTrainWorker() { HandleTrainCommand(SimBuildingType.Base, SimUnitType.Worker, 3); }
    public void OnClickTrainSoldier() { HandleTrainCommand(SimBuildingType.Barracks, SimUnitType.Soldier, 4); }

    private void HandleTrainCommand(SimBuildingType bType, SimUnitType uType, int actionID)
    {
        var world = SimGameContext.ActiveWorld;
        if (world == null) return;

        // 1. Uygun binayı bul (Önce seçiliye, sonra boştakilere bak)
        SimBuildingData targetBuilding = FindTrainingBuilding(world, bType);

        if (targetBuilding != null)
        {
            // Agent Varsa -> Emri ona ilet (Source: Bina Konumu)
            if (RTSAgent.Instance != null)
            {
                int sourceIndex = (targetBuilding.GridPosition.y * world.Map.Width) + targetBuilding.GridPosition.x;
                SendToAgent(actionID, sourceIndex, 0); // Üretimde Target önemsiz
            }
            // Agent Yoksa -> Direkt üretimi başlat
            else
            {
                SimBuildingSystem.StartTraining(targetBuilding, world, uType);
                Debug.Log($"Manüel Üretim Başlatıldı: {uType}");
            }
        }
        else
        {
            Debug.LogWarning($"Uygun {bType} bulunamadı! (Kaynak yetersiz veya bina yok)");
        }
        CloseAllMenus();
    }

    // --- YARDIMCI METOTLAR ---

    private SimBuildingData FindTrainingBuilding(SimWorldState world, SimBuildingType type)
    {
        // A. Oyuncunun seçtiği binaya bak
        int selectedID = SimInputManager.Instance.SelectedBuildingID;
        if (selectedID != -1 && world.Buildings.TryGetValue(selectedID, out SimBuildingData b))
        {
            if (b.PlayerID == 1 && b.Type == type && b.IsConstructed && !b.IsTraining)
                return b;
        }

        // B. Seçili değilse, haritadaki boşta duran ilk binayı bul (Yardımcı)
        return world.Buildings.Values.FirstOrDefault(b =>
            b.PlayerID == 1 && b.Type == type && b.IsConstructed && !b.IsTraining);
    }

    private int GetSelectedUnitSourceIndex()
    {
        var world = SimGameContext.ActiveWorld;
        int uid = SimInputManager.Instance.SelectedUnitID;
        if (uid != -1 && world != null && world.Units.TryGetValue(uid, out SimUnitData u))
        {
            if (u.PlayerID == 1) // Sadece kendi ünitelerimiz
                return (u.GridPosition.y * world.Map.Width) + u.GridPosition.x;
        }
        return -1;
    }

    private void SendToAgent(int actionID, int sourceIdx, int targetIdx)
    {
        if (RTSAgent.Instance != null)
        {
            RTSAgent.Instance.RegisterExternalAction(actionID, sourceIdx, targetIdx);
        }
    }
}