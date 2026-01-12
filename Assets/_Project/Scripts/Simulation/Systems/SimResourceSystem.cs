using RTS.Simulation.Data;
using RTS.Simulation.Core;
using System; // Action için gerekli

namespace RTS.Simulation.Systems
{
    public class SimResourceSystem
    {
        public static event Action<int, int, SimResourceType> OnResourceGathered;
        private SimWorldState _world;


        public SimResourceSystem(SimWorldState world = null)
        {
            _world = world ?? SimGameContext.ActiveWorld;
        }

        public static Action<int, int, SimResourceType> OnResourceSpent;

        // --- Instance Methods (Tercih Edilen) ---
        public int GetResourceAmount(int playerID, SimResourceType type) => GetResourceAmount(_world, playerID, type);
        public bool SpendResources(int playerID, int w, int s, int m) => SpendResources(_world, playerID, w, s, m);
        public void AddResource(int playerID, SimResourceType type, int amount) => AddResource(_world, playerID, type, amount);
        public bool CanAfford(int playerID, int w, int s, int m) => CanAfford(_world, playerID, w, s, m);
        public bool HasPopulationSpace(int playerID) => HasPopulationSpace(_world, playerID);
        public void IncreaseMaxPopulation(int playerID, int amount) => IncreaseMaxPopulation(_world, playerID, amount);
        public void ModifyPopulation(int playerID, int amount) => ModifyPopulation(_world, playerID, amount);

        // --- Static Helper Methods (Logic Implementation) ---
        public static SimPlayerData GetPlayer(SimWorldState world, int playerID)
        {
            if (world.Players.TryGetValue(playerID, out SimPlayerData data)) return data;
            return null;
        }

        public static bool CanAfford(SimWorldState world, int playerID, int wood, int stone, int meat)
        {
            SimPlayerData data = GetPlayer(world, playerID);
            if (data == null) return false;
            return data.Wood >= wood && data.Stone >= stone && data.Meat >= meat;
        }

        public static bool HasPopulationSpace(SimWorldState world, int playerID)
        {
            SimPlayerData data = GetPlayer(world, playerID);
            return data != null && data.CurrentPopulation < data.MaxPopulation;
        }

        public static int GetResourceAmount(SimWorldState world, int playerID, SimResourceType type)
        {
            SimPlayerData data = GetPlayer(world, playerID);
            if (data == null) return 0;
            switch (type)
            {
                case SimResourceType.Wood: return data.Wood;
                case SimResourceType.Stone: return data.Stone;
                case SimResourceType.Meat: return data.Meat;
                default: return 0;
            }
        }

        public static bool SpendResources(SimWorldState world, int playerID, int wood, int stone, int meat)
        {
            if (!CanAfford(world, playerID, wood, stone, meat)) return false;
            SimPlayerData data = GetPlayer(world, playerID);
            data.Wood -= wood;
            data.Stone -= stone;
            data.Meat -= meat;

            // --- ANALİTİK İÇİN BURAYA EKLE ---
            if (wood > 0) OnResourceSpent?.Invoke(playerID, wood, SimResourceType.Wood);
            if (stone > 0) OnResourceSpent?.Invoke(playerID, stone, SimResourceType.Stone);
            if (meat > 0) OnResourceSpent?.Invoke(playerID, meat, SimResourceType.Meat);
            // ---------------------------------


            return true;
        }

        public static void AddResource(SimWorldState world, int playerID, SimResourceType type, int amount)
        {
            SimPlayerData data = GetPlayer(world, playerID);
            if (data == null || amount <= 0) return;

            switch (type)
            {
                case SimResourceType.Wood: data.Wood += amount; break;
                case SimResourceType.Stone: data.Stone += amount; break;
                case SimResourceType.Meat: data.Meat += amount; break;
            }

            // ARTIK TÜR BİLGİSİNİ DE GÖNDERİYORUZ
            OnResourceGathered?.Invoke(playerID, amount, type);
        }

        public static void ModifyPopulation(SimWorldState world, int playerID, int amount)
        {
            SimPlayerData data = GetPlayer(world, playerID);
            if (data != null)
            {
                data.CurrentPopulation += amount;
                if (data.CurrentPopulation < 0) data.CurrentPopulation = 0;
            }
        }

        public static void IncreaseMaxPopulation(SimWorldState world, int playerID, int amount)
        {
            SimPlayerData data = GetPlayer(world, playerID);
            if (data != null) data.MaxPopulation += amount;
        }
    }
}