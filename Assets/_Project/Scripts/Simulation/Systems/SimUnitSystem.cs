using RTS.Simulation.Data;
using RTS.Simulation.Core; // SimGameContext için
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS.Simulation.Systems
{
    public class SimUnitSystem
    {
        // --- YENİ: Instance Yapısı ---
        private SimWorldState _world;

        public SimUnitSystem(SimWorldState world = null)
        {
            _world = world ?? SimGameContext.ActiveWorld;
        }

        // --- Instance Wrapper'lar ---
        public void UpdateUnit(SimUnitData unit, float dt) => UpdateUnit(unit, _world, dt);
        public void OrderMove(SimUnitData unit, int2 targetPos) => OrderMove(unit, targetPos, _world);
        public void OrderBuild(SimUnitData worker, SimBuildingData building) => OrderBuild(worker, building, _world);
        public void OrderAttack(SimUnitData unit, SimBuildingData building) => OrderAttack(unit, building, _world);
        public void OrderAttackUnit(SimUnitData unit, SimUnitData target) => OrderAttackUnit(unit, target, _world);
        public bool TryAssignGatherTask(SimUnitData unit, SimResourceData res) => TryAssignGatherTask(unit, res, _world);
        // -----------------------------

        // --- MEVCUT STATİK FONKSİYONLAR (KORUNDU) ---
        public static void UpdateUnit(SimUnitData unit, SimWorldState world, float dt)
        {
            if (unit.State == SimTaskType.Dead) return;

            switch (unit.State)
            {
                case SimTaskType.Moving: UpdateMovement(unit, world, dt); break;
                case SimTaskType.Gathering: UpdateGathering(unit, world, dt); break;
                case SimTaskType.Building: UpdateConstruction(unit, world, dt); break;
                case SimTaskType.Attacking: UpdateCombat(unit, world, dt); break;
                case SimTaskType.Idle: break;
            }
        }

        private static void UpdateMovement(SimUnitData unit, SimWorldState world, float dt)
        {
            if (unit.Path == null || unit.Path.Count == 0)
            {
                if (unit.TargetID != -1)
                {
                    if (world.Resources.ContainsKey(unit.TargetID)) unit.State = SimTaskType.Gathering;
                    else if (world.Units.TryGetValue(unit.TargetID, out SimUnitData targetUnit))
                    {
                        if (targetUnit.PlayerID != unit.PlayerID) unit.State = SimTaskType.Attacking;
                        else unit.State = SimTaskType.Idle;
                    }
                    else if (world.Buildings.TryGetValue(unit.TargetID, out SimBuildingData targetBuilding))
                    {
                        if (targetBuilding.PlayerID == unit.PlayerID && !targetBuilding.IsConstructed) unit.State = SimTaskType.Building;
                        else if (targetBuilding.PlayerID != unit.PlayerID) unit.State = SimTaskType.Attacking;
                        else unit.State = SimTaskType.Idle;
                    }
                    else unit.State = SimTaskType.Idle;
                }
                else unit.State = SimTaskType.Idle;
                return;
            }

            unit.MoveProgress += unit.MoveSpeed * dt;
            if (unit.MoveProgress >= 1.0f)
            {
                unit.MoveProgress = 0f;
                int2 nextPos = unit.Path[0];

                if (SimGridSystem.IsWalkable(world, nextPos) || nextPos == unit.GridPosition)
                {
                    world.Map.Grid[unit.GridPosition.x, unit.GridPosition.y].OccupantID = -1;
                    unit.GridPosition = nextPos;
                    world.Map.Grid[nextPos.x, nextPos.y].OccupantID = unit.ID;
                    unit.Path.RemoveAt(0);
                }
                else unit.Path.Clear();
            }
        }

        private static void UpdateCombat(SimUnitData unit, SimWorldState world, float dt)
        {
            // --- HEDEF KONTROLÜ ---
            bool isUnit = world.Units.TryGetValue(unit.TargetID, out SimUnitData enemyUnit);
            bool isBuilding = world.Buildings.TryGetValue(unit.TargetID, out SimBuildingData enemyBuilding);

            // Hedef yoksa veya öldüyse/yıkıldıysa saldırıyı durdur
            if ((!isUnit && !isBuilding) ||
                (isUnit && enemyUnit.State == SimTaskType.Dead) ||
                (isBuilding && !enemyBuilding.IsConstructed && enemyBuilding.PlayerID == unit.PlayerID))
            {
                unit.State = SimTaskType.Idle;
                unit.TargetID = -1;
                return;
            }

            // --- MESAFE KONTROLÜ ---
            int2 targetPos = isUnit ? enemyUnit.GridPosition : enemyBuilding.GridPosition;
            float distSq = SimGridSystem.GetDistanceSq(unit.GridPosition, targetPos);
            float rangeSq = unit.AttackRange * unit.AttackRange;

            // Menzildeyse VUR
            if (distSq <= rangeSq)
            {
                unit.AttackTimer += dt;
                if (unit.AttackTimer >= unit.AttackSpeed)
                {
                    unit.AttackTimer = 0f;

                    if (isUnit)
                    {
                        // HASAR UYGULA
                        enemyUnit.Health -= unit.Damage;

                        // --- DETAYLI LOG (BİRİM) ---
                        UnityEngine.Debug.Log(
                            $"⚔️ [BİRİM VURDU] " +
                            $"Saldıran: {unit.UnitType} (ID:{unit.ID}) (P:{unit.PlayerID}) -> " +
                            $"Hedef: {enemyUnit.UnitType} (ID:{enemyUnit.ID}) (P:{enemyUnit.PlayerID}) | " +
                            $"Konum: {targetPos} | Hasar: {unit.Damage} | Kalan Can: {enemyUnit.Health}"
                        );
                        // -------------------------

                        if (enemyUnit.Health <= 0)
                        {
                            UnityEngine.Debug.Log($"☠️ [ÖLÜM] {enemyUnit.UnitType} (ID:{enemyUnit.ID}) öldürüldü!");
                            KillUnit(enemyUnit, world);
                        }
                    }
                    else if (isBuilding)
                    {
                        // HASAR UYGULA
                        enemyBuilding.Health -= unit.Damage;

                        // --- DETAYLI LOG (BİNA) ---
                        UnityEngine.Debug.Log(
                            $"🔥 [BİNA VURDU] " +
                            $"Saldıran: {unit.UnitType} (ID:{unit.ID}) (P:{unit.PlayerID}) -> " +
                            $"Hedef Bina: {enemyBuilding.Type} (ID:{enemyBuilding.ID}) (P:{enemyBuilding.PlayerID}) | " +
                            $"Konum: {targetPos} | Hasar: {unit.Damage} | Kalan Can: {enemyBuilding.Health}"
                        );
                        // -------------------------

                        if (enemyBuilding.Health <= 0)
                        {
                            UnityEngine.Debug.Log($"💥 [YIKIM] {enemyBuilding.Type} (ID:{enemyBuilding.ID}) yıkıldı!");
                            DestroyBuilding(enemyBuilding, world);
                        }
                    }
                }
            }
            else
            {
                // Menzilde değilse YÜRÜ
                if (unit.Path == null || unit.Path.Count == 0)
                {
                    if (isBuilding)
                    {
                        int2? standPos = SimGridSystem.FindWalkableNeighbor(world, targetPos);
                        if (standPos.HasValue) unit.Path = SimGridSystem.FindPath(world, unit.GridPosition, standPos.Value);
                    }
                    else
                    {
                        unit.Path = SimGridSystem.FindPath(world, unit.GridPosition, targetPos);
                    }
                }
                unit.State = SimTaskType.Moving;
            }
        }

        private static void UpdateGathering(SimUnitData unit, SimWorldState world, float dt)
        {
            if (!world.Resources.TryGetValue(unit.TargetID, out SimResourceData res))
            {
                unit.State = SimTaskType.Idle;
                return;
            }

            unit.ActionTimer += dt;
            if (unit.ActionTimer >= SimConfig.GATHER_INTERVAL)
            {
                unit.ActionTimer = 0f;
                int amount = Math.Min(SimConfig.GATHER_AMOUNT, res.AmountRemaining);
                res.AmountRemaining -= amount;
                SimResourceSystem.AddResource(world, unit.PlayerID, res.Type, amount);

                if (res.AmountRemaining <= 0)
                {
                    world.Resources.Remove(res.ID);
                    var node = world.Map.Grid[res.GridPosition.x, res.GridPosition.y];
                    node.IsWalkable = true;
                    node.Type = SimTileType.Grass;
                    node.OccupantID = -1;
                    unit.State = SimTaskType.Idle;
                }
            }
        }

        private static void UpdateConstruction(SimUnitData unit, SimWorldState world, float dt)
        {
            if (!world.Buildings.TryGetValue(unit.TargetID, out SimBuildingData building))
            {
                unit.State = SimTaskType.Idle;
                return;
            }
            if (building.IsConstructed)
            {
                unit.State = SimTaskType.Idle;
                return;
            }

            unit.ActionTimer += dt;
            if (unit.ActionTimer >= SimConfig.BUILD_INTERVAL)
            {
                unit.ActionTimer = 0f;
                bool finished = SimBuildingSystem.AdvanceConstruction(building, world, SimConfig.BUILD_AMOUNT_PER_TICK);
                if (finished) unit.State = SimTaskType.Idle;
            }
        }

        public static void OrderMove(SimUnitData unit, int2 targetPos, SimWorldState world)
        {
            unit.Path = SimGridSystem.FindPath(world, unit.GridPosition, targetPos);
            if (unit.Path != null && unit.Path.Count > 0)
            {
                unit.State = SimTaskType.Moving;
                unit.TargetID = -1;
            }
        }

        public static void OrderBuild(SimUnitData worker, SimBuildingData building, SimWorldState world)
        {
            int2? standPos = SimGridSystem.FindWalkableNeighbor(world, building.GridPosition);
            if (standPos.HasValue)
            {
                worker.Path = SimGridSystem.FindPath(world, worker.GridPosition, standPos.Value);
                worker.TargetID = building.ID;
                if (worker.Path != null && worker.Path.Count > 0) worker.State = SimTaskType.Moving;
                else worker.State = SimTaskType.Building;
            }
        }

        public static bool TryAssignGatherTask(SimUnitData unit, SimResourceData targetRes, SimWorldState world)
        {
            int2? standPos = SimGridSystem.FindWalkableNeighbor(world, targetRes.GridPosition);
            if (standPos.HasValue)
            {
                unit.Path = SimGridSystem.FindPath(world, unit.GridPosition, standPos.Value);
                unit.TargetID = targetRes.ID;
                if (unit.Path != null && unit.Path.Count > 0) unit.State = SimTaskType.Moving;
                else unit.State = SimTaskType.Gathering;
                return true;
            }
            return false;
        }

        public static void OrderAttack(SimUnitData unit, SimBuildingData targetBuilding, SimWorldState world)
        {
            int2? standPos = SimGridSystem.FindWalkableNeighbor(world, targetBuilding.GridPosition);
            if (standPos.HasValue)
            {
                unit.Path = SimGridSystem.FindPath(world, unit.GridPosition, standPos.Value);
                unit.TargetID = targetBuilding.ID;
                if (unit.Path != null && unit.Path.Count > 0) unit.State = SimTaskType.Moving;
                else unit.State = SimTaskType.Attacking;
            }
        }

        public static void OrderAttackUnit(SimUnitData unit, SimUnitData targetUnit, SimWorldState world)
        {
            unit.Path = SimGridSystem.FindPath(world, unit.GridPosition, targetUnit.GridPosition);
            unit.TargetID = targetUnit.ID;
            if (unit.Path != null && unit.Path.Count > 0) unit.State = SimTaskType.Moving;
            else unit.State = SimTaskType.Attacking;
        }

        private static void KillUnit(SimUnitData unit, SimWorldState world)
        {
            unit.State = SimTaskType.Dead;
            world.Map.Grid[unit.GridPosition.x, unit.GridPosition.y].OccupantID = -1;
            world.Units.Remove(unit.ID);
            SimResourceSystem.ModifyPopulation(world, unit.PlayerID, -1);
        }

        private static void DestroyBuilding(SimBuildingData b, SimWorldState world)
        {
            world.Map.Grid[b.GridPosition.x, b.GridPosition.y].OccupantID = -1;
            world.Map.Grid[b.GridPosition.x, b.GridPosition.y].IsWalkable = true;
            world.Buildings.Remove(b.ID);
        }
    }
}