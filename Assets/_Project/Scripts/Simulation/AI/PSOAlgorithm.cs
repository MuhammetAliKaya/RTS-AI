using System;
using System.Collections.Generic;
using RTS.Simulation.Core; // SimMath

// Kaynak Makale: APSO-SL (Gao & Yang, 2024)
// Özellikler:
// 1. PDEM (Population Distribution Evaluation Mechanism)
// 2. ALS (Adaptive Learning Strategy)
// 3. Seed-Based Reproducibility (Tekrarlanabilirlik)
// 4. UseStandardPSO Anahtarı (Devre dışı bırakma)

public class PSOAlgorithm
{
    // Makaledeki 3 Temel Durum
    public enum SwarmState
    {
        Exploration,  // Keşif (Dağınık) -> Klasik PSO
        Exploitation, // Sömürü (Toplanmış) -> Random Exemplar PSO
        Balance       // Denge -> Bir önceki durumu koru
    }

    public struct Particle
    {
        public float[] Position;
        public float[] Velocity;
        public float[] BestPosition; // pBest
        public float BestFitness;

        // Constructor'da System.Random kullanarak deterministik başlangıç
        public Particle(int dim, float min, float max, System.Random rng)
        {
            Position = new float[dim];
            Velocity = new float[dim];
            BestPosition = new float[dim];
            BestFitness = float.MinValue;

            for (int i = 0; i < dim; i++)
            {
                // Pozisyon: [min, max] arası rastgele
                Position[i] = (float)(rng.NextDouble() * (max - min) + min);

                // Hız: [-1, 1] * Aralık * 0.1 (Yüzde 10 hızla başla)
                Velocity[i] = (float)((rng.NextDouble() * 2.0 - 1.0) * (max - min) * 0.1f);

                BestPosition[i] = Position[i];
            }
        }
    }

    private List<Particle> _particles;
    public float[] GlobalBestPosition { get; private set; }
    public float GlobalBestFitness { get; private set; }

    // --- PUBLIC GÖZLEM DEĞERLERİ ---
    public SwarmState CurrentState { get; private set; } = SwarmState.Exploration;
    private SwarmState _lastActiveState = SwarmState.Exploration;

    // APSO-SL bypass anahtarı (Inspector'dan yönetilir)
    public bool UseStandardPSO { get; set; } = false;

    // Parametreler (Makalede sabit ama formül değişiyor)
    public float W { get; private set; } = 0.729f;
    public float C1 { get; private set; } = 1.49445f;
    public float C2 { get; private set; } = 1.49445f;

    private int _dimensions;
    private float _minVal, _maxVal;
    private float _maxDiagonalDist; // Normalizasyon için
    private System.Random _algoRng; // Algoritma içi deterministik Random

    // --- CONSTRUCTOR ---
    public PSOAlgorithm(int popSize, int dim, float min, float max, int seed)
    {
        _dimensions = dim;
        _minVal = min;
        _maxVal = max;
        _particles = new List<Particle>();
        GlobalBestFitness = float.MinValue;
        GlobalBestPosition = new float[dim];

        // Maksimum köşegen mesafesi (Eq 6 paydası)
        double sumSquaredRange = dim * Math.Pow(max - min, 2);
        _maxDiagonalDist = (float)Math.Sqrt(sumSquaredRange);

        // Master Seed'den türetilen algoritma RNG'si
        _algoRng = new System.Random(seed);

        for (int i = 0; i < popSize; i++)
        {
            _particles.Add(new Particle(dim, min, max, _algoRng));
        }
    }

    // --- GET POSITIONS (Popülasyonu dışarı ver) ---
    public List<float[]> GetPositions()
    {
        var list = new List<float[]>();
        foreach (var p in _particles) list.Add(p.Position);
        return list;
    }

    // --- REPORT FITNESS (Skorları al ve güncelle) ---
    public void ReportFitness(int index, float fitness)
    {
        var p = _particles[index];

        // pBest Güncelleme
        if (fitness > p.BestFitness)
        {
            p.BestFitness = fitness;
            Array.Copy(p.Position, p.BestPosition, _dimensions);
        }

        // gBest Güncelleme
        if (fitness > GlobalBestFitness)
        {
            GlobalBestFitness = fitness;
            Array.Copy(p.Position, GlobalBestPosition, _dimensions);
        }
        _particles[index] = p;
    }

