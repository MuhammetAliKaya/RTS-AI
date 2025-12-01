// using UnityEngine;
// using UnityEngine.EventSystems;

// /*
//  * BuildingPlacer.cs
//  * Handles the logic for placing buildings from the UI.
//  *
//  * UPDATED VERSION 5 - COST CONTROL:
//  * 1. 'StartPlacingBuilding()' is called by the UI Button.
//  * - Checks if a Worker is selected.
//  * - Checks and SPENDS resources immediately using 'ResourceManager.SpendResources()'.
//  * - If successful, enters placement mode (Ghost building follows mouse).
//  * 2. 'Update()' moves the ghost.
//  * 3. Left Click calls 'TryPlaceBuilding()'.
//  * - Checks if the Worker can move to the target node.
//  * - If yes, assigns the building task to the Worker via 'StartBuildingTask'.
//  * - The Worker will go there and construct it.
//  * 4. Right Click calls 'CancelPlacingBuilding()'.
//  * - TODO: REFUND resources if cancelled? Currently resources are lost if cancelled here.
//  */
// public class BuildingPlacer : MonoBehaviour
// {
//     // --- Singleton ---
//     public static BuildingPlacer Instance { get; private set; }

//     [Header("System References")]
//     private Camera mainCamera;

//     [Header("Building Ghost")]
//     public Color validColor = new Color(0, 1, 0, 0.5f);
//     public Color invalidColor = new Color(1, 0, 0, 0.5f);

//     // --- Internal State ---
//     public bool isPlacingBuilding { get; private set; } = false;
//     private GameObject buildingPrefabToPlace;
//     private GameObject ghostBuildingInstance;
//     private SpriteRenderer ghostSpriteRenderer;
//     private Node lastValidNode;

//     void Awake()
//     {
//         if (Instance != null && Instance != this) { Destroy(gameObject); }
//         else { Instance = this; }
//     }

//     void Start()
//     {
//         mainCamera = Camera.main;
//         if (mainCamera == null)
//         {
//             Debug.LogError("[BuildingPlacer] Main Camera not found!", this);
//             this.enabled = false;
//         }
//     }

//     /// <summary>
//     /// MAIN COMMAND CALLED EXTERNALLY (UI BUTTON CALLS THIS).
//     /// UPDATED: Checks cost and spends resources immediately.
//     /// </summary>
//     public void StartPlacingBuilding(GameObject prefab)
//     {
//         // 1. Check if a Worker is selected
//         InputManager inputManager = FindFirstObjectByType<InputManager>();
//         if (inputManager == null || inputManager.selectedUnit == null || !(inputManager.selectedUnit is Worker))
//         {
//             Debug.LogWarning("You must select a 'Worker' first to construct a building!");
//             return;
//         }

//         Building prefabBuilding = prefab.GetComponent<Building>();
//         if (prefabBuilding == null)
//         {
//             Debug.LogError($"[BuildingPlacer] 'Building.cs' script not found on Prefab '{prefab.name}'!");
//             return;
//         }

//         // --- NEWLY ADDED (Cost Control & Spending) ---
//         if (prefabBuilding.cost.HasAnyCost())
//         {
//             Unit selectedUnit = inputManager.selectedUnit;

//             // Spend resources immediately
//             bool success = GameManager.Instance.resourceManager.SpendResources(
//                 selectedUnit.playerID,
//                 prefabBuilding.cost.woodCost,
//                 prefabBuilding.cost.stoneCost,
//                 prefabBuilding.cost.meatCost
//             );

//             if (!success)
//             {
//                 Debug.LogWarning($"[BuildingPlacer] Not enough resources! Construction failed. Required: {prefabBuilding.cost.woodCost} Wood, {prefabBuilding.cost.stoneCost} Stone.");
//                 // TODO: Show Visual feedback to player via UIManager
//                 return; // Do NOT start placement mode
//             }
//             // Success: Resources spent, proceed.
//         }
//         // --- CHECK DONE ---

//         if (isPlacingBuilding)
//         {
//             CancelPlacingBuilding();
//         }

