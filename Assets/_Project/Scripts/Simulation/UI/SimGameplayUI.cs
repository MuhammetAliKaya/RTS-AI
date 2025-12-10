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

    public void OnClickBuildHouse() { PrepareBuildOrder(1, SimBuildingType.House); }
    public void OnClickBuildBarracks() { PrepareBuildOrder(2, SimBuildingType.Barracks); }
    public void OnClickBuildTower() { PrepareBuildOrder(8, SimBuildingType.Tower); }
    public void OnClickBuildWall() { PrepareBuildOrder(9, SimBuildingType.Wall); }

    // Translator'da henüz tanımlı olmayanlar (Sadece Manuel çalışır)
    public void OnClickBuildFarm() { PrepareBuildOrder(-1, SimBuildingType.Farm); }
    public void OnClickBuildWoodCutter() { PrepareBuildOrder(-1, SimBuildingType.WoodCutter); }
    public void OnClickBuildStonePit() { PrepareBuildOrder(-1, SimBuildingType.StonePit); }

    private void PrepareBuildOrder(int actionID, SimBuildingType type)
    {
        // 1. Durum: Agent Modu Aktif mi? (Ve geçerli bir Agent Action ID var mı?)
        if (RTSAgent.Instance != null && actionID != -1)
        {
            // İşçi seçili mi kontrol et (Source Index)
            int workerIdx = GetSelectedUnitSourceIndex();

            if (workerIdx != -1)
            {
                // Input Manager'a "Sıradaki sağ tık bu aksiyon olsun" diyoruz.
                // Bu sayede sen haritaya tıkladığında Agent'a (Action, Source, Target) gidecek
                // ve Agent bunu 'Translator' üzerinden GERÇEKLEŞTİRECEK.
                if (SimInputManager.Instance != null)
                {
                    SimInputManager.Instance.SetPendingAction(actionID);
                    Debug.Log($"[UI] Agent İnşaat Modu (ID: {actionID}). Lütfen haritada inşa edilecek yere SAĞ TIKLA.");
                }

                if (BuildingPlacer != null)
                {
                    BuildingPlacer.SelectBuildingToPlace(type);
                    Debug.Log($"[UI] Manuel Yerleştirme Modu: {type}");
                }
                else
                {
                    Debug.LogWarning("BuildingPlacer atanmamış!");
                }
            }
            else
            {
                Debug.LogWarning("Agent Modu: İnşaat için önce bir İŞÇİ seçmelisin!");
            }
        }
        // 2. Durum: Agent Yok veya Tanımsız Aksiyon (Manuel Oynanış / Test Modu)
        else
        {
            // Eski 'Hayalet Bina' sistemini çalıştır
            if (BuildingPlacer != null)
            {
                BuildingPlacer.SelectBuildingToPlace(type);
                Debug.Log($"[UI] Manuel Yerleştirme Modu: {type}");
            }
            else
            {
                Debug.LogWarning("BuildingPlacer atanmamış!");
            }
        }

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
            // Agent Varsa -> Emri Agent'a ilet
            if (RTSAgent.Instance != null)
            {
                // Source: Binanın konumu
                int sourceIndex = (targetBuilding.GridPosition.y * world.Map.Width) + targetBuilding.GridPosition.x;

                // Target: 0 (Üretim için hedef koordinat gerekmez, bina içinde çıkar)
                // Bu çağrı Agent'ın 'Heuristic' metodunu tetikler -> 'OnActionReceived' -> 'Translator' -> ÜRETİM YAPILIR.
                RTSAgent.Instance.RegisterExternalAction(actionID, sourceIndex, 0);

                Debug.Log($"[UI] Agent Üretim Emri: {uType} (Act: {actionID})");
            }
            // Agent Yoksa -> Direkt sistem üzerinden başlat (Manuel Mod)
            else
            {
                SimBuildingSystem.StartTraining(targetBuilding, world, uType);
                Debug.Log($"[UI] Manuel Üretim Başlatıldı: {uType}");
            }
        }
        else
        {
            Debug.LogWarning($"Uygun {bType} bulunamadı! (Seçili değil veya inşa edilmemiş)");
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

        // B. Seçili değilse, haritadaki boşta duran ilk binayı bul (Yardımcı olmak için)
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
}