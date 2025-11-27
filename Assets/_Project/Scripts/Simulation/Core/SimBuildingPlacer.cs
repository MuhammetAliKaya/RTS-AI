using UnityEngine;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;

public class SimBuildingPlacer : MonoBehaviour
{
    public SimRunner Runner;

    // Şu an elimizde tuttuğumuz (inşa etmek istediğimiz) bina tipi
    private SimBuildingType _selectedBuildingType = SimBuildingType.None;
    private bool _isPlacingMode = false;

    // Görsel Hayalet (Ghost)
    private GameObject _ghostObject;

    [Header("Prefab Referansları (Hayalet İçin)")]
    public GameObject GhostBase;
    public GameObject GhostFarm;
    public GameObject GhostBarracks;
    // ... Diğerlerini de ekleyebilirsin

    void Update()
    {
        if (!_isPlacingMode) return;

        // 1. Fare altındaki kareyi bul
        int2? gridPos = SimInputManager.Instance.GetGridPositionUnderMouse();

        if (gridPos.HasValue)
        {
            // Hayaleti oraya taşı (Görselleştirme)
            if (_ghostObject != null)
            {
                _ghostObject.SetActive(true);
                // InputManager'daki tile boyutlarını kullanarak pozisyon hesapla
                // (Burada basitlik için direkt Visualizer mantığını kopyalayabilirsin veya InputManager'dan çekebilirsin)
                // Şimdilik hayaleti gizliyoruz, direkt tıklama mantığına geçelim.
            }

            // 2. Tıklama Kontrolü (Sol Tık)
            if (Input.GetMouseButtonDown(0))
            {
                TryBuild(gridPos.Value);
            }
        }

        // Sağ Tık -> İptal
        if (Input.GetMouseButtonDown(1))
        {
            CancelBuildMode();
        }
    }

    public void SelectBuildingToPlace(SimBuildingType type)
    {
        _selectedBuildingType = type;
        _isPlacingMode = true;
        Debug.Log($"İnşaat Modu: {type} seçildi. Yeri seçin.");
    }

    private void TryBuild(int2 pos)
    {
        var world = Runner.World;

        // 1. İŞÇİ KONTROLÜ: Bir işçi seçili mi?
        int workerID = SimInputManager.Instance.SelectedUnitID;
        if (workerID == -1 || !world.Units.TryGetValue(workerID, out SimUnitData worker))
        {
            Debug.LogWarning("⚠️ Önce bir işçi seçmelisin!");
            return;
        }

        if (worker.UnitType != SimUnitType.Worker)
        {
            Debug.LogWarning("⚠️ Askerler bina yapamaz! Bir işçi seç.");
            return;
        }

        // 2. Yer Müsait mi?
        if (!SimGridSystem.IsWalkable(world, pos))
        {
            Debug.LogWarning("❌ Burası inşaat için uygun değil!");
            return;
        }

        // 3. Maliyet Kontrolü (Örnek)
        int woodCost = 0, stoneCost = 0;
        if (_selectedBuildingType == SimBuildingType.Farm) { woodCost = 100; }
        else if (_selectedBuildingType == SimBuildingType.Barracks) { woodCost = 200; stoneCost = 100; }

        if (!SimResourceSystem.CanAfford(world, 1, woodCost, stoneCost, 0))
        {
            Debug.LogWarning("❌ Kaynak yetersiz!");
            return;
        }

        // 4. HARCA VE TEMELİ AT
        SimResourceSystem.SpendResources(world, 1, woodCost, stoneCost, 0);

        var b = new SimBuildingData
        {
            ID = world.NextID(),
            PlayerID = 1,
            Type = _selectedBuildingType,
            GridPosition = pos,
            IsConstructed = false, // <--- KRİTİK: Henüz bitmedi!
            ConstructionProgress = 0
        };

        SimBuildingSystem.InitializeBuildingStats(b);

        world.Buildings.Add(b.ID, b);
        world.Map.Grid[pos.x, pos.y].IsWalkable = false;

        Debug.Log($"🔨 {_selectedBuildingType} temeli atıldı! İşçi yola çıkıyor...");

        // 5. İŞÇİYE EMİR VER
        SimUnitSystem.OrderBuild(worker, b, world);

        CancelBuildMode();
    }

    private void CancelBuildMode()
    {
        _isPlacingMode = false;
        _selectedBuildingType = SimBuildingType.None;
        if (_ghostObject != null) Destroy(_ghostObject);
    }
}