//         isPlacingBuilding = true;
//         buildingPrefabToPlace = prefab;

//         // Create Ghost
//         ghostBuildingInstance = Instantiate(buildingPrefabToPlace);

//         // Disable logic on ghost (Building script & Collider)
//         if (ghostBuildingInstance.GetComponent<Building>())
//             ghostBuildingInstance.GetComponent<Building>().enabled = false;
//         if (ghostBuildingInstance.GetComponent<Collider2D>())
//             ghostBuildingInstance.GetComponent<Collider2D>().enabled = false;

//         ghostSpriteRenderer = ghostBuildingInstance.GetComponentInChildren<SpriteRenderer>();
//         if (ghostSpriteRenderer == null)
//         {
//             Debug.LogError($"SpriteRenderer not found on ghost building '{ghostBuildingInstance.name}'!");
//             CancelPlacingBuilding();
//         }
//     }

//     /// <summary>
//     /// Cancels placement mode and destroys the ghost.
//     /// TODO: Should REFUND resources if cancelled here.
//     /// </summary>
//     private void CancelPlacingBuilding()
//     {
//         if (ghostBuildingInstance != null)
//         {
//             Destroy(ghostBuildingInstance);
//         }
//         isPlacingBuilding = false;
//         buildingPrefabToPlace = null;
//         ghostSpriteRenderer = null;
//         lastValidNode = null;
//     }

//     void Update()
//     {
//         if (!isPlacingBuilding)
//         {
//             return;
//         }

//         // 1. Get Mouse World Position
//         Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
//         mouseWorldPos.z = 0f;

//         // 2. Find Node
//         Node targetNode = TilemapVisualizer.Instance.NodeFromWorldPoint(mouseWorldPos);

//         if (targetNode == null)
//         {
//             ghostSpriteRenderer.color = invalidColor;
//             lastValidNode = null;
//             return;
//         }

//         // 3. Snap Ghost to Node Center
//         Vector3 targetNodeCenter = TilemapVisualizer.Instance.WorldPositionFromNode(targetNode);
//         ghostBuildingInstance.transform.position = targetNodeCenter;

//         // 4. Check Validity and Change Color
//         bool canBuild = targetNode.isWalkable;

//         if (canBuild)
//         {
//             ghostSpriteRenderer.color = validColor;
//             lastValidNode = targetNode;
//         }
//         else
//         {
//             ghostSpriteRenderer.color = invalidColor;
//             lastValidNode = null;
//         }

//         // 5. Confirm Construction (Left Click)
//         if (Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject())
//         {
//             if (lastValidNode != null)
//             {
//                 TryPlaceBuilding(lastValidNode);
//             }
//             else
//             {
//                 Debug.LogWarning("You cannot build here!");
//             }
//         }

//         // 6. Cancel (Right Click)
//         if (Input.GetMouseButtonDown(1))
//         {
//             CancelPlacingBuilding();
//         }
//     }

//     /// <summary>
//     /// Asks the selected Worker if they can travel to the node.
//     /// If yes, assigns the task. Does NOT instantiate the building here (Worker handles it).
//     /// </summary>
//     private void TryPlaceBuilding(Node node)
//     {
//         Worker selectedWorker = FindFirstObjectByType<InputManager>().selectedUnit as Worker;

//         if (selectedWorker == null)
//         {
//             Debug.LogError("ERROR: Selected 'Worker' returned null while expected!");
//             return;
//         }

//         // 1. Ask Worker to start building task
//         bool canWorkerStartJob = selectedWorker.StartBuildingTask(node, buildingPrefabToPlace);

//         // 2. Check result
//         if (canWorkerStartJob)
//         {
//             // SUCCESS: Worker found path and took the job.
//             // Resources were already spent in StartPlacingBuilding.
//             CancelPlacingBuilding(); // Exit placement mode (remove ghost)
//         }
//         else
//         {
//             // FAILURE: Worker could not find path.
//             Debug.LogWarning("'Worker' could not find a path to the build site. Try another location.");
//             // Do NOT cancel placement mode, let player try elsewhere.
//         }
//     }
// }