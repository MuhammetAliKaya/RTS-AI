// using System.Collections.Generic; // Required for List<T> and HashSet<T>
// using UnityEngine; // Required for Mathf.Abs

// /*
//  * AStarPathfinder.cs
//  * This is a "service" or "calculator" class.
//  * It is NOT a MonoBehaviour.
//  * Its only job is to calculate the shortest path in a given GridSystem
//  * using the A* (A-Star) algorithm.
//  */
// public class AStarPathfinder
// {
//     /// <summary>
//     /// Finds the shortest path between two nodes using the A* algorithm.
//     /// </summary>
//     /// <param name="grid">The GridSystem (map) to search in.</param>
//     /// <param name="startNode">The starting node.</param>
//     /// <param name="endNode">The target node.</param>
//     /// <returns>A List of Nodes representing the path, or null if no path is found.</returns>
//     public List<Node> FindPath(GridSystem grid, Node startNode, Node endNode)
//     {
//         // 1. Initialize the lists
//         List<Node> openList = new List<Node>(); // Nodes to be evaluated
//         HashSet<Node> closedSet = new HashSet<Node>(); // Nodes already evaluated

//         // 2. Add the starting node to the openList
//         openList.Add(startNode);

//         // Reset all node A* data before starting a new pathfind to avoid dirty data from previous runs
//         for (int x = 0; x < grid.width; x++)
//         {
//             for (int y = 0; y < grid.height; y++)
//             {
//                 grid.GetNode(x, y)?.ResetAStarData();
//             }
//         }

//         // Set the starting node's costs
//         startNode.gCost = 0;
//         startNode.hCost = GetDistance(startNode, endNode);

//         // 3. The main A* loop
//         while (openList.Count > 0)
//         {
//             // A* Step 3: Find the node in the openList with the LOWEST fCost
//             Node currentNode = openList[0];
//             for (int i = 1; i < openList.Count; i++)
//             {
//                 if (openList[i].fCost < currentNode.fCost ||
//                     (openList[i].fCost == currentNode.fCost && openList[i].hCost < currentNode.hCost))
//                 {
//                     currentNode = openList[i];
//                 }
//             }

//             // A* Step 4: Move the best node from openList to closedSet
//             openList.Remove(currentNode);
//             closedSet.Add(currentNode);

//             // A* Step 5: Check if we found the target
//             if (currentNode == endNode)
//             {
//                 // PATH FOUND! Retrace the path from end to start
//                 return RetracePath(startNode, endNode);
//             }

//             // A* Step 6 & 7: Check all neighbors
//             foreach (Node neighbour in GetNeighbours(grid, currentNode))
//             {
//                 // Skip if the neighbour is not walkable or is already checked
//                 if (!neighbour.isWalkable || closedSet.Contains(neighbour))
//                 {
//                     continue;
//                 }

//                 // Calculate the new G cost to this neighbour
//                 // (GetDistance provides the cost: 10 for straight, 14 for diagonal)
//                 int newMovementCostToNeighbour = currentNode.gCost + GetDistance(currentNode, neighbour);

//                 // If this path to the neighbour is shorter, or neighbor is not in openList
//                 if (newMovementCostToNeighbour < neighbour.gCost || !openList.Contains(neighbour))
//                 {
//                     // Update the neighbour's costs and parent
//                     neighbour.gCost = newMovementCostToNeighbour;
//                     neighbour.hCost = GetDistance(neighbour, endNode);
//                     neighbour.parent = currentNode; // Set the "breadcrumb" to retrace later

//                     // Add to openList if it's not already there
//                     if (!openList.Contains(neighbour))
//                     {
//                         openList.Add(neighbour);
//                     }
//                 }
//             }
//         }

//         return null; // No path found
//     }

//     /// <summary>
//     /// Retraces the path from the end node back to the start node using the 'parent' links.
//     /// </summary>
//     private List<Node> RetracePath(Node startNode, Node endNode)
//     {
//         List<Node> path = new List<Node>();
//         Node currentNode = endNode;

//         while (currentNode != startNode)
//         {
//             path.Add(currentNode);
//             currentNode = currentNode.parent; // Follow the "breadcrumbs" back
//         }
//         path.Reverse(); // Reverse the list to get path from start -> end
//         return path;
//     }

//     /// <summary>
//     /// Gets all 8 adjacent neighbours of a node (including diagonals).
//     /// </summary>
//     private List<Node> GetNeighbours(GridSystem grid, Node node)
//     {
//         List<Node> neighbours = new List<Node>();

//         for (int x = -1; x <= 1; x++)
//         {
//             for (int y = -1; y <= 1; y++)
//             {
//                 if (x == 0 && y == 0) continue; // This is the node itself

//                 int checkX = node.x + x;
//                 int checkY = node.y + y;

//                 Node neighbour = grid.GetNode(checkX, checkY);
//                 if (neighbour != null)
//                 {
//                     neighbours.Add(neighbour);
//                 }
//             }
//         }
//         return neighbours;
//     }

//     /// <summary>
//     /// Calculates the distance (cost) between two nodes for A*.
//     /// Uses 10 for horizontal/vertical and 14 for diagonal moves (approximation of sqrt(2)*10).
//     /// </summary>
//     private int GetDistance(Node nodeA, Node nodeB)
//     {
//         int dstX = Mathf.Abs(nodeA.x - nodeB.x);
//         int dstY = Mathf.Abs(nodeA.y - nodeB.y);

//         if (dstX > dstY)
//             return 14 * dstY + 10 * (dstX - dstY);
//         return 14 * dstX + 10 * (dstY - dstX);
//     }
// }