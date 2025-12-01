// using UnityEngine;
// using UnityEngine.EventSystems;
// using System.Linq; // For 'OrderBy' (Sorting)

// /*
//  * InputManager.cs
//  * Controls Player 1's interactions (Selection and Commands).
//  *
//  * NOTE: This script only controls Player 1.
//  * Player 2 is controlled by 'EnemyAIController.cs'.
//  */
// public class InputManager : MonoBehaviour
// {
//     [Header("Selection")]
//     [Tooltip("The currently selected Unit (via Left Click).")]
//     public Unit selectedUnit;

//     [Tooltip("The currently selected Building (via Left Click).")]
//     public Building selectedBuilding;

//     [Header("System References")]
//     public Camera mainCamera;

//     [Header("Layer Masks")]
//     public LayerMask groundLayerMask;
//     public LayerMask resourceLayerMask;
//     public LayerMask unitLayerMask;
//     public LayerMask buildingLayerMask;

//     // Integer values for Layers
//     private int groundLayer;
//     private int resourceLayer;
//     private int unitLayer;
//     private int buildingLayer;

//     [Header("Player Settings")]
//     public int myPlayerID = 1; // Controls only Player 1

//     void Start()
//     {
//         mainCamera = Camera.main;
//         if (mainCamera == null) 
//         { 
//             Debug.LogError("[InputManager] ERROR: 'Main Camera' not found!", this); 
//             this.enabled = false; 
//             return; 
//         }

//         if (groundLayerMask == 0 || resourceLayerMask == 0 || unitLayerMask == 0 || buildingLayerMask == 0)
//         {
//             Debug.LogError("[InputManager] ERROR: One or more LayerMasks are not assigned in the Inspector!", this);
//             this.enabled = false;
//             return;
//         }

//         groundLayer = LayerMask.NameToLayer("Ground");
//         resourceLayer = LayerMask.NameToLayer("Resource");
//         unitLayer = LayerMask.NameToLayer("Unit");
//         buildingLayer = LayerMask.NameToLayer("Building");
//     }

//     void Update()
//     {
//         // --- LEFT CLICK (SELECTION) ---
//         if (Input.GetMouseButtonDown(0))
//         {
//             // Ignore if clicking on UI
//             if (EventSystem.current.IsPointerOverGameObject()) { return; }

//             // Ignore if placing a building
//             if (BuildingPlacer.Instance != null && BuildingPlacer.Instance.isPlacingBuilding) { return; }

//             Collider2D topHit = GetTopHitAtMousePos();

//             if (topHit != null)
//             {
//                 int hitLayer = topHit.gameObject.layer;

//                 if (hitLayer == unitLayer)
//                 {
//                     Unit clickedUnit = topHit.GetComponent<Unit>();
//                     // ONLY select our own units
//                     if (clickedUnit != null && clickedUnit.playerID == myPlayerID)
//                     {
//                         SelectUnit(clickedUnit);
//                     }
//                     else
//                     {
//                         DeselectAll();
//                     }
//                 }
//                 else if (hitLayer == buildingLayer)
//                 {
//                     // ONLY select our own buildings
//                     Building clickedBuilding = topHit.GetComponent<Building>();
//                     if (clickedBuilding != null && clickedBuilding.playerID == myPlayerID)
//                     {
//                         SelectBuilding(clickedBuilding);
//                     }
//                     else
//                     {
//                         DeselectAll();
//                     }
//                 }
//                 else
//                 {
//                     DeselectAll();
//                 }
//             }
//             else
//             {
//                 DeselectAll();
//             }
//         }

//         // --- RIGHT CLICK (COMMANDS) ---
//         if (Input.GetMouseButtonDown(1))
//         {
//             if (EventSystem.current.IsPointerOverGameObject()) { return; }
//             if (selectedUnit == null) { return; }

//             Collider2D topHit = GetTopHitAtMousePos();
//             if (topHit == null) return;

//             int hitLayer = topHit.gameObject.layer;

//             // ... Command Logic based on what we clicked ...
//             if (hitLayer == unitLayer)
//             {
//                 Unit targetUnit = topHit.GetComponent<Unit>();
//                 if (targetUnit != null && targetUnit.playerID != selectedUnit.playerID)
//                 {
//                     if (selectedUnit is Soldier)
//                     {
//                         (selectedUnit as Soldier).Attack(targetUnit);
//                     }
//                 }
//             }
//             else if (hitLayer == resourceLayer)
//             {
//                 if (selectedUnit is Worker)
//                 {
//                     (selectedUnit as Worker).Gather(topHit.GetComponent<ResourceNode>());
//                 }
//             }
//             else if (hitLayer == groundLayer)
//             {
//                 Vector3 clickPoint = topHit.ClosestPoint(mainCamera.ScreenToWorldPoint(Input.mousePosition));
//                 clickPoint.z = 0;
//                 selectedUnit.MoveTo(clickPoint);
//             }
//             else if (hitLayer == buildingLayer)
//             {
//                 Building targetBuilding = topHit.GetComponent<Building>();

//                 // ATTACK BUILDING COMMAND
//                 if (selectedUnit is Soldier && targetBuilding != null && targetBuilding.playerID != myPlayerID)
//                 {
//                     (selectedUnit as Soldier).AttackBuilding(targetBuilding);
//                     return;
//                 }
//             }
//         }
//     }

//     // --- HELPER FUNCTIONS ---

//     private Collider2D GetTopHitAtMousePos()
//     {
//         Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(Input.mousePosition);

//         // Get all colliders at the mouse position
//         Collider2D[] hits = Physics2D.OverlapPointAll(mouseWorldPos,
//             groundLayerMask | resourceLayerMask | unitLayerMask | buildingLayerMask);

//         if (hits.Length == 0) { return null; }

//         // Sort by priority: Unit/Building > Resource > Ground
//         return hits.OrderByDescending(hit =>
//         {
//             if (hit.gameObject.layer == unitLayer) return 3;
//             if (hit.gameObject.layer == buildingLayer) return 3;
//             if (hit.gameObject.layer == resourceLayer) return 2;
//             if (hit.gameObject.layer == groundLayer) return 1;
//             return 0;
//         }).First();
//     }

//     private void SelectUnit(Unit unit)
//     {
//         if (selectedUnit == unit) return;
//         DeselectAll();
//         selectedUnit = unit;
//         Debug.Log($"[InputManager] UNIT SELECTED: {unit.name}");
//         selectedUnit.ShowSelection(true);
//         UIManager.Instance.HideAllProductionPanels();
//     }

//     private void SelectBuilding(Building building)
//     {
//         if (selectedBuilding == building) return;
//         DeselectAll();
//         selectedBuilding = building;
//         Debug.Log($"[InputManager] BUILDING SELECTED: {building.name}");
//         UIManager.Instance.ShowBuildingPanel(building);
//     }

//     private void DeselectAll()
//     {
//         if (selectedUnit != null)
//         {
//             selectedUnit.ShowSelection(false);
//             selectedUnit = null;
//         }
//         if (selectedBuilding != null)
//         {
//             selectedBuilding = null;
//         }

//         if (UIManager.Instance != null)
//         {
//             UIManager.Instance.HideAllProductionPanels();
//         }
//     }
// }