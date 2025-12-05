using RTS.Simulation.Data;

namespace RTS.Simulation.Systems
{
    public static class SimResourceSystem
    {
        // Oyuncunun verisine güvenli erişim
        public static SimPlayerData GetPlayer(SimWorldState world, int playerID)
        {
            if (world.Players.TryGetValue(playerID, out SimPlayerData data))
            {
                return data;
            }
            return null;
        }

        // --- SORGULAMA ---
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

        // --- İŞLEMLER ---
        public static bool SpendResources(SimWorldState world, int playerID, int wood, int stone, int meat)
        {
            if (!CanAfford(world, playerID, wood, stone, meat)) return false;

            SimPlayerData data = GetPlayer(world, playerID);
            data.Wood -= wood;
            data.Stone -= stone;
            data.Meat -= meat;
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
        }

        // --- NÜFUS ---
        public static void ModifyPopulation(SimWorldState world, int playerID, int amount)
        {
            SimPlayerData data = GetPlayer(world, playerID);
            if (data != null)
            {
                data.CurrentPopulation += amount;
                // Negatif nüfus olmasın
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