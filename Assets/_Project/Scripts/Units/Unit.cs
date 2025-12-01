// using UnityEngine;
// using System.Collections.Generic; // Required for List
// using System.Collections;         // Required for Coroutine

// /*
//  * Unit.cs
//  * Abstract base class for all moving units (Workers, Soldiers, etc.).
//  * Handles movement (A* pathfinding), health, visuals, and population management.
//  */

// public abstract class Unit : MonoBehaviour
// {
//     [Header("Unit Stats")]
//     public float moveSpeed = 5f;
//     public int maxHealth = 100;
//     public int currentHealth { get; protected set; }

//     [Header("Ownership")]
//     public int playerID = 1;
//     public bool IsBusy { get; protected set; } = false;

//     [Header("Visuals")]
//     public List<Color> playerTints;
//     public GameObject selectionVisual;

//     [Header("Pathfinding")]
//     protected List<Node> currentPath;
//     protected int currentPathIndex;

//     [Header("System References")]
//     protected GridSystem gridSystem;
//     protected AStarPathfinder pathfinder;
//     protected SpriteRenderer spriteRenderer;

//     private Color originalPlayerColor;

//     // --- INITIALIZATION ---
//     protected virtual void Awake()
//     {
//         // Get SpriteRenderer for all units (used for tinting and feedback)
//         spriteRenderer = GetComponentInChildren<SpriteRenderer>();
//         if (spriteRenderer == null)
//         {
//             spriteRenderer = GetComponent<SpriteRenderer>();
//         }
//     }

//     protected virtual void Start()
//     {
//         currentHealth = maxHealth;

//         if (TilemapVisualizer.Instance == null)
//         {
//             Debug.LogError("FATAL ERROR: TilemapVisualizer.Instance is null.", this);
//             return;
//         }
//         gridSystem = TilemapVisualizer.Instance.gridSystem;
//         pathfinder = TilemapVisualizer.Instance.pathfinder;

//         // Set Player Color Tint
//         if (spriteRenderer != null)
//         {
//             int colorIndex = playerID - 1;
//             if (playerTints != null && playerTints.Count > colorIndex && colorIndex >= 0)
//             {
//                 originalPlayerColor = playerTints[colorIndex];
//                 spriteRenderer.color = originalPlayerColor;
//             }
//             else
//             {
//                 originalPlayerColor = Color.white;
//             }
//         }

//         if (selectionVisual != null)
//         {
//             selectionVisual.SetActive(false);
//         }

//         // Increase population count on spawn
//         if (GameManager.Instance != null)
//         {
//             GameManager.Instance.resourceManager.AddPopulation(this.playerID, 1);
//         }
//     }

//     protected virtual void Update()
//     {
//         HandleMovement();
//     }

//     /// <summary>
//     /// Moves the unit along the calculated path.
//     /// </summary>
//     protected virtual void HandleMovement()
//     {
//         if (currentPath == null || currentPathIndex >= currentPath.Count)
//         {
//             IsBusy = false;
//             return;
//         }

//         IsBusy = true;

//         Node targetNode = currentPath[currentPathIndex];
//         Vector3 targetPosition = TilemapVisualizer.Instance.WorldPositionFromNode(targetNode);

//         // Move towards the next node in the path
//         transform.position = Vector3.MoveTowards(
//             transform.position,
//             targetPosition,
//             moveSpeed * Time.deltaTime
//         );

//         // Check if reached the node (within small distance)
//         if (Vector3.Distance(transform.position, targetPosition) < 0.1f)
//         {
//             currentPathIndex++;
//             // If reached the end of the path
//             if (currentPathIndex >= currentPath.Count)
//             {
//                 transform.position = targetPosition; // Snap to exact position
//                 currentPath = null;
//                 currentPathIndex = 0;
//             }
//         }
//     }

//     /// <summary>
//     /// Calculates a path to the target world position using A*.
//     /// Returns true if a path is found.
//     /// </summary>
//     public virtual bool MoveTo(Vector3 targetWorldPosition)
//     {
//         if (gridSystem == null || pathfinder == null) return false;

//         Node startNode = TilemapVisualizer.Instance.NodeFromWorldPoint(transform.position);
//         Node endNode = TilemapVisualizer.Instance.NodeFromWorldPoint(targetWorldPosition);

//         if (startNode == null || endNode == null) return false;

//         // If already at target
//         if (startNode == endNode) 
//         { 
//             currentPath = null; 
//             currentPathIndex = 0; 
//             return true; 
//         }

//         // If target is unwalkable
//         if (!endNode.isWalkable) return false;

//         // Calculate Path
//         currentPath = pathfinder.FindPath(gridSystem, startNode, endNode);

//         if (currentPath != null && currentPath.Count > 0)
//         {
//             currentPathIndex = 0;
//             IsBusy = true;
//             return true;
//         }
//         else
//         {
//             currentPath = null;
//             return false;
//         }
//     }

//     public virtual void TakeDamage(int damage)
//     {
//         currentHealth -= damage;

//         StopCoroutine("FlashRed");
//         StartCoroutine(FlashRed());

//         if (currentHealth <= 0)
//         {
//             Die();
//         }
//     }

//     private IEnumerator FlashRed()
//     {
//         if (spriteRenderer == null) yield break;

//         spriteRenderer.color = Color.red;
//         yield return new WaitForSeconds(0.1f);

//         if (spriteRenderer != null)
//         {
//             spriteRenderer.color = originalPlayerColor;
//         }
//     }

//     public void ShowSelection(bool isSelected)
//     {
//         if (selectionVisual != null)
//         {
//             selectionVisual.SetActive(isSelected);
//         }
//     }

//     public virtual void Die()
//     {
//         // Decrease population count on death
//         if (GameManager.Instance != null)
//         {
//             GameManager.Instance.resourceManager.RemovePopulation(this.playerID, 1);
//         }
//         Destroy(gameObject);
//     }

//     /// <summary>
//     /// Sets the unit's sprite color/alpha. Used for visual effects (e.g., ghost building).
//     /// </summary>
//     public void SetPlayerTint(float alpha)
//     {
//         if (spriteRenderer == null) return;

//         int colorIndex = playerID - 1;
//         Color newColor = Color.white;

//         if (playerTints != null && playerTints.Count > colorIndex && colorIndex >= 0)
//         {
//             newColor = playerTints[colorIndex];
//         }

//         newColor.a = alpha;
//         spriteRenderer.color = newColor;
//     }

//     public virtual bool IsIdle()
//     {
//         if (currentPath != null) return false;
//         return true;
//     }
// }