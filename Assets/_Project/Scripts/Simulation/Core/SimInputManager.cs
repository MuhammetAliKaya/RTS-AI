using UnityEngine;
using UnityEngine.EventSystems;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;
using RTS.Simulation.Core;
// using Unity.Mathematics;
using System.Linq;


public class SimInputManager : MonoBehaviour
{
    public static SimInputManager Instance;
    public Camera MainCamera;
    public GameVisualizer Visualizer;

    public int LocalPlayerID = 1; // Varsayılan 1, Runner bunu değiştirebilir.

    // --- AI BAĞLANTILARI ---
    // 3 Başlı yapıyı yöneten orkestratör referansı
    public RTSOrchestrator Orchestrator;


    // --- SELECTION DATA ---
    public int SelectedUnitID { get; private set; } = -1;
    public int SelectedBuildingID { get; private set; } = -1;

    private int _pendingActionID = 10; // Varsayılan: Attack/Interact
    private SimBuildingPlacer _buildingPlacer;

    void Awake()
    {
        Instance = this;
        if (MainCamera == null) MainCamera = Camera.main;

        // Eğer editörden atanmadıysa sahnede bulmaya çalış
        if (Orchestrator == null) Orchestrator = FindObjectOfType<RTSOrchestrator>();
        _buildingPlacer = FindObjectOfType<SimBuildingPlacer>();
    }

    void Update()
    {
        // UI tıklamalarını engelle
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (_buildingPlacer != null && _buildingPlacer.IsPlacingMode) return;

        if (Input.GetMouseButtonDown(0)) HandleSelection();      // Sol Tık: Seçim (Unit Selection)
        if (Input.GetMouseButtonDown(1)) HandleMovementOrder();  // Sağ Tık: Aksiyon ve Hedef (Action + Target)
    }

    // --- DIŞARIDAN (UI) ÇAĞRILACAK METOT ---
    public void SetPendingAction(int actionID)
    {
        _pendingActionID = actionID;
        Debug.Log($"[Input] Sıradaki işlem: {actionID}. Sağ tık ile onayla.");
    }

    // --- SEÇİM İŞLEMİ (KİM?) ---
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
                    SelectedBuildingID = -1;

                    // --- DEMO KAYDI İÇİN: Ünite seçildiğini AI'a bildir ---
                    // Eğer sadece seçim yapıp henüz emir vermediysek bile, AI'ın "UnitSelection" ajanı
                    // bu seçimi görmeli mi? Genellikle emir tamamlandığında (Source+Action+Target)
                    // üçünü birden göndermek daha temizdir. O yüzden burayı pas geçiyorum.