    // --- STEP (Ana Döngü - Algorithm 1) ---
    public void Step()
    {
        // 1. PDEM: Durum Belirleme
        float fB = CalculateEvolutionaryFactor();
        UpdateState(fB);

        // 2. ALS: Parçacıkları Güncelle
        for (int i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];

            // Hangi stratejiyi kullanacağız?
            SwarmState strategy = (CurrentState == SwarmState.Balance) ? _lastActiveState : CurrentState;

            for (int d = 0; d < _dimensions; d++)
            {
                // Deterministik rastgele sayılar
                float r1 = (float)_algoRng.NextDouble();
                float r2 = (float)_algoRng.NextDouble();

                // EĞER "STANDART PSO" ANAHTARI AÇIKSA -> HEP KLASİK FORMÜL
                // EĞER KAPALIYSA -> APSO-SL MANTIĞI (Duruma göre formül)
                if (UseStandardPSO || strategy == SwarmState.Exploration)
                {
                    // --- CASE 1: EXPLORATION (Klasik PSO) ---
                    // Eq (1): V = w*V + c1*r1*(pBest - X) + c2*r2*(gBest - X)
                    p.Velocity[d] = W * p.Velocity[d] +
                                    C1 * r1 * (p.BestPosition[d] - p.Position[d]) +
                                    C2 * r2 * (GlobalBestPosition[d] - p.Position[d]);
                }
                else
                {
                    // --- CASE 2: EXPLOITATION (Makale Eq 7) ---
                    // gBest yerine rastgele bir 'exemplar' (pBest) seçilir.
                    int randIdx = _algoRng.Next(0, _particles.Count);
                    float[] e_position = _particles[randIdx].BestPosition;

                    // Eq (7): V = w*V + c1*r1*(pBest - X) + c2*r2*(e - X)
                    p.Velocity[d] = W * p.Velocity[d] +
                                    C1 * r1 * (p.BestPosition[d] - p.Position[d]) +
                                    C2 * r2 * (e_position[d] - p.Position[d]);
                }

                // Pozisyon Güncelleme (Eq 2)
                p.Position[d] += p.Velocity[d];

                // Sınır Kontrolü (Clamping)
                p.Position[d] = SimMath.Clamp(p.Position[d], _minVal, _maxVal);
            }
            _particles[i] = p;
        }
    }

    // --- PDEM HESAPLAMALARI [cite: 163-169] ---
    private float CalculateEvolutionaryFactor()
    {
        // 1. Popülasyonun Merkezini (X_M) bul (Eq 4)
        float[] centerPos = new float[_dimensions];
        foreach (var p in _particles)
        {
            for (int d = 0; d < _dimensions; d++)
                centerPos[d] += p.Position[d];
        }
        for (int d = 0; d < _dimensions; d++)
            centerPos[d] /= _particles.Count;

        // 2. gBest ile Merkez arasındaki mesafeyi (dist_B) bul (Eq 5)
        double distSq = 0;
        for (int d = 0; d < _dimensions; d++)
        {
            float delta = GlobalBestPosition[d] - centerPos[d];
            distSq += delta * delta;
        }
        float distB = (float)Math.Sqrt(distSq);

        // 3. Evrim Faktörünü (f_B) hesapla (Eq 6)
        return distB / _maxDiagonalDist;
    }

    private void UpdateState(float fB)
    {
        // Eşik değerleri (Makale 3.2)
        if (fB > 0.4f)
        {
            CurrentState = SwarmState.Exploration;
            _lastActiveState = SwarmState.Exploration;
        }
        else if (fB < 0.3f)
        {
            CurrentState = SwarmState.Exploitation;
            _lastActiveState = SwarmState.Exploitation;
        }
        else
        {
            CurrentState = SwarmState.Balance;
            // Balance durumunda _lastActiveState değişmez.
        }
    }
}