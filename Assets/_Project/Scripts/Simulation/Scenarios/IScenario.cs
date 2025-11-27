using RTS.Simulation.Data;

namespace RTS.Simulation.Scenarios
{
    public interface IScenario
    {
        // Senaryonun Adı (UI'da göstermek için)
        string ScenarioName { get; }

        // Haritayı ve Kaynakları Seed'e göre oluşturur
        void SetupMap(SimWorldState world, int seed);

        // Oyun bitti mi? (Örn: Kışla bitti mi? Düşman öldü mü?)
        bool CheckWinCondition(SimWorldState world, int playerID);

        // Bu senaryoya özel ödül fonksiyonu
        // (Örn: Economy senaryosunda odun toplamak ödülken, Savaş senaryosunda düşman öldürmek ödüldür)
        float CalculateReward(SimWorldState world, int playerID, int action);
    }
}