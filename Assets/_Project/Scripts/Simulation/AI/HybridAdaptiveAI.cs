using System.Linq;
using RTS.Simulation.Data;
using RTS.Simulation.Core;
using RTS.Simulation.Systems;
using UnityEngine;

namespace RTS.Simulation.AI
{
    public class HybridAdaptiveAI
    {
        private SimWorldState _world;
        private int _playerID;
        private SpecializedMacroAI _aiAgent;
        private float _decisionTimer;

        private float[] _economyGenes;
        private float[] _defenseGenes;
        private float[] _attackGenes;

        private float _defThreshold;
        private float _atkThreshold;

        // Bu parametreleri artık sadece Analizör'e paslayacağız
        private int _minDefenseSteps;
        private int _minTowers;
        private int _maturitySoldierCount;
        private int _maturityResourceLevel;

        private float _enemyInactivityTimer = 0f;
        private string _currentStrategy = "None";

        public HybridAdaptiveAI(SimWorldState world, int playerID, SpecializedMacroAI aiAgent,
                                float[] ecoGenes, float[] defGenes, float[] atkGenes,
                                float defThreshold, float atkThreshold,
                                int minDefenseSteps, int minTowers,
                                int maturitySoldierCount, int maturityResourceLevel)
        {
            _world = world;
            _playerID = playerID;
            _aiAgent = aiAgent;

            _economyGenes = ecoGenes;
            _defenseGenes = defGenes;
            _attackGenes = atkGenes;

            _defThreshold = defThreshold;
            _atkThreshold = atkThreshold;

            _minDefenseSteps = minDefenseSteps;
            _minTowers = minTowers;
            _maturitySoldierCount = maturitySoldierCount;
            _maturityResourceLevel = maturityResourceLevel;
        }

        public void Update(float dt)
        {
            _aiAgent.Update(dt);
            UpdateInactivityTimer(dt);

            _decisionTimer += dt;
            if (_decisionTimer >= 0.5f)
            {
                _decisionTimer = 0;
                EvaluateAndSwitchStrategy();
            }
        }
        public string GetCurrentStrategy()
        {
            return _currentStrategy;
        }



        private void UpdateInactivityTimer(float dt)
        {
            var myBase = _world.Buildings.Values.FirstOrDefault(b => b.PlayerID == _playerID && b.Type == SimBuildingType.Base);
            if (myBase == null) return;

            bool isThreatened = false;
            float threatRange = 30f;

            foreach (var u in _world.Units.Values)
            {
                if (u.PlayerID != _playerID && u.UnitType == SimUnitType.Soldier)
                {
                    if (SimMath.Distance(u.GridPosition, myBase.GridPosition) < threatRange)
                    {
                        isThreatened = true;
                        break;
                    }
                }
            }

            if (isThreatened) _enemyInactivityTimer = 0f;
            else _enemyInactivityTimer += dt;
        }

        private void EvaluateAndSwitchStrategy()
        {
            // Analizöre TÜM kısıtlamaları gönderiyoruz.
            // O bize nihai bir puan (GSF) veriyor.
            var metrics = SimGameStateAnalyzer.CalculateGSF(_world, _playerID, _enemyInactivityTimer,
                                                            _minDefenseSteps, _minTowers,
                                                            _maturitySoldierCount, _maturityResourceLevel);
            float gsf = metrics.GSF;

            string targetStrategy = _currentStrategy;
            float[] targetGenes = null;

            // --- TEK VE NET KARAR MEKANİZMASI ---
            // Artık "Zorunlu Defans" veya "Zorunlu Saldırı" yok.
            // Sadece GSF skoru var. Eğer kulem yoksa GSF zaten -1000 çıkıyor, yani otomatik Defans oluyor.

            if (gsf < _defThreshold)
            {
                if (_currentStrategy != "Defensive")
                {
                    targetStrategy = "Defensive";
                    targetGenes = _defenseGenes;
                }
            }
            else if (gsf > _atkThreshold)
            {
                if (_currentStrategy != "Aggressive")
                {
                    targetStrategy = "Aggressive";
                    targetGenes = _attackGenes;
                }
            }
            else
            {
                if (_currentStrategy != "Economic")
                {
                    targetStrategy = "Economic";
                    targetGenes = _economyGenes;
                }
            }

            // Değişikliği Uygula
            if (targetGenes != null)
            {
                _currentStrategy = targetStrategy;
                _aiAgent.SetGenes(targetGenes, _currentStrategy);

                if (SimConfig.EnableLogs)
                {
                    Debug.Log($"📊 GSF: {gsf:F1} (Pasiflik: {_enemyInactivityTimer:F0}s) -> Mod: {targetStrategy}");
                }
            }
        }

        public float GetInactivityTimer() => _enemyInactivityTimer;
    }
}