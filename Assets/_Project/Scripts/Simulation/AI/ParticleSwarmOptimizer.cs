using System; // System.Math ve System.Random için
using System.Collections.Generic;

// UnityEngine YOK!

public class ParticleSwarmOptimizer
{
    public struct Particle
    {
        public float[] Position;    // Genler
        public float[] Velocity;    // Hız
        public float[] BestPosition; // Kişisel En İyi
        public float BestFitness;    // Kişisel En İyi Skor

        // System.Random (rng) parametre olarak alınır
        public Particle(int dim, float min, float max, System.Random rng)
        {
            Position = new float[dim];
            Velocity = new float[dim];
            BestPosition = new float[dim];
            BestFitness = float.MinValue;

            for (int i = 0; i < dim; i++)
            {
                // UnityEngine.Random.Range(min, max) yerine:
                Position[i] = (float)(rng.NextDouble() * (max - min) + min);

                // UnityEngine.Random.Range(-1f, 1f) yerine:
                Velocity[i] = (float)(rng.NextDouble() * 2.0 - 1.0);

                BestPosition[i] = Position[i];
            }
        }
    }

    private List<Particle> _particles;
    public float[] GlobalBestPosition { get; private set; }
    public float GlobalBestFitness { get; private set; }

    private int _dimensions;
    private float _minVal, _maxVal;

    // PSO Hiperparametreleri
    private float _w = 0.7f;  // Inertia
    private float _c1 = 1.4f; // Cognitive
    private float _c2 = 1.4f; // Social

    public ParticleSwarmOptimizer(int populationSize, int dimensions, float minVal, float maxVal)
    {
        _dimensions = dimensions;
        _minVal = minVal;
        _maxVal = maxVal;
        _particles = new List<Particle>();
        GlobalBestFitness = float.MinValue;
        GlobalBestPosition = new float[dimensions];

        // Başlangıç oluşturma sırasında geçici bir Random kullanabiliriz
        System.Random rng = new System.Random();

        for (int i = 0; i < populationSize; i++)
        {
            _particles.Add(new Particle(dimensions, minVal, maxVal, rng));
        }
    }

    public List<float[]> GetPositions()
    {
        List<float[]> positions = new List<float[]>();
        foreach (var p in _particles) positions.Add(p.Position);
        return positions;
    }

    public void UpdateFitness(int particleIndex, float fitness)
    {
        var p = _particles[particleIndex];

        // Particle Best Update
        if (fitness > p.BestFitness)
        {
            p.BestFitness = fitness;
            Array.Copy(p.Position, p.BestPosition, 0); // System.Array kullanımı
        }

        // Global Best Update
        if (fitness > GlobalBestFitness)
        {
            GlobalBestFitness = fitness;
            Array.Copy(p.Position, GlobalBestPosition, 0);
        }

        _particles[particleIndex] = p;
    }

    public void Step()
    {
        // Step fonksiyonu için yeni bir Random örneği
        System.Random rng = new System.Random();

        for (int i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];

            for (int d = 0; d < _dimensions; d++)
            {
                // UnityEngine.Random.value yerine System.Random.NextDouble()
                float r1 = (float)rng.NextDouble();
                float r2 = (float)rng.NextDouble();

                // Hız Güncelleme
                p.Velocity[d] = _w * p.Velocity[d]
                              + _c1 * r1 * (p.BestPosition[d] - p.Position[d])
                              + _c2 * r2 * (GlobalBestPosition[d] - p.Position[d]);

                // Pozisyon Güncelleme
                p.Position[d] += p.Velocity[d];

                // Sınırlandırma (Clamping) - System.Math kullanımı
                // Math.Max(min, Math.Min(max, val)) yapısı Clamp ile aynıdır
                p.Position[d] = Math.Max(_minVal, Math.Min(_maxVal, p.Position[d]));
            }

            _particles[i] = p;
        }
    }
}