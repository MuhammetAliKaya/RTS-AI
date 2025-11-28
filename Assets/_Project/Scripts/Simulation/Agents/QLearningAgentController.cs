using UnityEngine;
using RTS.Simulation.RL;

namespace RTS.Simulation.Agents
{
    public class QLearningAgentController : IAgentController
    {
        private QLearningBrain _brain;
        private SimRLEnvironment _env;

        // --- AYARLARI SAKLAMAK İÇİN DEĞİŞKENLER ---
        // Varsayılan değerler
        private float _alpha = 0.1f;
        private float _gamma = 0.99f;
        private float _epsilon = 1.0f;
        private float _epsilonMin = 0.01f;
        private float _epsilonDecay = 0.999f;

        // --- YENİ: AYARLARI DIŞARIDAN ALAN FONKSİYON ---
        public void SetHyperparameters(float alpha, float gamma, float epsilon, float epMin, float epDecay)
        {
            _alpha = alpha;
            _gamma = gamma;
            _epsilon = epsilon;
            _epsilonMin = epMin;
            _epsilonDecay = epDecay;
        }

        // Ajanı Başlat
        public void Initialize(SimRLEnvironment env)
        {
            _env = env;
            // 64 State, 5 Action (Odun, Taş, Et, İşçi, Kışla)
            _brain = new QLearningBrain(64, 5);

            // --- AYARLARI BEYNE İŞLE ---
            _brain.Alpha = _alpha;
            _brain.Gamma = _gamma;
            _brain.Epsilon = _epsilon;
            _brain.EpsilonMin = _epsilonMin;
            _brain.EpsilonDecay = _epsilonDecay;

            Debug.Log($"🧠 Q-Learning Hazır. (Alpha:{_alpha}, Gamma:{_gamma}, Epsilon:{_epsilon}, Decay:{_epsilonDecay})");
        }

        // ... (Diğer fonksiyonlar GetAction, Train, SaveModel, LoadModel AYNEN KALSIN) ...

        public int GetAction(int state)
        {
            return _brain.GetAction(state);
        }

        public void Train(int state, int action, float reward, int nextState, bool done)
        {
            _brain.Learn(state, action, reward, nextState, done);
        }

        public void OnEpisodeEnd()
        {
            _brain.DecayEpsilon();
        }

        public void SaveModel(string path)
        {
            _brain.SaveTable(path);
            Debug.Log($"💾 Model Kaydedildi: {path}");
        }

        public string GetStats()
        {
            return $"Epsilon: {_brain.Epsilon:F3}";
        }

        public void LoadModel(string path)
        {
            _brain.LoadTable(path);
            _brain.Epsilon = 0f;
        }
    }
}