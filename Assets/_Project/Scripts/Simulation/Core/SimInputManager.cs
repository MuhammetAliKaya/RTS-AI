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
        if (Input.GetKeyDown(KeyCode.Space)) HandleWaitCommand();
    }
    private void HandleWaitCommand()
    {
        // Sadece Demo Modunda ve bir ünite seçiliyken çalışır
        if (RTSOrchestrator.Instance == null || !RTSOrchestrator.Instance.IsHumanDemoMode) return;
        if (SelectedUnitID == -1) return;

        var world = SimGameContext.ActiveWorld;
        if (world != null && world.Units.TryGetValue(SelectedUnitID, out SimUnitData u))
        {
            // Sadece kendi ünitelerimiz için
            if (u.PlayerID != RTSOrchestrator.Instance.MyPlayerID) return;

            int mapW = world.Map.Width;
            int sourceIndex = (u.GridPosition.y * mapW) + u.GridPosition.x;

            // --- ZİNCİRLEME KAYIT (Wait) ---
            // Sanki mouse ile tıklamışız gibi 3 adımı da hızlıca simüle ediyoruz.

            // 1. Adım: Üniteyi Seçtiğimizi Teyit Et
            RTSOrchestrator.Instance.UserSelectUnit(sourceIndex);

            // 2. Adım: Aksiyon Olarak "0" (WAIT) Seç
            // (ActionSelectionAgent.cs içinde ACT_WAIT = 0 sabiti var)
            RTSOrchestrator.Instance.UserSelectAction(0);

            // 3. Adım: Hedef Olarak "0" (veya kendisi) Seç ve KAYDI BİTİR
            // Wait eyleminde hedef önemsizdir ama zincirin tamamlanması için gereklidir.
            RTSOrchestrator.Instance.UserSelectTarget(sourceIndex);

            Debug.Log($"[Demo] '{u.UnitType}' için BEKLE (Wait) emri kaydedildi.");
        }
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

        int clickedID = -1;
        bool isUnit = false;

        if (hit.collider != null)
        {
            SimEntityVisual visual = hit.collider.GetComponent<SimEntityVisual>();
            if (visual != null)
            {
                clickedID = visual.ID;
                if (SimGameContext.ActiveWorld.Units.ContainsKey(clickedID)) isUnit = true;
            }
        }

        if (clickedID != -1)
        {
            if (isUnit)
            {
                SelectedUnitID = clickedID;
                SelectedBuildingID = -1;

                // --- ORCHESTRATOR'A BİLDİR (BUFFER START) ---
                if (RTSOrchestrator.Instance != null && RTSOrchestrator.Instance.IsHumanDemoMode)
                {
                    var world = SimGameContext.ActiveWorld;
                    var u = world.Units[clickedID];
                    // Sadece bizim ünitelerimiz kayda girsin
                    if (u.PlayerID == RTSOrchestrator.Instance.MyPlayerID)
                    {
                        int gridIndex = (u.GridPosition.y * world.Map.Width) + u.GridPosition.x;
                        RTSOrchestrator.Instance.UserSelectUnit(gridIndex);
                    }
                }
            }
            else
            {
                // Bina seçimi (Şimdilik AI için bina seçimi yoksa pas geçiyoruz)
                SelectedBuildingID = clickedID;
                SelectedUnitID = -1;
            }
        }
        else
        {
            // --- BOŞA TIKLANDI: SEÇİMİ VE BUFFER'I İPTAL ET ---
            SelectedUnitID = -1;
            SelectedBuildingID = -1;

            // Orchestrator'a "Vazgeçtik" demenin basit yolu:
            // Geçersiz bir unit ID göndererek state'i resetleyebiliriz veya 
            // Orchestrator'a public void CancelSelection() yazabilirsin.
            // Şimdilik en basit yöntem, yeni bir unit seçilmediği için 
            // _tempSourceIndex zaten eski kalacak veya UserSelectUnit çağrılmayacak.
            // Ama temiz iş için Orchestrator'a -1 gönderebiliriz.
            if (RTSOrchestrator.Instance != null)
            {
                RTSOrchestrator.Instance.UserSelectUnit(-1);
                // UserSelectUnit(-1) metodun içinde _tempSourceIndex = -1 yapacağı için
                // sonraki Action/Target çağrıları "Source Eksik" diyip iptal olur.
            }
        }
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