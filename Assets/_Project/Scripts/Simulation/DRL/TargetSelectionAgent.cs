using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using RTS.Simulation.Core;   // SimGridSystem vb. için
using RTS.Simulation.Data;   // SimWorldState için
using RTS.Simulation.Systems;

public class TargetSelectionAgent : Agent
{
    private RTSOrchestrator _orchestrator;
    private SimWorldState _world;
    private DRLActionTranslator _translator;

    // Haritayı görebilmesi için GridSensor referansı şart
    private RTSGridSensorComponent _gridSensorComp;
    private AdversarialTrainerRunner _runner;

    private bool _hasPendingDemo = false;
    private int _pendingTargetIndex = 0;

    // Setup metodunu güncelledik: SimGridSystem'i de paramatre olarak alıyoruz (Sensör için)
    public void Setup(RTSOrchestrator orchestrator, SimWorldState world, DRLActionTranslator translator, SimGridSystem gridSystem, AdversarialTrainerRunner runner)
    {
        _orchestrator = orchestrator;
        _world = world;
        _translator = translator;
        _runner = runner; // Runner'ı kaydet!

        // GridSensorComponent'i bu GameObject üzerinden alıyoruz.
        // (Unity Editor'de TargetSelectionAgent objesine RTSGridSensorComponent eklemeyi unutmayın!)
        _gridSensorComp = GetComponent<RTSGridSensorComponent>();

        if (_gridSensorComp != null)
        {
            // Sensörü başlatıyoruz
            _gridSensorComp.InitializeSensor(_world, gridSystem);
        }
        else
        {
            Debug.LogWarning("[TargetSelectionAgent] RTSGridSensorComponent bulunamadı! Ajan kör çalışıyor.");
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 1. Önce Context Bilgisi (Seçilen Birim Türü) - Bu her ajanda farklı olabilir veya aynı kalabilir
        // (Mevcut kodunuzdaki Context kısmını koruyun, örneğin One-Hot Encoding)
        if (_orchestrator == null || _translator == null)
        {
            // Sensör boş kalmasın diye dummy veri basabiliriz veya boş döneriz.
            // ML-Agents hata vermemesi için genellikle boş bırakmak yerine 0 basmak daha güvenlidir ama 
            // Orchestrator yoksa yapacak bir şey yok, return diyoruz.
            return;
        }
        if (_orchestrator.SelectedSourceIndex != -1)
        {
            float[] typeObs = _translator.GetOneHotEncodedTypeAt(_orchestrator.SelectedSourceIndex);
            foreach (float val in typeObs) sensor.AddObservation(val);
        }
        else
        {
            // Seçim yoksa boş context
            for (int i = 0; i < 5; i++) sensor.AddObservation(0f);
        }

        // 2. STRATEJİK VEKTÖR (Sizin istediğiniz liste)
        _orchestrator.AddStrategicObservations(sensor);
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        // Güvenlik kontrolleri
        if (_world == null || _translator == null) return;

        int actionType = _orchestrator.SelectedActionType;
        int totalMapSize = _world.Map.Width * _world.Map.Height;
        bool anyTarget = false;

        // Tüm harita karelerini tek tek kontrol et
        for (int i = 0; i < totalMapSize; i++)
        {
            // Translator'daki "Bu eylem bu kareye uygulanabilir mi?" fonksiyonunu kullan
            // Örnek: "Attack" emri için sadece düşman olan kareler true döner.
            // Örnek: "Build House" emri için sadece boş ve inşaata uygun kareler true döner.
            bool isValid = _translator.IsTargetValidForAction(actionType, i);

            if (isValid) anyTarget = true;

            actionMask.SetActionEnabled(0, i, isValid);
        }

        // Eğer haritada hiçbir geçerli hedef yoksa (örneğin düşman kalmadı ama 'Saldır' seçildi)
        // Ajanın hata verip çökmemesi için 0. indeksi (Genelde sol alt köşe) mecburen açıyoruz.
        // Orchestrator tarafında bu "başarısız eylem" olarak işlenip ceza verilecek.
        if (!anyTarget)
        {
            actionMask.SetActionEnabled(0, 0, true);
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        int targetIndex = actions.DiscreteActions[0];

        // Seçilen hedefi yöneticiye (Orchestrator) bildir.
        // Bu noktada zincir tamamlanır ve eylem simülasyona uygulanır.
        _orchestrator.OnTargetSelected(targetIndex);
    }

    // Agent'ın OnEpisodeBegin metodunu override et
    public override void OnEpisodeBegin()
    {
        if (_runner != null)
        {
            // Ortamın sıfırlanmasını Runner'a devret.
            // _runner.ResetSimulation();
        }

        // Highlight'ı sıfırla
        if (_gridSensorComp != null)
        {
            _gridSensorComp.SetHighlight(-1);
        }
    }

    public void RegisterExternalAction(int actionType, int sourceIndex, int targetIndex)
    {
        _pendingTargetIndex = targetIndex;
        _hasPendingDemo = true;

        RequestDecision();
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discreteActions = actionsOut.DiscreteActions;

        if (_hasPendingDemo)
        {
            discreteActions[0] = _pendingTargetIndex;
            _hasPendingDemo = false;
        }
        else
        {
            discreteActions[0] = 0;
        }
    }
}