using RTS.Simulation.Data;
using System; // System.Math için
using System.Collections.Generic;
using System.Linq;

// UnityEngine KÜTÜPHANESİ KALDIRILDI (Thread-Safe olması için)

namespace RTS.Simulation.Systems
{
    public static class SimUnitSystem
    {
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

        // --- HAREKET ---
        private static void UpdateMovement(SimUnitData unit, SimWorldState world, float dt)
        {
            if (unit.Path == null || unit.Path.Count == 0)
            {
                // YOL BİTTİ. ŞİMDİ NE YAPACAĞIM?
                if (unit.TargetID != -1)
                {
                    // 1. KAYNAK MI? -> TOPLA
                    if (world.Resources.ContainsKey(unit.TargetID))
                    {
                        unit.State = SimTaskType.Gathering;
                    }
                    // 2. BİRİM Mİ? -> SALDIR (Sadece düşmansa)
                    else if (world.Units.TryGetValue(unit.TargetID, out SimUnitData targetUnit))
                    {
                        if (targetUnit.PlayerID != unit.PlayerID)
                            unit.State = SimTaskType.Attacking;
                        else
                            unit.State = SimTaskType.Idle;
                    }
                    // 3. BİNA MI? -> İNŞA ET veya SALDIR
                    else if (world.Buildings.TryGetValue(unit.TargetID, out SimBuildingData targetBuilding))
                    {
                        // Benim binam ve bitmemiş -> İNŞA ET
                        if (targetBuilding.PlayerID == unit.PlayerID && !targetBuilding.IsConstructed)
                        {
                            unit.State = SimTaskType.Building;
                        }
                        // Düşman binası -> SALDIR
                        else if (targetBuilding.PlayerID != unit.PlayerID)
                        {
                            unit.State = SimTaskType.Attacking;
                        }
                        else
                        {
                            unit.State = SimTaskType.Idle;
                        }
                    }
                    else
                    {
                        unit.State = SimTaskType.Idle; // Hedef kaybolmuş
                    }
                }
                else
                {
                    unit.State = SimTaskType.Idle; // Hedefsiz yürüme bitti
                }
                return;
            }

            // --- YÜRÜME FİZİĞİ ---
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
                else
                {
                    unit.Path.Clear(); // Yol tıkandı
                }
            }
        }

        // --- SAVAŞ ---
        private static void UpdateCombat(SimUnitData unit, SimWorldState world, float dt)
        {
            // Hedef kontrolü
            bool isUnit = world.Units.TryGetValue(unit.TargetID, out SimUnitData enemyUnit);
            bool isBuilding = world.Buildings.TryGetValue(unit.TargetID, out SimBuildingData enemyBuilding);

            if ((!isUnit && !isBuilding) ||
                (isUnit && enemyUnit.State == SimTaskType.Dead) ||
                (isBuilding && !enemyBuilding.IsConstructed && enemyBuilding.PlayerID == unit.PlayerID))
            {
                unit.State = SimTaskType.Idle;
                unit.TargetID = -1;
                return;
            }

            int2 targetPos = isUnit ? enemyUnit.GridPosition : enemyBuilding.GridPosition;
            float distSq = SimGridSystem.GetDistanceSq(unit.GridPosition, targetPos);
            float rangeSq = unit.AttackRange * unit.AttackRange;

            if (distSq <= rangeSq)
            {
                // VUR
                unit.AttackTimer += dt;
                if (unit.AttackTimer >= unit.AttackSpeed)
                {
                    unit.AttackTimer = 0f;
                    if (isUnit)
                    {
                        enemyUnit.Health -= unit.Damage;
                        if (enemyUnit.Health <= 0) KillUnit(enemyUnit, world);
                    }
                    else if (isBuilding)
                    {
                        enemyBuilding.Health -= unit.Damage;
                        if (enemyBuilding.Health <= 0) DestroyBuilding(enemyBuilding, world);
                    }
                }
            }
            else
            {
                // KOVALA
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

        // --- TOPLAMA ---
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

                // DEĞİŞİKLİK BURADA: Mathf.Min -> Math.Min
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

        // --- İNŞAAT ---
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

        // --- ORDER FONKSİYONLARI ---
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

                if (worker.Path != null && worker.Path.Count > 0)
                    worker.State = SimTaskType.Moving;
                else
                    worker.State = SimTaskType.Building;
            }
        }

        public static bool TryAssignGatherTask(SimUnitData unit, SimResourceData targetRes, SimWorldState world)
        {
            int2? standPos = SimGridSystem.FindWalkableNeighbor(world, targetRes.GridPosition);
            if (standPos.HasValue)
            {
                unit.Path = SimGridSystem.FindPath(world, unit.GridPosition, standPos.Value);
                unit.TargetID = targetRes.ID;

                if (unit.Path != null && unit.Path.Count > 0)
                    unit.State = SimTaskType.Moving;
                else
                    unit.State = SimTaskType.Gathering;

                return true;
            }
            return false;
        }

        // --- YENİ EKLENENLER (Kaybolmaması İçin) ---
        public static void OrderAttack(SimUnitData unit, SimBuildingData targetBuilding, SimWorldState world)
        {
            int2? standPos = SimGridSystem.FindWalkableNeighbor(world, targetBuilding.GridPosition);
            if (standPos.HasValue)
            {
                unit.Path = SimGridSystem.FindPath(world, unit.GridPosition, standPos.Value);
                unit.TargetID = targetBuilding.ID;

                if (unit.Path != null && unit.Path.Count > 0)
                    unit.State = SimTaskType.Moving;
                else
                    unit.State = SimTaskType.Attacking;
            }
        }

        public static void OrderAttackUnit(SimUnitData unit, SimUnitData targetUnit, SimWorldState world)
        {
            unit.Path = SimGridSystem.FindPath(world, unit.GridPosition, targetUnit.GridPosition);
            unit.TargetID = targetUnit.ID;

            if (unit.Path != null && unit.Path.Count > 0)
                unit.State = SimTaskType.Moving;
            else
                unit.State = SimTaskType.Attacking;
        }

        // --- HELPER ---
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