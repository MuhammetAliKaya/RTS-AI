using UnityEngine;
using RTS.Simulation.Orchestrator; // Harita boyutunu çekmek için

public class CameraPanZoom : MonoBehaviour
{
    [Header("Hareket Ayarları")]
    public float PanSpeed = 20f;       // Klavye hareket hızı
    public float PanBorderThickness = 10f; // Mouse kenara gelince hareket etsin mi? (Opsiyonel)
    public bool UseMouseEdgePanning = false; // Mouse kenar hareketi açık mı?

    [Header("Zoom Ayarları")]
    public float ZoomSpeed = 10f;
    public float MinZoom = 5f;         // En yakın
    public float MaxZoom = 30f;        // En uzak

    [Header("Sınırlar (Map Bounds)")]
    // Harita dışına çıkmayı engellemek için
    public bool LimitToMap = true;
    public Vector2 MinLimit;
    public Vector2 MaxLimit;

    private Camera _cam;

    void Start()
    {
        _cam = GetComponent<Camera>();
        if (_cam == null) _cam = Camera.main;

        // Harita boyutlarını ExperimentManager'dan otomatik çekmeye çalış
        CalculateBounds();
    }

    void LateUpdate()
    {
        HandlePan();
        HandleZoom();
    }

    void HandlePan()
    {
        Vector3 pos = transform.position;

        // --- KLAVYE HAREKETİ (WASD / Yön Tuşları) ---
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
        {
            pos.y += PanSpeed * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
        {
            pos.y -= PanSpeed * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
        {
            pos.x += PanSpeed * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
        {
            pos.x -= PanSpeed * Time.deltaTime;
        }

        // --- MOUSE KENAR HAREKETİ (Opsiyonel) ---
        if (UseMouseEdgePanning)
        {
            if (Input.mousePosition.y >= Screen.height - PanBorderThickness)
                pos.y += PanSpeed * Time.deltaTime;
            if (Input.mousePosition.y <= PanBorderThickness)
                pos.y -= PanSpeed * Time.deltaTime;
            if (Input.mousePosition.x >= Screen.width - PanBorderThickness)
                pos.x += PanSpeed * Time.deltaTime;
            if (Input.mousePosition.x <= PanBorderThickness)
                pos.x -= PanSpeed * Time.deltaTime;
        }

        // --- SINIRLANDIRMA (CLAMP) ---
        if (LimitToMap)
        {
            pos.x = Mathf.Clamp(pos.x, MinLimit.x, MaxLimit.x);
            pos.y = Mathf.Clamp(pos.y, MinLimit.y, MaxLimit.y);
        }

        transform.position = pos;
    }

    void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");

        if (scroll != 0f)
        {
            float newSize = _cam.orthographicSize - scroll * ZoomSpeed;
            _cam.orthographicSize = Mathf.Clamp(newSize, MinZoom, MaxZoom);

            // Zoom yaptıkça sınırları güncellemek gerekebilir (Opsiyonel detay)
        }
    }

    // Harita boyutuna göre sınırları otomatik hesapla
    public void CalculateBounds()
    {
        if (ExperimentManager.Instance != null)
        {
            // Harita 50x50 ise, kamera (0,0) ile (50,50) arasında gezebilsin
            // İzometrik olduğu için biraz pay bırakabiliriz.

            float width = ExperimentManager.Instance.MapWidth;
            float height = ExperimentManager.Instance.MapHeight;

            // İzometrik düzlemde görsel genişlik biraz farklı olabilir ama
            // basitçe grid koordinatlarına sadık kalalım.

            // Görselleştirici ayarlarını (TileWidth) hesaba katmak daha doğru olurdu
            // ama şimdilik manuel ayar yapabilmen için geniş bırakıyorum.

            // Eğer Visualizer'a erişebilirsek:
            // float tileW = 2.56f;
            // float tileH = 1.28f;
            // MaxLimit = new Vector2(width * tileW / 2, height * tileH); 

            // Şimdilik Inspector'dan elle ayarlanabilir bırakalım veya geniş bir alan verelim:
            MinLimit = new Vector2(-10, -10);
            MaxLimit = new Vector2(width * 2, height * 2); // Kabaca genişlik
        }
    }
}