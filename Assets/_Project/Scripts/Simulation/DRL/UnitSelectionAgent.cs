using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using RTS.Simulation.Core;
using RTS.Simulation.Data;
using RTS.Simulation.Systems; // SimConfig vb. için
using System.Linq;

public class UnitSelectionAgent : Agent
{
    private RTSOrchestrator _orchestrator;
    private SimWorldState _world;
    private DRLActionTranslator _translator;

    // Gözlemleri normalize etmek için sabitler
    private const float MAX_RES = 1000f;
    private const float MAX_POP = 50f;
    private const float MAX_UNIT_COUNT = 30f;

    private bool _hasPendingDemo = false;
    private int _pendingSourceIndex = 0;

    public void Setup(RTSOrchestrator orchestrator, SimWorldState world, DRLActionTranslator translator)
    {
        _orchestrator = orchestrator;
        _world = world;
        _translator = translator;
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
        if (_world == null || _translator == null) return;

        int totalMapSize = _world.Map.Width * _world.Map.Height;
        int myID = _orchestrator.MyPlayerID;

        // GÜVENLİK KİLİDİ: Hiçbir şey seçemezsek devreye girecek bayrak
        bool hasAnySelection = false;

        for (int i = 0; i < totalMapSize; i++)
        {
            bool canSelect = false;

            // 1. Bu karede bana ait bir şey var mı?
            // (Translator artık çok hızlı olduğu için burası performansı düşürmez)
            if (_translator.IsUnitOwnedByPlayer(i, myID))
            {
                var unit = _translator.GetUnitAtPosIndex(i);

                if (unit != null)
                {
                    // Sadece BOŞTA (Idle) olan işçiler seçilebilir
                    // (Stratejine göre burayı değiştirebilirsin ama Idle en güvenlisidir)
                    if (unit.State != SimTaskType.Building && unit.State != SimTaskType.Moving)
                    {
                        canSelect = true;
                    }
                }
                else
                {
                    // Bina Kontrolü: Binalar her zaman seçilebilir (Üretim için)
                    // (Translator IsUnitOwned true dediyse ve Unit değilse binadır)
                    SimBuildingType bType = _translator.GetBuildingTypeAt(i);
                    if (bType == SimBuildingType.Base ||
        bType == SimBuildingType.Barracks)
                    {
                        canSelect = true;
                    }
                    else
                    {
                        canSelect = false; // Ev, Çiftlik vb. seçilemez
                    }
                }
            }
            if (canSelect)
            {
                int gridW = _world.Map.Width;
                // Mevcut 'i' indeksini x,y koordinatına çevir
                int cx = i % gridW;
                int cy = i / gridW;

                // Etrafında boş yer yoksa (tamamen sarılmışsa) seçimi iptal et
                if (!HasWalkableNeighbor(cx, cy))
                {
                    canSelect = false;
                }
            }

            // Eğer bu kare seçilebiliyorsa bayrağı kaldır
            if (canSelect) hasAnySelection = true;

            // Maskeyi uygula
            actionMask.SetActionEnabled(0, i, canSelect);
        }

        // --- KRİTİK GÜVENLİK ---
        // Eğer haritada tıklanacak tek bir geçerli kare bile kalmadıysa
        // ML-Agents'ın çökmemesi için 0. indeksi (veya herhangi birini) zorla açıyoruz.
        // Orchestrator bu durumu "Wait" olarak algılamalı veya pas geçmeli.
        if (!hasAnySelection)
        {
            actionMask.SetActionEnabled(0, 0, true);
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // Karar verilen grid indeksi (0 ile MapSize-1 arası)
        int sourceIndex = actions.DiscreteActions[0];

        // Orkestratöre bildir: "Ben bu birimi seçtim, sıra ActionSelectionAgent'ta."
        _orchestrator.OnUnitSelected(sourceIndex);
    }

    // Orchestrator bu fonksiyonu çağırır
    public void RegisterExternalAction(int actionType, int sourceIndex, int targetIndex)
    {
        _pendingSourceIndex = sourceIndex;
        _hasPendingDemo = true;

        // Ajanı uyandır, Heuristic çalışsın, kayıt alınsın.
        RequestDecision();
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discreteActions = actionsOut.DiscreteActions;

        if (_hasPendingDemo)
        {
            // İnsanın seçtiği üniteyi yapay zekanın kararı gibi çıktı ver
            discreteActions[0] = _pendingSourceIndex;
            _hasPendingDemo = false; // Reset
        }
        else
        {
            // Eğer demo yoksa ve heuristic çalıştıysa (test amaçlı) 0 döndür
            discreteActions[0] = 0;
        }
    }

    private bool HasWalkableNeighbor(int cx, int cy)
    {
        // Etraftaki 8 kareyi kontrol et
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue; // Kendisi hariç

                // SimGridSystem.IsWalkable, harita sınırlarını ve doluluğu zaten kontrol eder
                if (SimGridSystem.IsWalkable(_world, new RTS.Simulation.Data.int2(cx + dx, cy + dy)))
                {
                    return true; // En az bir çıkış yolu var
                }
            }
        }
        return false; // Hiçbir yere kıpırdayamaz (veya üretim çıkışı yok)
    }
}