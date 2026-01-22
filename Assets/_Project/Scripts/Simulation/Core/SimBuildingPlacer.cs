using UnityEngine;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;

public class SimBuildingPlacer : MonoBehaviour
{
    private SimBuildingType _selectedBuildingType = SimBuildingType.None;
    private bool _isPlacingMode = false;
    public bool IsPlacingMode => _isPlacingMode;
    private GameObject _ghostObject;

    void Update()
    {
        if (!_isPlacingMode) return;

        if (Input.GetMouseButtonDown(0))
        {
            // UI üzerine tıklamayı engelle
            if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

            int2? gridPos = SimInputManager.Instance.GetGridPositionUnderMouse();
            if (gridPos.HasValue) TryBuild(gridPos.Value);
        }

        if (Input.GetMouseButtonDown(1)) CancelBuildMode();
    }

    public void SelectBuildingToPlace(SimBuildingType type)
    {
        _selectedBuildingType = type;
        _isPlacingMode = true;
        Debug.Log($"🏗️ İNŞAAT MODU: {type}");
    }

    private void TryBuild(int2 pos)
    {
        var world = SimGameContext.ActiveWorld;
        if (world == null || SimInputManager.Instance == null) return;

        int myID = SimInputManager.Instance.LocalPlayerID; // DİNAMİK ID

        // 1. İŞÇİ KONTROLÜ
        int workerID = SimInputManager.Instance.SelectedUnitID;
        if (workerID == -1 || !world.Units.TryGetValue(workerID, out SimUnitData worker))
        {
            Debug.LogWarning("⚠️ Önce bir işçi seçmelisin!");
            CancelBuildMode();
            return;
        }

        // HATA DÜZELTİLDİ: Sabit 1 yerine myID kullanıldı
        if (worker.UnitType != SimUnitType.Worker || worker.PlayerID != myID)
        {
            Debug.LogWarning($"⚠️ Bu birim bina yapamaz veya senin ({myID}) değil.");
            CancelBuildMode();
            return;
        }

        // 2. YER KONTROLÜ
        if (!world.Map.IsInBounds(pos) || world.Map.Grid[pos.x, pos.y].OccupantID != -1 || world.Map.Grid[pos.x, pos.y].Type != SimTileType.Grass)
        {
            Debug.LogWarning("❌ Yer uygun değil.");
            return;
        }

        // 3. MALİYET HESABI
        int wood = 0, stone = 0, meat = 0;
        int actionID = 0;

        switch (_selectedBuildingType)
        {
            case SimBuildingType.House: wood = SimConfig.HOUSE_COST_WOOD; stone = SimConfig.HOUSE_COST_STONE; meat = SimConfig.HOUSE_COST_MEAT; actionID = 1; break;
            case SimBuildingType.Barracks: wood = SimConfig.BARRACKS_COST_WOOD; stone = SimConfig.BARRACKS_COST_STONE; meat = SimConfig.BARRACKS_COST_MEAT; actionID = 2; break;
            case SimBuildingType.WoodCutter: wood = SimConfig.WOODCUTTER_COST_WOOD; stone = SimConfig.WOODCUTTER_COST_STONE; meat = SimConfig.WOODCUTTER_COST_MEAT; actionID = 5; break;
            case SimBuildingType.StonePit: wood = SimConfig.STONEPIT_COST_WOOD; stone = SimConfig.STONEPIT_COST_STONE; meat = SimConfig.STONEPIT_COST_MEAT; actionID = 6; break;
            case SimBuildingType.Farm: wood = SimConfig.FARM_COST_WOOD; stone = SimConfig.FARM_COST_STONE; meat = SimConfig.FARM_COST_MEAT; actionID = 7; break;
            case SimBuildingType.Tower: wood = SimConfig.TOWER_COST_WOOD; stone = SimConfig.TOWER_COST_STONE; meat = SimConfig.TOWER_COST_MEAT; actionID = 8; break;
            case SimBuildingType.Wall: wood = SimConfig.WALL_COST_WOOD; stone = SimConfig.WALL_COST_STONE; meat = SimConfig.WALL_COST_MEAT; actionID = 9; break;
        }

        // HATA DÜZELTİLDİ: Kaynak kontrolü myID ile yapılıyor
        if (!SimResourceSystem.CanAfford(world, myID, wood, stone, meat))
        {
            Debug.LogWarning($"❌ Kaynak yetersiz! (Oyuncu {myID})");
            CancelBuildMode();
            return;
        }

        // --- DÜZELTME: AJAN KAYDI (YENİ ORCHESTRATOR DESTEĞİ) ---
        // Sadece Orchestrator varsa ve Demo modu açıksa kaydet
        if (SimInputManager.Instance.Orchestrator != null && SimInputManager.Instance.Orchestrator.IsHumanDemoMode)
        {
            int mapW = world.Map.Width;
            int sourceIndex = (worker.GridPosition.y * mapW) + worker.GridPosition.x;
            int targetIndex = (pos.y * mapW) + pos.x;

            SimInputManager.Instance.Orchestrator.RecordHumanDemonstration(sourceIndex, actionID, targetIndex);
        }
        // Eski Agent desteği (Opsiyonel)
        else if (RTSAgent.Instance != null && RTSAgent.Instance.MyPlayerID == myID)
        {
            int mapW = world.Map.Width;
            int sourceIndex = (worker.GridPosition.y * mapW) + worker.GridPosition.x;
            int targetIndex = (pos.y * mapW) + pos.x;
            RTSAgent.Instance.RegisterExternalAction(actionID, sourceIndex, targetIndex);
        }

        // 4. İNŞAAT
        // HATA DÜZELTİLDİ: Harcama ve bina sahipliği myID ile yapılıyor
        SimResourceSystem.SpendResources(world, myID, wood, stone, meat);

        var b = new SimBuildingData
        {
            ID = world.NextID(),
            PlayerID = myID, // <-- DÜZELTİLDİ
            Type = _selectedBuildingType,
            GridPosition = pos,
            IsConstructed = false,
            ConstructionProgress = 0
        };

        SimBuildingSystem.InitializeBuildingStats(b);
        world.Buildings.Add(b.ID, b);
        world.Map.Grid[pos.x, pos.y].OccupantID = b.ID;
        world.Map.Grid[pos.x, pos.y].IsWalkable = false;

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