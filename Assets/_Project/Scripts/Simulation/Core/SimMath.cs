using System;

namespace RTS.Simulation.Core
{
    /// <summary>
    /// Unity.Mathf kütüphanesinin Thread-Safe (System.Math kullanan) karşılığıdır.
    /// Multi-thread simülasyonlarda Mathf yerine bunu kullanmalıyız.
    /// </summary>
    public static class SimMath
    {
        // Yuvarlama İşlemleri
        public static int RoundToInt(float f) => (int)Math.Round(f);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static int CeilToInt(float f) => (int)Math.Ceiling(f);

        // Sınırlama (Clamp) İşlemleri
        public static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        // Mutlak Değer
        public static float Abs(float f) => Math.Abs(f);
        public static int Abs(int f) => Math.Abs(f);

        // Min / Max
        public static float Min(float a, float b) => (a < b) ? a : b;
        public static float Max(float a, float b) => (a > b) ? a : b;

        public static int Min(int a, int b) => (a < b) ? a : b;
        public static int Max(int a, int b) => (a > b) ? a : b;

        // Diğer Matematiksel İşlemler
        public static float Sqrt(float f) => (float)Math.Sqrt(f);

        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * Clamp01(t);
        }
    }
}