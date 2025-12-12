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
        // 1. DÜNYA VE SEÇİM KONTROLÜ
        var world = SimGameContext.ActiveWorld;
        if (world == null || SelectedUnitID == -1) return;

        if (!world.Units.TryGetValue(SelectedUnitID, out SimUnitData selectedUnit))
        {
            SelectedUnitID = -1;
            return;
        }

        // Sadece kendi ünitelerimiz
        if (selectedUnit.PlayerID != 1) return;

        // 2. HEDEF TESPİTİ (RAYCAST ÖNCELİKLİ)
        // Önce "Görsel" olarak neye tıkladığımıza bakıyoruz (Ağaç, Bina, Ünite).
        // Bu sayede izometrik hataları (ağacın arkasına yürüme) engelliyoruz.

        int2 targetGridPos = new int2(-1, -1);
        bool hitEntity = false;
        int clickedEntityID = -1; // Tıklanan objenin ID'sini tutalım

        Vector2 mouseWorldPos = MainCamera.ScreenToWorldPoint(Input.mousePosition);
        RaycastHit2D hit = Physics2D.Raycast(mouseWorldPos, Vector2.zero);

        if (hit.collider != null)
        {
            SimEntityVisual visual = hit.collider.GetComponent<SimEntityVisual>();
            if (visual != null)
            {
                int id = visual.ID;
                clickedEntityID = id;

                // Tıklanan şey Kaynak mı?
                if (world.Resources.ContainsKey(id))
                {
                    targetGridPos = world.Resources[id].GridPosition;
                    hitEntity = true;
                }
                // Tıklanan şey Bina mı?
                else if (world.Buildings.ContainsKey(id))
                {
                    targetGridPos = world.Buildings[id].GridPosition;
                    hitEntity = true;
                }
                // Tıklanan şey Ünite mi?
                else if (world.Units.ContainsKey(id))
                {
                    targetGridPos = world.Units[id].GridPosition;
                    hitEntity = true;
                }
            }
        }

        // Eğer bir objeye denk gelmediysek, zemini (matematiksel grid'i) kullan
        if (!hitEntity)
        {
            int2? calculatedPos = GetGridPositionUnderMouse();
            if (calculatedPos == null) return; // Harita dışı
            targetGridPos = calculatedPos.Value;
        }

        // 3. AKSİYON TÜRÜNE KARAR VER (SMART CONTEXT)
        // Varsayılan: MOVE (11)
        int actionID = 11;

        // Hedef karesinde ne var? (Raycast ile bulduysak zaten biliyoruz, yoksa Grid'den bakıyoruz)
        var targetNode = world.Map.Grid[targetGridPos.x, targetGridPos.y];
        int occupantID = (hitEntity) ? clickedEntityID : targetNode.OccupantID;

        // A. DÜŞMAN KONTROLÜ (Ünite veya Bina)
        if (occupantID != -1)
        {
            if (world.Units.TryGetValue(occupantID, out SimUnitData targetUnit))
            {
                // Düşman mı? -> ATTACK (10)
                if (targetUnit.PlayerID != selectedUnit.PlayerID) actionID = 10;
            }
            else if (world.Buildings.TryGetValue(occupantID, out SimBuildingData targetBuilding))
            {
                // Düşman binası mı? -> ATTACK (10)
                if (targetBuilding.PlayerID != selectedUnit.PlayerID) actionID = 10;
            }
        }

        // B. KAYNAK KONTROLÜ
        // Raycast ile bir kaynağa tıkladıysak VEYA o karede kaynak varsa
        if (world.Resources.Values.Any(r => r.GridPosition.Equals(targetGridPos)))
        {
            actionID = 12; // GATHER
        }

        // C. UI'DAN GELEN ÖZEL KOMUT (İnşaat vb.)
        if (_pendingActionID != 10 && _pendingActionID != 0)
        {
            actionID = _pendingActionID;
        }

        // 4. ML-AGENTS KAYIT (DÜZELTİLEN KISIM)
        if (RTSAgent.Instance != null)
        {
            int mapW = world.Map.Width;
            int sourceIndex = (selectedUnit.GridPosition.y * mapW) + selectedUnit.GridPosition.x;
            int targetIndex = (targetGridPos.y * mapW) + targetGridPos.x;

            // Ajanı dürt (Kayıt alması için)
            RTSAgent.Instance.RegisterExternalAction(actionID, sourceIndex, targetIndex);

            // DİKKAT: BURADA 'return' YOK! Kod aşağı akıp işlemi yapacak.
        }

        // 5. İŞLEMİ UYGULA (MANUEL FORCING)
        // Bu kısım hem ajan varken (kayıt anında) hem yokken (test) çalışır.

        if (actionID == 10) // ATTACK
        {
            // Hedefi tekrar bul (Unit mi Bina mı?)
            if (world.Units.TryGetValue(occupantID, out SimUnitData enemyUnit))
                SimUnitSystem.OrderAttackUnit(selectedUnit, enemyUnit, world);
            else if (world.Buildings.TryGetValue(occupantID, out SimBuildingData enemyBuilding))
                SimUnitSystem.OrderAttack(selectedUnit, enemyBuilding, world);
        }
        else if (actionID == 12) // GATHER
        {
            var res = world.Resources.Values.FirstOrDefault(r => r.GridPosition.Equals(targetGridPos));
            if (res != null)
            {
                bool assigned = SimUnitSystem.TryAssignGatherTask(selectedUnit, res, world);
                if (!assigned)
                {
                    // Eğer toplama görevi verilemezse (örn: asker seçiliyse) oraya yürü
                    SimUnitSystem.OrderMove(selectedUnit, targetGridPos, world);
                }
            }
        }
        else // MOVE (11) veya diğerleri
        {
            SimUnitSystem.OrderMove(selectedUnit, targetGridPos, world);
        }

        // Modu sıfırla
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