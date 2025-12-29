namespace RTS.Simulation.Data
{
    [System.Serializable]
    public class SimPlayerData
    {
        public int PlayerID;

        public int Wood;
        public int Stone;
        public int Meat;

        public int CurrentPopulation;
        public int MaxPopulation;
        // --- EKLENEN İSTATİSTİKLER ---
        public float TotalDamageDealt; // Toplam verilen hasar
        public float TotalDamageTaken; // Toplam alınan hasar
    }
}