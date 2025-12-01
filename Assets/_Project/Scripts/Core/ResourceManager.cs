// using UnityEngine;
// using System.Collections.Generic;
// using System.Linq;

// public class ResourceManager : MonoBehaviour
// {
//     private List<PlayerResourceData> playersResources = new List<PlayerResourceData>();

//     void Awake()
//     {
//         // We move data initialization to the VERY BEGINNING, into Awake.
//         // This ensures other scripts can access it in their Start() methods.
//         if (playersResources.Count == 0)
//         {
//             LoadState(new ResourceData());
//         }
//     }

//     void Start()
//     {
//         // Start can now remain empty or be used for other operations.
//     }

//     public PlayerResourceData GetPlayerResources(int playerID)
//     {
//         // Safety check: If the list is still empty (unlikely but possible), try loading again.
//         if (playersResources == null || playersResources.Count == 0)
//         {
//             LoadState(new ResourceData());
//         }

//         return playersResources.FirstOrDefault(p => p.playerID == playerID);
//     }

//     public void LoadState(ResourceData data)
//     {
//         playersResources = data.players;
//         // Debug.Log("Resource state loaded."); // Commented out to prevent log clutter.
//     }

//     public ResourceData GetState()
//     {
//         ResourceData data = new ResourceData();
//         data.players = playersResources;
//         return data;
//     }

//     public bool CanAfford(int playerID, int woodCost, int stoneCost, int meatCost)
//     {
//         PlayerResourceData data = GetPlayerResources(playerID);
//         if (data == null) return false;
//         return data.wood >= woodCost && data.stone >= stoneCost && data.meat >= meatCost;
//     }

//     public void AddResource(int playerID, ResourceType type, int amount)
//     {
//         PlayerResourceData data = GetPlayerResources(playerID);
//         if (data == null || amount <= 0) return;

//         if (type == ResourceType.Wood) { data.wood += amount; }
//         else if (type == ResourceType.Stone) { data.stone += amount; }
//         else if (type == ResourceType.Meat) { data.meat += amount; }
//     }

//     public bool SpendResources(int playerID, int woodCost, int stoneCost, int meatCost)
//     {
//         PlayerResourceData data = GetPlayerResources(playerID);
//         if (data == null) return false;

//         if (CanAfford(playerID, woodCost, stoneCost, meatCost))
//         {
//             data.wood -= woodCost;
//             data.stone -= stoneCost;
//             data.meat -= meatCost;
//             return true;
//         }
//         return false;
//     }

//     public void AddPopulation(int playerID, int amount)
//     {
//         PlayerResourceData data = GetPlayerResources(playerID);
//         if (data != null) data.currentPopulation += amount;
//     }

//     public void RemovePopulation(int playerID, int amount)
//     {
//         PlayerResourceData data = GetPlayerResources(playerID);
//         if (data != null) data.currentPopulation -= amount;
//     }

//     public void IncreaseMaxPopulation(int playerID, int amount)
//     {
//         PlayerResourceData data = GetPlayerResources(playerID);
//         if (data != null) data.maxPopulation += amount;
//     }

//     // RL Environment helper methods
//     public void SetResources(int playerID, int wood, int stone, int meat)
//     {
//         PlayerResourceData data = GetPlayerResources(playerID);
//         if (data != null)
//         {
//             data.wood = wood;
//             data.stone = stone;
//             data.meat = meat;
//         }
//     }

//     public void SetPopulation(int playerID, int current)
//     {
//         PlayerResourceData data = GetPlayerResources(playerID);
//         if (data != null)
//         {
//             data.currentPopulation = current;
//         }
//     }

//     public void ResetPlayerForTraining(int playerID)
//     {
//         PlayerResourceData data = GetPlayerResources(playerID);
//         if (data != null)
//         {
//             data.wood = 0;
//             data.stone = 0;
//             data.meat = 50; // Baþlangýç kaynaðýn neyse
//             data.currentPopulation = 0;
//             data.maxPopulation = 10; // VEYA BAÞLANGIÇ LÝMÝTÝN NEYSE (Örn: Base binasý 5 veriyorsa)
//         }
//     }
// }

// public enum ResourceType
// {
//     Wood,
//     Stone,
//     Meat
// }