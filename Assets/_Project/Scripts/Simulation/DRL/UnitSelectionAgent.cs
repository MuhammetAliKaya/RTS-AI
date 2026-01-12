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
        // 1. Veri Güvenliği
        if (_world == null || !_world.Players.ContainsKey(_orchestrator.MyPlayerID))
        {
            // Veri yoksa boş gözlem bas (6 adet float)
            for (int i = 0; i < 6; i++) sensor.AddObservation(0f);
            return;
        }

        var me = _world.Players[_orchestrator.MyPlayerID];

        // 2. Kaynak Durumu (Ekonomi ne durumda?)
        sensor.AddObservation(me.Wood / MAX_RES);
        sensor.AddObservation(me.Stone / MAX_RES);
        sensor.AddObservation(me.Meat / MAX_RES);
        sensor.AddObservation(me.CurrentPopulation / MAX_POP);

        // 3. Ordu/İşçi Sayımı (Neye ihtiyacım var?)
        // LINQ sorguları her frame biraz maliyetli olabilir ama ML-Agents için kabul edilebilir.
        // Daha optimize olması için bu sayılar Orchestrator'da tutulup buraya paslanabilir.
        int workerCount = 0;
        int soldierCount = 0;

        foreach (var u in _world.Units.Values)
        {
            if (u.PlayerID == _orchestrator.MyPlayerID && u.State != SimTaskType.Dead)
            {
                if (u.UnitType == SimUnitType.Worker) workerCount++;
                else if (u.UnitType == SimUnitType.Soldier) soldierCount++;
            }
        }

        sensor.AddObservation(workerCount / MAX_UNIT_COUNT);
        sensor.AddObservation(soldierCount / MAX_UNIT_COUNT);
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
}