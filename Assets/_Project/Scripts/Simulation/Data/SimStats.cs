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

        public int FarmsBuilt;
        public int WoodCuttersBuilt;
        public int StonePitsBuilt;

        // Askeri
        public int SoldierCount;
        public int TowersBuilt;
        public int BarracksBuilt;

        // --- YENİ EKLENEN SAVAŞ İSTATİSTİKLERİ ---
        public int EnemyUnitsKilled;       // Öldürülen Düşman Sayısı
        public int EnemyBuildingsDestroyed;// Yıkılan Düşman Binası

        // Savaş
        public float DamageDealt;
        public float DamageTaken;
        public float BaseHealthRemaining;
    }
}