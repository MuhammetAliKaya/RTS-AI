using UnityEngine;
using RTS.Simulation.RL;

namespace RTS.Simulation.Agents
{
    public class QLearningAgentController : IAgentController
    {
        private QLearningBrain _brain;
        private SimRLEnvironment _env;

        // Ajanı Başlat
        public void Initialize(SimRLEnvironment env)
        {
            _env = env;
            // 64 State, 5 Action (Odun, Taş, Et, İşçi, Kışla)
            _brain = new QLearningBrain(64, 5);

            Debug.Log("🧠 Q-Learning Controller Hazır.");
        }

        // Karar Al
        public int GetAction(int state)
        {
            return _brain.GetAction(state);
        }

        // Eğit
        public void Train(int state, int action, float reward, int nextState, bool done)
        {
            _brain.Learn(state, action, reward, nextState, done);
        }

        // Bölüm Sonu
        public void OnEpisodeEnd()
        {
            _brain.DecayEpsilon();
        }

        // Kaydet
        public void SaveModel(string path)
        {
            _brain.SaveTable(path);
            Debug.Log($"💾 Model Kaydedildi: {path}");
        }

        // İstatistik (UI İçin)
        public string GetStats()
        {
            return $"Epsilon: {_brain.Epsilon:F3}";
        }
    }
}