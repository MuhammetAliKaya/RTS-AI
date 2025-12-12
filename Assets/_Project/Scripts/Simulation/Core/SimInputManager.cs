using UnityEngine;
using UnityEngine.EventSystems;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
using System.Linq;

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

        // --- CONTEXT (BAĞLAM) HESAPLAMA ---
        // Sağ tıklandığında ne yapılacağına karar ver (Smart Context)
        int actionID = 11; // Varsayılan: MOVE (11)

        var targetNode = world.Map.Grid[gridPos.Value.x, gridPos.Value.y];

        // 1. Hedefte bir ünite veya bina var mı?
        if (targetNode.OccupantID != -1)
        {
            if (world.Units.TryGetValue(targetNode.OccupantID, out SimUnitData targetUnit))
            {
                // Düşman mı? -> ATTACK (10)
                if (targetUnit.PlayerID != selectedUnit.PlayerID) actionID = 10;
            }
            else if (world.Buildings.TryGetValue(targetNode.OccupantID, out SimBuildingData targetBuilding))
            {
                // Düşman binası mı? -> ATTACK (10)
                if (targetBuilding.PlayerID != selectedUnit.PlayerID) actionID = 10;
            }
        }
        // 2. Hedefte kaynak var mı? -> GATHER (12)
        else if (world.Resources.Values.Any(r => r.GridPosition.Equals(gridPos.Value)))
        {
            actionID = 12;
        }

        // 3. UI'dan özel bir işlem seçildiyse (Bina kurma vb.) onu koru
        if (_pendingActionID != 10 && _pendingActionID != 0)
        {
            actionID = _pendingActionID;
        }

        // --- ML-AGENTS ENTEGRASYONU ---
        int mapW = world.Map.Width;
        int sourceIndex = (selectedUnit.GridPosition.y * mapW) + selectedUnit.GridPosition.x;
        int targetIndex = (gridPos.Value.y * mapW) + gridPos.Value.x;

        // Agent varsa veriyi gönder ve çık
        if (RTSAgent.Instance != null)
        {
            // Kaydı oluştur ve ajanı DÜRT
            RTSAgent.Instance.RegisterExternalAction(actionID, sourceIndex, targetIndex);

            // İşlem sonrası varsayılan moda dön
            _pendingActionID = 10;
            return;
        }

        // --- AGENT YOKSA MANUEL ÇALIŞTIRMA (FALLBACK) ---
        // Burası test sahnelerinde ajan yoksa veya manuel kontrol isteniyorsa çalışır.
        if (actionID == 10) // Attack
        {
            // Hedefin ne olduğunu (Bina mı, Ünite mi) tekrar tespit et ve saldır
            if (targetNode.OccupantID != -1)
            {
                if (world.Units.TryGetValue(targetNode.OccupantID, out SimUnitData enemyUnit))
                {
                    SimUnitSystem.OrderAttackUnit(selectedUnit, enemyUnit, world);
                }
                else if (world.Buildings.TryGetValue(targetNode.OccupantID, out SimBuildingData enemyBuilding))
                {
                    SimUnitSystem.OrderAttack(selectedUnit, enemyBuilding, world);
                }
            }
        }
        else if (actionID == 12) // Gather
        {
            var res = world.Resources.Values.FirstOrDefault(r => r.GridPosition.Equals(gridPos.Value));
            if (res != null)
            {
                SimUnitSystem.TryAssignGatherTask(selectedUnit, res, world);
            }
        }
        else // Move (11) veya diğerleri
        {
            SimUnitSystem.OrderMove(selectedUnit, gridPos.Value, world);
        }

        // İşlem sonrası varsayılan moda dön (Manuel mod için de geçerli)
        _pendingActionID = 10;
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