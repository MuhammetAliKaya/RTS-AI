namespace RTS.Simulation.Data
{
    [System.Serializable]
    public class SimResourceData
    {
        public int ID;
        public SimResourceType Type;
        public int2 GridPosition;
        public int AmountRemaining;
    }
}