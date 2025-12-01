// using UnityEngine;
// using System.Collections.Generic;

// /*
//  * Node.cs
//  * This file defines the 'Node' class.
//  * 'Node' is NOT a Unity GameObject.
//  * The 'GridSystem' class will create a 2D array of these 'Node's (like Node[100, 100])
//  * to represent the game map logic.
//  */

// // Defines the 'terrain' type represented by the 'Node'.
// // This controls both walkability and visuals.
// public enum NodeType
// {
//     Grass,      // Walkable
//     Water,      // Unwalkable
//     Forest,     // Unwalkable, Wood resource
//     Stone,      // Unwalkable, Stone resource
//     MeatBush    // Unwalkable, Meat resource
// }

// public class Node
// {
//     // --- Grid Position ---
//     public int x; // X position of the Node in the grid array
//     public int y; // Y position of the Node in the grid array

//     // --- Terrain Data ---
//     public bool isWalkable;
//     public NodeType type; // What type of terrain is this? (Grass, Water...)

//     // --- Occupancy Data ---
//     // (We can use this later to track if there is a 'Unit' or 'Building' on this Node)
//     public object placedObject = null;

//     // --- A* Pathfinding Data ---
//     // These values are helper variables used only by the A* algorithm to find the best path.

//     public int gCost; // Cost from start Node
//     public int hCost; // Heuristic cost to end Node
//     public Node parent; // Previous Node in path (to retrace the path)

//     // Calculated Property: fCost = gCost + hCost
//     // This is the total cost of the Node used by A* to find the best path.
//     public int fCost
//     {
//         get { return gCost + hCost; }
//     }

//     // --- Constructor ---
//     // A shortcut to create a new Node and set initial values.
//     public Node(int x, int y, NodeType type)
//     {
//         this.x = x;
//         this.y = y;
//         this.type = type;

//         // Automatically set walkability ('isWalkable') based on Node type
//         switch (type)
//         {
//             case NodeType.Grass:
//                 this.isWalkable = true;
//                 break;
//             case NodeType.Water:
//                 this.isWalkable = false;
//                 break;
//             case NodeType.Forest:
//                 this.isWalkable = false;
//                 break;
//             case NodeType.Stone:
//                 this.isWalkable = false;
//                 break;
//             // --- NEWLY ADDED ---
//             case NodeType.MeatBush:
//                 this.isWalkable = false; // Meat bushes are also obstacles
//                 break;
//             // --- FINISHED ---
//             default:
//                 this.isWalkable = true;
//                 break;
//         }

//         // Initialize A* costs
//         this.gCost = int.MaxValue;
//         this.hCost = 0;
//         this.parent = null;
//     }

//     // Helper method to reset A* data for a new pathfinding operation.
//     public void ResetAStarData()
//     {
//         this.gCost = int.MaxValue;
//         this.hCost = 0;
//         this.parent = null;
//     }
// }