                    Debug.Log($"🎯 Unit Selected: {id}");
                    return;
                }
                // 2. Bina mı?
                else if (world.Buildings.ContainsKey(id))
                {
                    SelectedBuildingID = id;
                    SelectedUnitID = -1;
                    Debug.Log($"🏠 Building Selected: {id}");
                    return;
                }
            }
        }

        // Boşluğa tıklandı -> Seçimi kaldır
        SelectedUnitID = -1;
        SelectedBuildingID = -1;
    }

    // --- EMİR İŞLEMİ (NE? ve NEREYE?) ---
    void HandleMovementOrder()
    {
        // 1. DÜNYA VE SEÇİM KONTROLÜ
        var world = SimGameContext.ActiveWorld;
        if (world == null) return;

        // Hem bina hem ünite seçili değilse çık
        if (SelectedUnitID == -1 && SelectedBuildingID == -1) return;

        // --- SOURCE (KAYNAK) BULMA ---
        int sourceIndex = -1;
        int playerID = -1;

        SimUnitData selectedUnit = null;
        SimBuildingData selectedBuilding = null;

        if (SelectedUnitID != -1 && world.Units.TryGetValue(SelectedUnitID, out selectedUnit))
        {
            sourceIndex = GetIndex(selectedUnit.GridPosition, world.Map.Width);
            playerID = selectedUnit.PlayerID;
        }
        else if (SelectedBuildingID != -1 && world.Buildings.TryGetValue(SelectedBuildingID, out selectedBuilding))
        {
            sourceIndex = GetIndex(selectedBuilding.GridPosition, world.Map.Width);
            playerID = selectedBuilding.PlayerID;
        }

        // --- KRİTİK DEĞİŞİKLİK: Sadece LocalPlayerID ile eşleşen birimleri kontrol et ---
        if (sourceIndex == -1 || playerID != LocalPlayerID)
        {
            // Debug.Log("Bu birim sizin değil!"); 
            return;
        }

        // 2. HEDEF TESPİTİ (RAYCAST ÖNCELİKLİ)
        int2 targetGridPos = new int2(-1, -1);
        bool hitEntity = false;
        int clickedEntityID = -1;

        Vector2 mouseWorldPos = MainCamera.ScreenToWorldPoint(Input.mousePosition);
        RaycastHit2D hit = Physics2D.Raycast(mouseWorldPos, Vector2.zero);

        if (hit.collider != null)
        {
            SimEntityVisual visual = hit.collider.GetComponent<SimEntityVisual>();
            if (visual != null)
            {
                int id = visual.ID;
                clickedEntityID = id;

                if (world.Resources.ContainsKey(id))
                {
                    targetGridPos = world.Resources[id].GridPosition;
                    hitEntity = true;
                }
                else if (world.Buildings.ContainsKey(id))
                {
                    targetGridPos = world.Buildings[id].GridPosition;
                    hitEntity = true;
                }
                else if (world.Units.ContainsKey(id))
                {
                    targetGridPos = world.Units[id].GridPosition;
                    hitEntity = true;
                }
            }
        }

        // Raycast bir şeye çarpmadıysa Grid pozisyonunu al
        if (!hitEntity)
        {
            int2? calculatedPos = GetGridPositionUnderMouse();
            if (calculatedPos == null) return;
            targetGridPos = calculatedPos.Value;
        }

        int targetIndex = GetIndex(targetGridPos, world.Map.Width);

        // 3. AKSİYON TÜRÜNE KARAR VER (SMART CONTEXT)
        int actionID = DetermineSmartAction(world, targetGridPos, hitEntity, clickedEntityID, selectedUnit, selectedBuilding);

        // UI'dan özel bir emir (İnşaat vb.) geldiyse onu kullan
        if (_pendingActionID != 10 && _pendingActionID != 0)
        {
            actionID = _pendingActionID;
        }

        // --------------------------------------------------------
        // 4. KRİTİK NOKTA: 3 BAŞLI AI'A DEMO GÖNDERİMİ
        // --------------------------------------------------------
        if (Orchestrator != null)
        {
            // Human Demo Kaydı: "Ben oyuncu olarak [Source] ile [Target]'a [Action] yaptım."
            // Orkestratör bunu alıp sırasıyla UnitAgent, ActionAgent ve TargetAgent'a "Heuristic" olarak enjekte edecek.
            Orchestrator.RecordHumanDemonstration(sourceIndex, actionID, targetIndex);
        }
        else
        {
            Debug.LogWarning("Orchestrator atanmadı! Eğitim verisi kaydedilemiyor.");
        }

        // 5. OYUNA MÜDAHALE (Görsel olarak emri hemen uygula)
        // Eğer "Adversarial Trainer" gibi bir yapı kullanıyorsan ve AI senin yerine oynuyorsa burayı kapatabilirsin.
        // Ama genellikle "Heuristic" modda hem kaydederiz hem oynarız.
        ExecuteGameAction(world, actionID, selectedUnit, selectedBuilding, clickedEntityID, targetGridPos);

        // Modu sıfırla
        _pendingActionID = 10;
    }

    // --- YARDIMCI METOTLAR ---

    private int DetermineSmartAction(SimWorldState world, int2 targetPos, bool hitEntity, int entityID, SimUnitData unit, SimBuildingData building)
    {
        // Varsayılan: MOVE (11)
        int actionID = 11;

        // Hedef karesinde ne var?
        var targetNode = world.Map.Grid[targetPos.x, targetPos.y];
        int occupantID = (hitEntity) ? entityID : targetNode.OccupantID;

        // A. DÜŞMAN KONTROLÜ
        if (occupantID != -1)
        {
            int myPlayerID = (unit != null) ? unit.PlayerID : building.PlayerID;

            if (world.Units.TryGetValue(occupantID, out SimUnitData targetUnit))
            {
                if (targetUnit.PlayerID != myPlayerID) actionID = 10; // ATTACK
            }
            else if (world.Buildings.TryGetValue(occupantID, out SimBuildingData targetB))
            {
                if (targetB.PlayerID != myPlayerID) actionID = 10; // ATTACK
            }
        }

        // B. KAYNAK KONTROLÜ
        if (world.Resources.Values.Any(r => r.GridPosition.Equals(targetPos)))
        {
            actionID = 12; // GATHER
        }

        return actionID;
    }

    private void ExecuteGameAction(SimWorldState world, int actionID, SimUnitData unit, SimBuildingData building, int targetEntityID, int2 targetPos)
    {
        // Not: Burada simülasyon kodlarını çağırıyoruz. AI arka planda öğrenirken oyunun donmaması için.
        // Sadece Üniteler hareket edebilir, binalar üretim yapar (o UI'dan gelir genelde)

        if (unit == null) return; // Şimdilik sadece ünite hareketlerini elle yapıyoruz

        if (actionID == 10) // ATTACK
        {
            var targetNode = world.Map.Grid[targetPos.x, targetPos.y];
            int occupantID = (targetEntityID != -1) ? targetEntityID : targetNode.OccupantID;

            if (world.Units.TryGetValue(occupantID, out SimUnitData enemyUnit))
                SimUnitSystem.OrderAttackUnit(unit, enemyUnit, world);
            else if (world.Buildings.TryGetValue(occupantID, out SimBuildingData enemyBuilding))
                SimUnitSystem.OrderAttack(unit, enemyBuilding, world);
        }
        else if (actionID == 12) // GATHER
        {
            var res = world.Resources.Values.FirstOrDefault(r => r.GridPosition.Equals(targetPos));
            if (res != null)
            {
                if (!SimUnitSystem.TryAssignGatherTask(unit, res, world))
                    SimUnitSystem.OrderMove(unit, targetPos, world);
            }
        }
        else // MOVE (11) veya inşaat dışı hareketler
        {
            // İnşaat komutları (1-9) buraya düşmemeli, onlar ExecuteAction ile AI tarafından veya 
            // UI butonu tıklandığında SimBuildingPlacer tarafından halledilir. 
            // Ama sağ tıklama ile "Move" varsayılır.
            if (actionID == 11)
                SimUnitSystem.OrderMove(unit, targetPos, world);
        }
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

    private int GetIndex(int2 pos, int width) => (pos.y * width) + pos.x;

    // --- GIZMOS ---
    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        if (SelectedUnitID != -1 && SimGameContext.ActiveWorld != null)
        {
            if (SimGameContext.ActiveWorld.Units.TryGetValue(SelectedUnitID, out SimUnitData unit))
            {
                Vector3 pos = GridToWorld(unit.GridPosition);
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(pos, 0.5f);
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