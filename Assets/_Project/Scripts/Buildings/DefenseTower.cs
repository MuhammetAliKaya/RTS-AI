// using UnityEngine;
// using System.Collections; // For Coroutine (Timer)
// using System.Linq; // For LINQ

// /*
//  * DefenseTower.cs (FINAL VERSION - Horizontal Elliptical/Rectangular Range Fixed)
//  *
//  * TASKS:
//  * 1. Search for the nearest enemy within range ('SearchForTarget').
//  * 2. Attack the enemy with a timer ('AttackTarget').
//  * 3. 'SearchForTarget()' simulates a Horizontal Elliptical (or rectangular) range by limiting vertical (Y) distance.
//  */
// public class DefenseTower : Building
// {
//     [Header("Defense Settings")]
//     [Tooltip("Scans for enemies every X seconds.")]
//     public float searchRate = 0.5f;

//     [Tooltip("Main range for the X Axis (Horizontal Radius).")]
//     public float searchRange = 5f; 

//     [Tooltip("Maximum allowed range for the Y Axis (Vertical). Should be smaller than Horizontal range (e.g., 3.5f) to simulate perspective.")]
//     public float verticalRangeLimit = 3.5f;

//     [Tooltip("Damage dealt per shot.")]
//     public int attackDamage = 15;

//     [Tooltip("Number of shots per second.")]
//     public float attackRate = 1.0f;

//     // --- NEWLY ADDED ---
//     [Header("Visuals")]
//     [Tooltip("The 'RangeIndicator' object inside the Prefab.")]
//     public GameObject rangeIndicator;
//     // --- END ---

//     // --- Internal State ---
//     private Unit currentTarget;
//     private float attackTimer;


//     protected override void Start()
//     {
//         base.Start(); // Call Building.cs Start

//         // --- NEWLY ADDED: Resize Range Indicator ---
//         if (rangeIndicator != null)
//         {
//             // X Scale: searchRange * 2 (Full width)
//             float scaleX = searchRange * 2f;

//             // Y Scale: verticalRangeLimit * 2 (Full height)
//             // Multiplied by 1.5f to make the isometric circle look more oval/elliptical visually.
//             float scaleY = verticalRangeLimit * 2f * 1.5f;

//             rangeIndicator.transform.localScale = new Vector3(scaleX, scaleY, 1f);
//             rangeIndicator.SetActive(false);
//         }
//     }

//     // Tower constantly runs Update to work
//     void Update()
//     {
//         // 1. Is the tower active and constructed?
//         if (!isFunctional) return;

//         // 2. Is the target dead or out of range?
//         if (currentTarget != null)
//         {
//             // Calculate distance
//             Vector3 diff = currentTarget.transform.position - transform.position;
//             float absXDistance = Mathf.Abs(diff.x);
//             float absYDistance = Mathf.Abs(diff.y);

//             // If target is dead, or outside X range, or outside Y range -> Lose target
//             if (currentTarget.currentHealth <= 0 || absXDistance > searchRange || absYDistance > verticalRangeLimit)
//             {
//                 currentTarget = null;
//                 if (rangeIndicator != null) rangeIndicator.SetActive(false);
//             }
//         }

//         // 3. Search for new target
//         if (currentTarget == null)
//         {
//             SearchForTarget();
//             if (currentTarget != null && rangeIndicator != null)
//             {
//                 rangeIndicator.SetActive(true);
//             }
//         }

//         // 4. Attack Logic
//         if (currentTarget != null)
//         {
//             AttackTarget();
//         }
//     }

//     /// <summary>
//     /// Searches for the nearest enemy unit within the rectangular bounds defined by searchRange and verticalRangeLimit.
//     /// (UPDATED: Uses manual check instead of Physics2D.OverlapCircle to enforce Rectangular/Elliptical bounds)
//     /// </summary>
//     private void SearchForTarget()
//     {
//         // NOTE: Instead of Physics2D.OverlapCircleAll (which is circular), we search all units and manually check bounds.
//         Unit[] allUnits = FindObjectsByType<Unit>(FindObjectsSortMode.None);

//         Unit closestEnemy = null;
//         float minDistanceScore = float.MaxValue;

//         foreach (Unit unit in allUnits)
//         {
//             // Skip self or units of the same player
//             if (unit == null || unit.playerID == this.playerID)
//             {
//                 continue;
//             }

//             Vector3 diff = unit.transform.position - transform.position;
//             float absXDistance = Mathf.Abs(diff.x);
//             float absYDistance = Mathf.Abs(diff.y);


//             // --- CRITICAL CHECK: IS INSIDE RECTANGULAR BOUNDARY? ---
//             bool inXRange = absXDistance <= searchRange;
//             bool inYRange = absYDistance <= verticalRangeLimit;


//             if (inXRange && inYRange)
//             {
//                 // If inside the "box", use Euclidean distance to find the closest one among them.
//                 float currentDistanceScore = Vector2.Distance(transform.position, unit.transform.position);

//                 if (currentDistanceScore < minDistanceScore)
//                 {
//                     minDistanceScore = currentDistanceScore;
//                     closestEnemy = unit;
//                 }
//             }
//         }

//         currentTarget = closestEnemy;
//     }

//     /// <summary>
//     /// Manages the attack timer and deals damage to the target.
//     /// </summary>
//     private void AttackTarget()
//     {
//         attackTimer += Time.deltaTime;

//         if (attackTimer >= (1f / attackRate))
//         {
//             attackTimer = 0f;

//             Debug.Log($"[Tower] Dealt {attackDamage} damage to {currentTarget.name}!");

//             currentTarget.TakeDamage(attackDamage);
//         }
//     }


//     // --- BUILDING BASE CLASS MANDATORY METHODS ---
//     public override void Die()
//     {
//         Debug.Log("Defense Tower destroyed.");
//         if (buildingNode != null)
//         {
//             buildingNode.isWalkable = true;
//         }
//         Destroy(gameObject);
//     }
// }