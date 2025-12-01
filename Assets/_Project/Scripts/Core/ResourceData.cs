// using System.Collections.Generic;

// /*
//  * ResourceData.cs
//  *
//  * Holds the master resource data for ALL players.
//  * It contains a list of 'PlayerResourceData' objects.
//  * This class is typically used for Saving/Loading game state.
//  */

// [System.Serializable]
// public class ResourceData
// {
//     public List<PlayerResourceData> players = new List<PlayerResourceData>();

//     /// <summary>
//     /// Constructor.
//     /// Creates default data for Player 1 (Human) and Player 2 (AI) by default.
//     /// </summary>
//     public ResourceData()
//     {
//         // Player 1 (Human / Us)
//         players.Add(new PlayerResourceData(1));

//         // Player 2 (AI)
//         players.Add(new PlayerResourceData(2));
//     }
// }