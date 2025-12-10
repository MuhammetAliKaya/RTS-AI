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

    // --- SELECTION DATA ---
    public int SelectedUnitID { get; private set; } = -1;
    public int SelectedBuildingID { get; private set; } = -1;

    private int _pendingActionID = 10;

    void Awake()
    {
        Instance = this;
        if (MainCamera == null) MainCamera = Camera.main;
    }

    void Update()
    {
        // UI blocking check
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        if (Input.GetMouseButtonDown(0)) HandleSelection();      // Left Click: Select
        if (Input.GetMouseButtonDown(1)) HandleMovementOrder();  // Right Click: Action
    }
    // --- DIŞARIDAN (UI) ÇAĞRILACAK METOT ---
    public void SetPendingAction(int actionID)
    {
        _pendingActionID = actionID;
        Debug.Log($"[Input] Sıradaki işlem ayarlandı: {actionID}. Lütfen haritada bir yere sağ tıkla.");
    }

    // --- SEÇİLİ ÜNİTENİN INDEX'İNİ DÖNDÜRÜR ---
    public int GetSelectedUnitSourceIndex()
    {
        var world = SimGameContext.ActiveWorld;
        if (world == null || SelectedUnitID == -1) return -1;

        if (world.Units.TryGetValue(SelectedUnitID, out SimUnitData u))
        {
            // Sadece kendi oyuncumuz (Player 1)
            if (u.PlayerID == 1)
                return (u.GridPosition.y * world.Map.Width) + u.GridPosition.x;
        }
        return -1;
    }
    void HandleSelection()
    {

        Vector2 mousePos = MainCamera.ScreenToWorldPoint(Input.mousePosition);
        RaycastHit2D hit = Physics2D.Raycast(mousePos, Vector2.zero);

        if (hit.collider != null)
        {
            Debug.Log($"🎯 Unit SelectedAAAAAAAAAAA");

            SimEntityVisual visual = hit.collider.GetComponent<SimEntityVisual>();
            if (visual != null)
            {
                int id = visual.ID;
                var world = SimGameContext.ActiveWorld;

                // 1. Is it a Unit?
                if (world.Units.ContainsKey(id))
                {
                    SelectedUnitID = id;
                    SelectedBuildingID = -1;
                    Debug.Log($"🎯 Unit Selected: {id}");
                    return;
                }
                // 2. Is it a Building?
                else if (world.Buildings.ContainsKey(id))
                {
                    SelectedBuildingID = id;
                    SelectedUnitID = -1;
                    Debug.Log($"🏠 Building Selected: {id}");
                    return;
                }
            }
        }

        // Clicked on empty space -> Deselect all
        SelectedUnitID = -1;
        SelectedBuildingID = -1;
    }

    void HandleMovementOrder()
    {
        var world = SimGameContext.ActiveWorld;
        if (world == null || SelectedUnitID == -1) return;

        // Seçili üniteyi al
        if (!world.Units.TryGetValue(SelectedUnitID, out SimUnitData selectedUnit))
        {
            SelectedUnitID = -1;
            return;
        }

        // Sadece kendi ünitelerimiz
        if (selectedUnit.PlayerID != 1) return;

        int2? gridPos = GetGridPositionUnderMouse();
        if (gridPos == null) return;

        // --- ML-AGENTS ENTEGRASYONU ---
        int mapW = world.Map.Width;

        // 1. SOURCE: Seçili ünitenin konumu
        int sourceIndex = (selectedUnit.GridPosition.y * mapW) + selectedUnit.GridPosition.x;

        // 2. TARGET: Farenin altındaki konum
        int targetIndex = (gridPos.Value.y * mapW) + gridPos.Value.x;

        // 3. ACTION: UI'dan ayarlanmış bekleyen aksiyon (Default: 10)
        int actionID = _pendingActionID;

        // Agent'a gönder
        if (RTSAgent.Instance != null)
        {
            // Kaydı oluştur ve ajanı çalıştır
            RTSAgent.Instance.RegisterExternalAction(actionID, sourceIndex, targetIndex);

            // Emri verdikten sonra modu varsayılan "Move" (10) moduna döndür
            _pendingActionID = 10;
            return;
        }

        // Enemy/Building Check
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

        // Default Move
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

    // --- GIZMOS ---
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
}