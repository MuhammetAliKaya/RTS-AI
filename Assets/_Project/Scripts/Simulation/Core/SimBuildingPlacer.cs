using UnityEngine;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;

public class SimBuildingPlacer : MonoBehaviour
{
    private SimBuildingType _selectedBuildingType = SimBuildingType.None;
    private bool _isPlacingMode = false;
    private GameObject _ghostObject;

    void Update()
    {
        if (!_isPlacingMode) return;

        if (Input.GetMouseButtonDown(0))
        {
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
        if (world == null) return;

        // 1. İŞÇİ KONTROLÜ
        int workerID = SimInputManager.Instance.SelectedUnitID;
        if (workerID == -1 || !world.Units.TryGetValue(workerID, out SimUnitData worker))
        {
            Debug.LogWarning("⚠️ Önce bir işçi seçmelisin!");
            CancelBuildMode();
            return;
        }

        if (worker.UnitType != SimUnitType.Worker || worker.PlayerID != 1)
        {
            Debug.LogWarning("⚠️ Bu birim bina yapamaz veya senin değil.");
            CancelBuildMode();
            return;
        }

        // 2. YER KONTROLÜ
        if (world.Map.Grid[pos.x, pos.y].OccupantID != -1 || world.Map.Grid[pos.x, pos.y].Type != SimTileType.Grass)
        {
            Debug.LogWarning("❌ Yer uygun değil.");
            return;
        }

        // 3. MALİYET KONTROLÜ (SimConfig'den)
        int wood = 0, stone = 0, meat = 0;

        switch (_selectedBuildingType)
        {
            case SimBuildingType.House: wood = SimConfig.HOUSE_COST_WOOD; break;
            case SimBuildingType.Farm: wood = SimConfig.FARM_COST_WOOD; break;
            case SimBuildingType.WoodCutter: meat = SimConfig.WOODCUTTER_COST_MEAT; break;
            case SimBuildingType.StonePit: wood = SimConfig.STONEPIT_COST_WOOD; break;
            case SimBuildingType.Barracks: wood = SimConfig.BARRACKS_COST_WOOD; stone = SimConfig.BARRACKS_COST_STONE; break;
            case SimBuildingType.Tower: wood = SimConfig.TOWER_COST_WOOD; stone = SimConfig.TOWER_COST_STONE; break;
            case SimBuildingType.Wall: stone = SimConfig.WALL_COST_STONE; break;
        }

        if (!SimResourceSystem.CanAfford(world, 1, wood, stone, meat))
        {
            Debug.LogWarning($"❌ Kaynak yetersiz! (Gereken: W:{wood} S:{stone} M:{meat})");
            CancelBuildMode();
            return;
        }

        // 4. HARCA VE YAP
        SimResourceSystem.SpendResources(world, 1, wood, stone, meat);

        var b = new SimBuildingData
        {
            ID = world.NextID(),
            PlayerID = 1,
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