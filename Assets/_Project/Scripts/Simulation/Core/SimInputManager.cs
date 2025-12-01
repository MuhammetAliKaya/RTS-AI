using UnityEngine;
using UnityEngine.EventSystems;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core; // Context için şart

public class SimInputManager : MonoBehaviour
{
    public static SimInputManager Instance;

    public Camera MainCamera;
    public GameVisualizer Visualizer;

    public int SelectedUnitID { get; private set; } = -1;

    void Awake()
    {
        Instance = this;
        if (MainCamera == null) MainCamera = Camera.main;
    }

    void Update()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        if (Input.GetMouseButtonDown(0)) HandleSelection();
        if (Input.GetMouseButtonDown(1)) HandleMovementOrder();
    }

    // --- GIZMOS ---
    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        // DÜNYAYI CONTEXT'TEN AL
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

    void HandleMovementOrder()
    {
        // DÜNYAYI CONTEXT'TEN AL
        var world = SimGameContext.ActiveWorld;
        if (world == null || SelectedUnitID == -1) return;

        if (!world.Units.TryGetValue(SelectedUnitID, out SimUnitData selectedUnit))
        {
            SelectedUnitID = -1;
            return;
        }

        if (selectedUnit.PlayerID != 1) return;

        int2? gridPos = GetGridPositionUnderMouse();
        if (gridPos == null) return;

        foreach (var res in world.Resources.Values)
        {
            if (res.GridPosition == gridPos.Value)
            {
                if (selectedUnit.UnitType == SimUnitType.Worker)
                {
                    SimUnitSystem.TryAssignGatherTask(selectedUnit, res, world);
                    Debug.Log("⛏️ Toplama Emri.");
                }
                return;
            }
        }

        SimUnitSystem.OrderMove(selectedUnit, gridPos.Value, world);
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
                SelectedUnitID = visual.ID;
                Debug.Log($"🎯 Seçildi: {SelectedUnitID}");
                return;
            }
        }
        SelectedUnitID = -1;
    }

    public int2? GetGridPositionUnderMouse()
    {
        var world = SimGameContext.ActiveWorld; // Context
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