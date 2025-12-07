using UnityEngine;
using System.Collections.Generic;
using Unity.MLAgents;

public class TrainingManager : MonoBehaviour
{
    [Header("Eğitim Hızı")]
    [Tooltip("Her render karesinde tüm ortamlar kaç adım atacak?")]
    [Range(1, 100)]
    public int StepsPerFrame = 100; // Başlangıçta 10 deneyin, sonra 50-100 yaparsınız.

    private List<DRLSimRunner> _runners = new List<DRLSimRunner>();

    private void Start()
    {
        // Sahnedeki tüm runner'ları bul ve kaydet
        _runners.AddRange(FindObjectsOfType<DRLSimRunner>());

        // Otomatik adımlamayı kapat (Kontrol bizde)
        if (Academy.IsInitialized)
        {
            Academy.Instance.AutomaticSteppingEnabled = false;
        }

        Debug.Log($"🚀 Training Manager Başladı! Toplam Ortam: {_runners.Count}");
    }

    private void Update()
    {
        // 1 Karede (Frame) N kez simülasyonu ilerlet
        for (int i = 0; i < StepsPerFrame; i++)
        {
            // 1. Tüm oyunları 1 tık (tick) ilerlet
            foreach (var runner in _runners)
            {
                runner.ManualUpdate();
            }

            // 2. Tüm ajanlar kararını verdiyse, topluca Python'a yolla ve cevap bekle
            if (Academy.IsInitialized)
            {
                Academy.Instance.EnvironmentStep();
            }
        }
    }
}