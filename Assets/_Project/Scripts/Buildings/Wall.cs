// using UnityEngine;

// /*
//  * Wall.cs
//  * Custom script for the 'Wall' building.
//  * Inherits from the 'Building' base class.
//  *
//  * LOGIC:
//  * 1. This building produces nothing and provides no passive income.
//  * Therefore, we do not need to override 'OnBuildingComplete'.
//  * The base functionality in 'Building.cs' is sufficient.
//  *
//  * 2. We MUST implement the abstract 'Die()' method from 'Building.cs'.
//  * 'Die()' handles the destruction logic (making the node walkable again).
//  */
// public class Wall : Building // Inherits from 'Building' instead of 'MonoBehaviour'
// {
//     // This building does not produce anything or generate passive income,
//     // so we don't need to override 'OnBuildingComplete'.
//     // The base implementation in 'Building.cs' is enough.

//     // We MUST implement the 'Die()' method because 'Building.cs' is abstract.
//     public override void Die()
//     {
//         Debug.Log("Wall destroyed.");

//         // Make the 'Node' (cell) walkable again
//         if (buildingNode != null)
//         {
//             buildingNode.isWalkable = true;
//         }

//         // Destroy the GameObject
//         Destroy(gameObject);
//     }
// }