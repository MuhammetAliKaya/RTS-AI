using UnityEngine;

/*
 * CameraPanZoom.cs
 *
 * FINAL FIX - Zoom-Clamping Stabilized.
 * * BUG FIX (Zoom Bug):
 * 1. The clamping calculation for limits relies on 'targetZoomSize' (target zoom)
 * instead of the camera's current 'orthographicSize'.
 * This ensures the clamping is calculated based on where the camera IS GOING TO BE,
 * preventing jitter or getting stuck when zooming out near edges.
 * 2. 'mapBounds' restricts the camera movement so it doesn't show outside the map.
 */
public class CameraPanZoom : MonoBehaviour
{
    [Header("Pan Settings")]
    public float panSpeed = 20f;
    public float panSmoothTime = 0.1f;

    [Header("Zoom Settings")]
    public float zoomSpeed = 20f;
    public float zoomSmoothTime = 0.1f;
    public float minZoom = 2f;
    public float maxZoom = 20f;

    private Bounds mapBounds; // Map X/Y min/max bounds

    private Camera cam;
    private Vector3 targetPosition;
    private Vector3 panVelocity = Vector3.zero;
    private float targetZoomSize;
    private float zoomVelocity = 0f;

    void Start()
    {
        cam = GetComponent<Camera>();
        if (!cam.orthographic)
        {
            Debug.LogError("Camera is not 'Orthographic'!", this);
            this.enabled = false;
        }

        // Get map bounds from TilemapVisualizer if available
        // (Wait for Start because TilemapVisualizer initializes in Awake/Start)
        if (TilemapVisualizer.Instance != null)
        {
            mapBounds = TilemapVisualizer.Instance.GetMapBounds();
        }

        targetPosition = transform.position;
        targetZoomSize = cam.orthographicSize;
    }

    void Update()
    {
        // Read Input
        HandlePanInput();
        HandleZoomInput();
    }

    void LateUpdate()
    {
        // First, clamp the 'targetPosition'
        // (This must be done BEFORE applying movement to ensure we aim for a valid spot)
        ClampCameraPosition();

        // Then apply smooth movement and zoom
        ApplyPanSmoothing();
        ApplyZoomSmoothing();
    }

    private void HandlePanInput()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        targetPosition.x += horizontal * panSpeed * Time.deltaTime;
        targetPosition.y += vertical * panSpeed * Time.deltaTime;
    }

    private void HandleZoomInput()
    {
        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        if (scrollInput == 0) return;

        targetZoomSize -= scrollInput * zoomSpeed;
        targetZoomSize = Mathf.Clamp(targetZoomSize, minZoom, maxZoom);
    }

    /// <summary>
    /// Prevents the camera from going outside the map bounds.
    /// Uses 'targetZoomSize' for calculation to ensure stability during zoom.
    /// </summary>
    private void ClampCameraPosition()
    {
        if (mapBounds.size == Vector3.zero) return;

        // 1. Calculate how much of the world the camera sees based on TARGET Zoom.
        // Bug Fix: Using 'targetZoomSize' instead of 'cam.orthographicSize' prevents jitter.
        float camExtentY = targetZoomSize;
        float camExtentX = targetZoomSize * cam.aspect;

        // 2. Clamp the Target Position
        // X Clamping (Left/Right)
        targetPosition.x = Mathf.Clamp(
            targetPosition.x,
            mapBounds.min.x + camExtentX, // Left limit
            mapBounds.max.x - camExtentX  // Right limit
        );

        // Y Clamping (Bottom/Top)
        targetPosition.y = Mathf.Clamp(
            targetPosition.y,
            mapBounds.min.y + camExtentY, // Bottom limit
            mapBounds.max.y - camExtentY  // Top limit
        );
    }

    private void ApplyPanSmoothing()
    {
        // Since 'ClampCameraPosition' already clamped 'targetPosition',
        // we can safely smooth towards it.
        transform.position = Vector3.SmoothDamp(
            transform.position,
            targetPosition,
            ref panVelocity,
            panSmoothTime
        );
    }

    private void ApplyZoomSmoothing()
    {
        cam.orthographicSize = Mathf.SmoothDamp(
            cam.orthographicSize,
            targetZoomSize,
            ref zoomVelocity,
            zoomSmoothTime
        );
    }
}