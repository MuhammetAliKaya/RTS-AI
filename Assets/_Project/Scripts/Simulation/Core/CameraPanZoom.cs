using UnityEngine;
using RTS.Simulation.Core; // SimGameContext için

public class CameraPanZoom : MonoBehaviour
{
    [Header("Pan Settings")]
    public float PanSpeed = 20f;

    // Sınırlandırma için (Harita dışına çıkmasın)
    public Vector2 PanLimitMin = new Vector2(-10, -10);

    [Header("Zoom Settings")]
    public float ScrollSpeed = 20f;
    public float MinZoom = 5f;
    public float MaxZoom = 20f;

    private Camera cam;

    void Start()
    {
        cam = GetComponent<Camera>();
    }

    void Update()
    {
        // UI Koruması
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

        HandleMovement();
        HandleZoom();
    }

    void HandleMovement()
    {
        Vector3 pos = transform.position;

        // --- SADECE KLAVYE KONTROLÜ (MOUSE KALDIRILDI) ---
        // 'Horizontal' (A/D, Sol/Sağ Ok) ve 'Vertical' (W/S, Yukarı/Aşağı Ok) akslarını kullanır.

        float h = Input.GetAxis("Horizontal"); // A=-1, D=1
        float v = Input.GetAxis("Vertical");   // S=-1, W=1

        pos.x += h * PanSpeed * Time.deltaTime;
        pos.y += v * PanSpeed * Time.deltaTime;

        // --- DİNAMİK SINIRLAMA ---
        float mapW = 50;
        float mapH = 50;

        // Harita boyutunu Context'ten al
        if (SimGameContext.ActiveWorld != null)
        {
            mapW = SimGameContext.ActiveWorld.Map.Width;
            mapH = SimGameContext.ActiveWorld.Map.Height;
        }

        // Sınırları uygula (İzometrik için yaklaşık değerler)
        // Harita boyutuna göre dinamik clamp
        pos.x = Mathf.Clamp(pos.x, PanLimitMin.x, mapW * 2);
        pos.y = Mathf.Clamp(pos.y, PanLimitMin.y, mapH * 2);

        transform.position = pos;
    }

    void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        float zoom = cam.orthographicSize;

        zoom -= scroll * ScrollSpeed * 100f * Time.deltaTime;
        zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);

        cam.orthographicSize = zoom;
    }
}