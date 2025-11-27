using System.IO;
using System.Text;
using UnityEngine; // Sadece Random ve Mathf için

namespace RTS.Simulation.RL
{
    public class QLearningBrain
    {
        // --- HİPERPARAMETRELER (UZUN EĞİTİM İÇİN OPTİMİZE EDİLDİ) ---
        public float Alpha = 0.1f;        // Öğrenme Hızı (Çok hızlı unutmasın)
        public float Gamma = 0.99f;      // Gelecek Odaklılık (Uzun vadeli ödül için 1'e yakın)
        public float Epsilon = 1.0f;      // Başlangıç Keşfetme Oranı
        public float EpsilonMin = 0.0f;  // Minimum Keşfetme
        public float EpsilonDecay = 0.9995f; // Çok yavaş düşsün (Binlerce bölüm sürsün)

        // Tablo: [State, Action]
        private float[,] _qTable;
        private int _numStates;
        private int _numActions;

        public QLearningBrain(int states, int actions)
        {
            _numStates = states;
            _numActions = actions;
            _qTable = new float[states, actions];
        }

        // --- KARAR VERME ---
        public int GetAction(int state)
        {
            // Keşfet (Explore)
            if (Random.value < Epsilon)
            {
                return Random.Range(0, _numActions);
            }

            // Sömür (Exploit)
            int bestAction = 0;
            float maxVal = _qTable[state, 0];

            for (int a = 1; a < _numActions; a++)
            {
                if (_qTable[state, a] > maxVal)
                {
                    maxVal = _qTable[state, a];
                    bestAction = a;
                }
            }
            return bestAction;
        }

        // --- ÖĞRENME ---
        public void Learn(int state, int action, float reward, int nextState, bool done)
        {
            float currentQ = _qTable[state, action];
            float maxNextQ = 0f;

            if (!done)
            {
                maxNextQ = _qTable[nextState, 0];
                for (int a = 1; a < _numActions; a++)
                {
                    if (_qTable[nextState, a] > maxNextQ) maxNextQ = _qTable[nextState, a];
                }
            }

            // Bellman Denklemi
            float target = reward + Gamma * maxNextQ;
            float error = target - currentQ;
            _qTable[state, action] += Alpha * error;
        }

        public void DecayEpsilon()
        {
            Epsilon = Mathf.Max(EpsilonMin, Epsilon * EpsilonDecay);
        }

        // --- KAYDETME ---
        public void SaveTable(string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("State,Action0,Action1,Action2,Action3,Action4");

            for (int s = 0; s < _numStates; s++)
            {
                sb.Append(s);
                for (int a = 0; a < _numActions; a++)
                {
                    sb.Append("," + _qTable[s, a].ToString("F3"));
                }
                sb.AppendLine();
            }
            File.WriteAllText(path, sb.ToString());
        }
    }
}