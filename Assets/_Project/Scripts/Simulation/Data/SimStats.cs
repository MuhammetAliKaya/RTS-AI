namespace RTS.Simulation.Data
{
    [System.Serializable]
    public struct SimStats
    {
        // Temel Sonuçlar
        public float Fitness;
        public bool IsWin;
        public float MatchDuration;

        // Ekonomi
        public int GatheredWood;
        public int GatheredMeat;
        public int GatheredStone;
        public int WorkerCount;

        // Askeri
        public int SoldierCount;
        public int TowersBuilt;
        public int BarracksBuilt;

        // Savaş
        public float DamageDealt;
        public float DamageTaken;
        public float BaseHealthRemaining;
    }
}