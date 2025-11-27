using RTS.Simulation.RL;

namespace RTS.Simulation.Agents
{
    public interface IAgentController
    {
        // Ajanı ortamla tanıştır
        void Initialize(SimRLEnvironment env);

        // Duruma bakıp karar ver
        int GetAction(int state);

        // Öğren (State, Action, Reward, NextState, Done)
        void Train(int state, int action, float reward, int nextState, bool done);

        // Bölüm bittiğinde temizlik yap (Epsilon düşür, Log al)
        void OnEpisodeEnd();

        // Beynini kaydet
        void SaveModel(string path);

        // İstatistik ver (UI için)
        string GetStats();
    }
}