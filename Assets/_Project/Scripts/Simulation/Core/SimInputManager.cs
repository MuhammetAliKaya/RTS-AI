using UnityEngine;
using UnityEngine.EventSystems;
using RTS.Simulation.Data;
using RTS.Simulation.Systems;

public class SimInputManager : MonoBehaviour
{
    public static SimInputManager Instance;
    public SimRunner Runner;
    public Camera MainCamera;
    public GameVisualizer Visualizer;

    // --- SEÇİM SİSTEMİ ---
    public int SelectedUnitID { get; private set; } = -1;

    void Awake()
    {
        Instance = this;
        if (MainCamera == null) MainCamera = Camera.main;
    }

    void Update()
    {
        // UI Koruması
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        // SOL TIK: Seçim Yap
        if (Input.GetMouseButtonDown(0))
        {
            HandleSelection();
        }

        // --- YENİ: SAĞ TIK (HAREKET EMRİ) ---
        if (Input.GetMouseButtonDown(1))
        {
            HandleMovementOrder();
        }
    }

    void HandleMovementOrder()
    {
        // 1. Kontroller
        if (Runner == null || Runner.World == null) return;
        if (SelectedUnitID == -1) return;

        if (!Runner.World.Units.TryGetValue(SelectedUnitID, out SimUnitData selectedUnit))
        {
            SelectedUnitID = -1;
            return;
        }

        if (selectedUnit.PlayerID != 1) return;

        // 2. Tıklanan yeri al
        int2? gridPos = GetGridPositionUnderMouse();
        if (gridPos == null) return;

        // --- 3. KAYNAK KONTROLÜ (YENİ) ---
        // Tıklanan karede bir kaynak var mı?
        foreach (var res in Runner.World.Resources.Values)
        {
            if (res.GridPosition == gridPos.Value)
            {
                // Kaynak bulundu! İşçiye toplama emri ver.
                // Not: Sadece Worker toplayabilir, Soldier ise saldırmalı (İleride eklenir)
                if (selectedUnit.UnitType == SimUnitType.Worker)
                {
                    if (SimUnitSystem.TryAssignGatherTask(selectedUnit, res, Runner.World))
                    {
                        Debug.Log($"⛏️ TOPLAMA EMRİ: ID {selectedUnit.ID} -> {res.Type} ({res.GridPosition})");
                    }
                    else
                    {
                        Debug.LogWarning("❌ Kaynağa ulaşılamıyor (Etrafı kapalı)!");
                    }
                }
                else
                {
                    Debug.Log("⚠️ Askerler kaynak toplayamaz.");
                }

                return; // Kaynağa tıklandıysa hareket emri verme, çık.
            }
        }

        // --- 4. HAREKET EMRİ (Varsayılan) ---
        // Kaynak yoksa, oraya yürü
        SimUnitSystem.OrderMove(selectedUnit, gridPos.Value, Runner.World);
        Debug.Log($"🚶 Yürüme Emri: {gridPos.Value}");
    }

    void HandleSelection()
    {
        // ... (Burası eski kodunla AYNI KALSIN) ...
        // (Kısa tutmak için tekrar yazmıyorum, eski Raycast'li hali duracak)

        int2? gridPos = GetGridPositionUnderMouse();
        Vector2 mousePos = MainCamera.ScreenToWorldPoint(Input.mousePosition);
        RaycastHit2D hit = Physics2D.Raycast(mousePos, Vector2.zero);

        if (hit.collider != null)
        {
            SimEntityVisual visual = hit.collider.GetComponent<SimEntityVisual>();
            if (visual != null)
            {
                SelectedUnitID = visual.ID;
                Debug.Log($"🎯 SEÇİLDİ: ID {SelectedUnitID}");
                return;
            }
        }

        // Yedek grid kontrolü vs... (Eski kodun devamı)
        SelectedUnitID = -1;
    }

    public int2? GetGridPositionUnderMouse()
    {
        // ... (Burası da AYNI KALSIN) ...
        if (Runner == null || Runner.World == null) return null;
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
        if (Runner.World.Map.IsInBounds(pos)) return pos;
        return null;
    }
}