using System.IO;
using System.Text;
using UnityEngine; // Sadece Random ve Mathf için
using System.Globalization;

namespace RTS.Simulation.RL
{
    public class QLearningBrain
    {
        // --- HİPERPARAMETRELER (UZUN EĞİTİM İÇİN OPTİMİZE EDİLDİ) ---
        public float Alpha = 0.1f;        // Öğrenme Hızı (Çok hızlı unutmasın)
        public float Gamma = 0.99f;      // Gelecek Odaklılık (Uzun vadeli ödül için 1'e yakın)
        public float Epsilon = 1.0f;      // Başlangıç Keşfetme Oranı
        public float EpsilonMin = 0.0f;  // Minimum Keşfetme
        public float EpsilonDecay = 0.995f; // Çok yavaş düşsün (Binlerce bölüm sürsün)

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

            // Başlığı dinamik oluştur
            sb.Append("State");
            for (int a = 0; a < _numActions; a++)
            {
                sb.Append($",Action{a}");
            }
            sb.AppendLine();

            for (int s = 0; s < _numStates; s++)
            {
                sb.Append(s);
                for (int a = 0; a < _numActions; a++)
                {
                    sb.Append("," + _qTable[s, a].ToString("F3", CultureInfo.InvariantCulture));
                }
                sb.AppendLine();
            }
            File.WriteAllText(path, sb.ToString());
        }


        public void LoadTable(string path)
        {
            // Bu satırı MUTLAKA en başa koy
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

            try
            {
                if (!File.Exists(path))
                {
                    Debug.LogWarning($"⚠️ Q-Table dosyası bulunamadı: {path}");
                    return;
                }

                string[] lines = File.ReadAllLines(path);

                for (int s = 1; s < lines.Length && s - 1 < _numStates; s++)
                {
                    string[] values = lines[s].Split(',');

                    for (int a = 0; a < _numActions && a + 1 < values.Length; a++)
                    {
                        if (float.TryParse(values[a + 1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float qValue))
                        {
                            _qTable[s - 1, a] = qValue;
                        }
                    }
                }

                Debug.Log($"✅ Q-Table yüklendi!");
                ValidateLoading();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Hata: {e.Message}");
            }
        }

        public void ValidateLoading()
        {
            float sum = 0;
            int nonZeroCount = 0;

            for (int s = 0; s < _numStates; s++)
            {
                for (int a = 0; a < _numActions; a++)
                {
                    if (_qTable[s, a] != 0) nonZeroCount++;
                    sum += _qTable[s, a];
                }
            }

            Debug.Log($"📊 Yükleme Kontrol:");
            Debug.Log($"  - Sıfır olmayan değer: {nonZeroCount} / {_numStates * _numActions}");
            Debug.Log($"  - Toplam Q değeri: {sum:F2}");
            Debug.Log($"  - Ortalama: {(sum / (_numStates * _numActions)):F6}");

            if (nonZeroCount == 0) Debug.LogError("❌ HATA: Hiçbir Q değeri yüklenmedi!");
        }
    }
}