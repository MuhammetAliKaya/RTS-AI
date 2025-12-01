using UnityEngine;
using UnityEngine.EventSystems;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;

public class SimInputManager : MonoBehaviour
{
    public static SimInputManager Instance;
    public Camera MainCamera;
    public GameVisualizer Visualizer;

    // --- SEÇİM VERİLERİ ---
    public int SelectedUnitID { get; private set; } = -1;
    public int SelectedBuildingID { get; private set; } = -1; // YENİ: Seçili Bina ID

    void Awake()
    {
        Instance = this;
        if (MainCamera == null) MainCamera = Camera.main;
    }

    void Update()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

        if (Input.GetMouseButtonDown(0)) HandleSelection();
        if (Input.GetMouseButtonDown(1)) HandleMovementOrder();
    }

    // --- GIZMOS (Aynı kalıyor, sadece referans için ekledim) ---
    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        var world = SimGameContext.ActiveWorld;
        if (world == null || SelectedUnitID == -1) return;

        if (world.Units.TryGetValue(SelectedUnitID, out SimUnitData unit))
        {
            if (unit.Path != null && unit.Path.Count > 0)
            {
                Gizmos.color = Color.red;
                Vector3 previousPos = GridToWorld(unit.GridPosition);
                foreach (var nextStep in unit.Path)
                {
                    Vector3 nextPos = GridToWorld(nextStep);
                    Gizmos.DrawLine(previousPos, nextPos);
                    Gizmos.DrawSphere(nextPos, 0.2f);
                    previousPos = nextPos;
                }
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(previousPos, 0.4f);
            }
        }
    }

    private Vector3 GridToWorld(int2 pos)
    {
        float tW = Visualizer != null ? Visualizer.TileWidth : 2.56f;
        float tH = Visualizer != null ? Visualizer.TileHeight : 1.28f;
        float isoX = (pos.x - pos.y) * tW * 0.5f;
        float isoY = (pos.x + pos.y) * tH * 0.5f;
        return new Vector3(isoX, isoY, 0);
    }

    void HandleSelection()
    {
        Vector2 mousePos = MainCamera.ScreenToWorldPoint(Input.mousePosition);
        RaycastHit2D hit = Physics2D.Raycast(mousePos, Vector2.zero);

        if (hit.collider != null)
        {
            SimEntityVisual visual = hit.collider.GetComponent<SimEntityVisual>();
            if (visual != null)
            {
                int id = visual.ID;
                var world = SimGameContext.ActiveWorld;

                // 1. Ünite mi?
                if (world.Units.ContainsKey(id))
                {
                    SelectedUnitID = id;
                    SelectedBuildingID = -1; // Binayı bırak
                    Debug.Log($"🎯 Ünite Seçildi: {id}");
                    return;
                }
                // 2. Bina mı?
                else if (world.Buildings.ContainsKey(id))
                {
                    SelectedBuildingID = id;
                    SelectedUnitID = -1; // Üniteyi bırak
                    Debug.Log($"🏠 Bina Seçildi: {id}");
                    return;
                }
            }
        }

        // Boşa tıklanırsa her şeyi bırak
        SelectedUnitID = -1;
        SelectedBuildingID = -1;
    }

    void HandleMovementOrder()
    {
        var world = SimGameContext.ActiveWorld;

        // Hareket sadece ÜNİTE seçiliyse geçerlidir
        if (world == null || SelectedUnitID == -1) return;

        if (!world.Units.TryGetValue(SelectedUnitID, out SimUnitData selectedUnit))
        {
            SelectedUnitID = -1;
            return;
        }

        if (selectedUnit.PlayerID != 1) return;

        int2? gridPos = GetGridPositionUnderMouse();
        if (gridPos == null) return;

        // Kaynak / Saldırı kontrolleri (Önceki kodun aynısı)
        foreach (var res in world.Resources.Values)
        {
            if (res.GridPosition == gridPos.Value)
            {
                if (selectedUnit.UnitType == SimUnitType.Worker)
                    SimUnitSystem.TryAssignGatherTask(selectedUnit, res, world);
                return;
            }
        }

        var targetNode = world.Map.Grid[gridPos.Value.x, gridPos.Value.y];
        if (targetNode.OccupantID != -1)
        {
            int targetID = targetNode.OccupantID;
            if (world.Units.TryGetValue(targetID, out SimUnitData enemyUnit))
            {
                if (enemyUnit.PlayerID != selectedUnit.PlayerID && selectedUnit.UnitType == SimUnitType.Soldier)
                {
                    selectedUnit.TargetID = targetID;
                    selectedUnit.State = SimTaskType.Attacking;
                    float dist = SimGridSystem.GetDistance(selectedUnit.GridPosition, enemyUnit.GridPosition);
                    if (dist > selectedUnit.AttackRange)
                    {
                        selectedUnit.Path = SimGridSystem.FindPath(world, selectedUnit.GridPosition, enemyUnit.GridPosition);
                        if (selectedUnit.Path.Count > 0) selectedUnit.State = SimTaskType.Moving;
                    }
                    return;
                }
            }
            else if (world.Buildings.TryGetValue(targetID, out SimBuildingData enemyBuilding))
            {
                if (enemyBuilding.PlayerID != selectedUnit.PlayerID && selectedUnit.UnitType == SimUnitType.Soldier)
                {
                    selectedUnit.TargetID = targetID;
                    selectedUnit.State = SimTaskType.Attacking;
                    int2? standPos = SimGridSystem.FindWalkableNeighbor(world, enemyBuilding.GridPosition);
                    if (standPos.HasValue)
                    {
                        selectedUnit.Path = SimGridSystem.FindPath(world, selectedUnit.GridPosition, standPos.Value);
                        if (selectedUnit.Path.Count > 0) selectedUnit.State = SimTaskType.Moving;
                    }
                    return;
                }
            }
        }

        SimUnitSystem.OrderMove(selectedUnit, gridPos.Value, world);
    }

    public int2? GetGridPositionUnderMouse()
    {
        var world = SimGameContext.ActiveWorld;
        if (world == null) return null;
        float tW = Visualizer != null ? Visualizer.TileWidth : 2.56f;
        float tH = Visualizer != null ? Visualizer.TileHeight : 1.28f;
        Vector3 mousePos = Input.mousePosition;
        Vector3 worldPos = MainCamera.ScreenToWorldPoint(mousePos);
        worldPos.z = 0;
        float halfW = tW * 0.5f;
        float halfH = tH * 0.5f;
        int gridY = Mathf.RoundToInt((worldPos.y / halfH - worldPos.x / halfW) / 2f);
        int gridX = Mathf.RoundToInt((worldPos.y / halfH + worldPos.x / halfW) / 2f);
        int2 pos = new int2(gridX, gridY);
        if (world.Map.IsInBounds(pos)) return pos;
        return null;
    }
}