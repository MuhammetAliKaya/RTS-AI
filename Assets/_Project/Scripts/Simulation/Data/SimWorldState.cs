using System.Collections.Generic;

namespace RTS.Simulation.Data
{
    [System.Serializable]
    public class SimWorldState
    {
        public int TickCount;
        public SimMapData Map;

        // ARTIK SADECE "UNITS" VAR (Hem Worker hem Soldier buraya girer)
        public Dictionary<int, SimUnitData> Units = new Dictionary<int, SimUnitData>();

        public Dictionary<int, SimBuildingData> Buildings = new Dictionary<int, SimBuildingData>();
        public Dictionary<int, SimResourceData> Resources = new Dictionary<int, SimResourceData>();
        public Dictionary<int, SimPlayerData> Players = new Dictionary<int, SimPlayerData>();

        private int _idCounter = 1;
        public int NextID() => _idCounter++;

        public SimWorldState(int width, int height)
        {
            Map = new SimMapData(width, height);
            TickCount = 0;

            // Oyuncu 1'i başlat
            Players.Add(1, new SimPlayerData { PlayerID = 1, MaxPopulation = 0 });
        }
    }
}