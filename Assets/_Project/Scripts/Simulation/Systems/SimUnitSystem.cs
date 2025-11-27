using RTS.Simulation.Data;
using UnityEngine;

namespace RTS.Simulation.Systems
{
    public static class SimUnitSystem
    {
        // --- 1. İNŞAAT EMRİ ---
        public static void OrderBuild(SimUnitData worker, SimBuildingData building, SimWorldState world)
        {
            // İnşa edilecek yerin yanındaki boş kareyi bul
            int2? standPos = SimGridSystem.FindWalkableNeighbor(world, building.GridPosition);

            if (standPos == null) return; // Gidecek yer yok

            // Yol Çiz
            worker.Path = SimGridSystem.FindPath(world, worker.GridPosition, standPos.Value);
            worker.TargetID = building.ID;
            worker.ActionTimer = 0f;

            // Önce yürümesi lazım
            worker.State = SimTaskType.Moving;
        }

        // --- 2. TOPLAMA EMRİ ---
        public static bool TryAssignGatherTask(SimUnitData unit, SimResourceData targetRes, SimWorldState world)
        {
            int2? neighborSpot = SimGridSystem.FindWalkableNeighbor(world, targetRes.GridPosition);
            if (neighborSpot == null) return false;

            unit.Path = SimGridSystem.FindPath(world, unit.GridPosition, neighborSpot.Value);

            if (unit.Path != null)
            {
                unit.State = SimTaskType.Moving;
                unit.TargetID = targetRes.ID;
                unit.ActionTimer = 0f;
                return true;
            }
            return false;
        }

        // --- 3. YÜRÜME EMRİ (DEBUG LOGLU) ---
        public static void OrderMove(SimUnitData unit, int2 targetPos, SimWorldState world)
        {
            bool isWalkable = SimGridSystem.IsWalkable(world, targetPos);

            if (!isWalkable)
            {
                var node = world.Map.Grid[targetPos.x, targetPos.y];
                Debug.LogWarning($"🚫 HEDEF BLOKE: {targetPos} | IsWalkable: {node.IsWalkable} | OccupantID: {node.OccupantID}");

                int2? neighbor = SimGridSystem.FindWalkableNeighbor(world, targetPos);
                if (neighbor.HasValue)
                {
                    Debug.Log($"↪️ Rota Komşuya Çevrildi: {neighbor.Value}");
                    targetPos = neighbor.Value;
                }
                else
                {
                    Debug.LogError("❌ Gidecek hiçbir yer yok!");
                    return;
                }
            }
            else
            {
                Debug.Log($"✅ Hedef Uygun: {targetPos}. Direkt gidiliyor.");
            }

            unit.Path = SimGridSystem.FindPath(world, unit.GridPosition, targetPos);

            if (unit.Path != null && unit.Path.Count > 0)
            {
                unit.State = SimTaskType.Moving;
                unit.TargetID = -1;
                unit.ActionTimer = 0f;
            }
            else
            {
                Debug.LogError($"❌ Yol Bulunamadı! {unit.GridPosition} -> {targetPos}");
            }
        }

        // --- UPDATE DÖNGÜSÜ ---
        public static void UpdateUnit(SimUnitData unit, SimWorldState world, float dt)
        {
            if (unit.State == SimTaskType.Dead) return;

            if (unit.State == SimTaskType.Moving)
            {
                UpdateMovement(unit, world, dt);
                return;
            }

            switch (unit.State)
            {
                case SimTaskType.Gathering: UpdateGathering(unit, world, dt); break;
                case SimTaskType.Building: UpdateConstruction(unit, world, dt); break; // BURASI GÜNCELLENDİ
                case SimTaskType.Attacking: UpdateCombat(unit, world, dt); break;
            }
        }

        // --- HAREKET ---
        private static void UpdateMovement(SimUnitData unit, SimWorldState world, float dt)
        {
            if (unit.Path == null || unit.Path.Count == 0)
            {
                // Hedefe vardık
                if (world.Buildings.TryGetValue(unit.TargetID, out SimBuildingData building))
                {
                    unit.State = SimTaskType.Building; // İnşaata başla
                }
                else if (world.Resources.ContainsKey(unit.TargetID))
                {
                    unit.State = SimTaskType.Gathering;
                }
                else if (world.Units.ContainsKey(unit.TargetID))
                {
                    unit.State = SimTaskType.Attacking;
                }
                else
                {
                    unit.State = SimTaskType.Idle;
                }

                unit.ActionTimer = 0f;
                return;
            }

            unit.MoveProgress += unit.MoveSpeed * dt;

            if (unit.MoveProgress >= 1.0f)
            {
                unit.MoveProgress = 0f;
                int2 nextPos = unit.Path[0];
                unit.Path.RemoveAt(0);

                if (SimGridSystem.IsWalkable(world, nextPos))
                {
                    var oldNode = world.Map.Grid[unit.GridPosition.x, unit.GridPosition.y];
                    var newNode = world.Map.Grid[nextPos.x, nextPos.y];

                    oldNode.OccupantID = -1;
                    newNode.OccupantID = unit.ID;
                    unit.GridPosition = nextPos;
                }
                else
                {
                    unit.State = SimTaskType.Idle;
                    unit.Path.Clear();
                }
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

                int amount = Mathf.Min(SimConfig.GATHER_AMOUNT, res.AmountRemaining);
                res.AmountRemaining -= amount;

                SimResourceSystem.AddResource(world, unit.PlayerID, res.Type, amount);

                if (res.AmountRemaining <= 0)
                {
                    world.Resources.Remove(res.ID);
                    world.Map.Grid[res.GridPosition.x, res.GridPosition.y].IsWalkable = true;
                    world.Map.Grid[res.GridPosition.x, res.GridPosition.y].Type = SimTileType.Grass;
                    unit.State = SimTaskType.Idle;
                }
            }
        }

        // --- İNŞAAT (BURASI DEĞİŞTİ) ---
        private static void UpdateConstruction(SimUnitData unit, SimWorldState world, float dt)
        {
            if (!world.Buildings.TryGetValue(unit.TargetID, out SimBuildingData building))
            {
                unit.State = SimTaskType.Idle;
                return;
            }

            if (building.IsConstructed)
            {
                unit.State = SimTaskType.Idle; // Zaten bitmiş
                return;
            }

            unit.ActionTimer += dt;
            if (unit.ActionTimer >= SimConfig.BUILD_INTERVAL)
            {
                unit.ActionTimer = 0f;

                // ESKİSİ: Elle toplama yapıyorduk
                // building.ConstructionProgress += ...

                // YENİSİ: SimBuildingSystem'e devrediyoruz.
                // Bu fonksiyon, ilerlemeyi artırır ve %100 olunca 'OnBuildingCompleted' çalıştırır.
                bool finished = SimBuildingSystem.AdvanceConstruction(building, world, SimConfig.BUILD_AMOUNT_PER_TICK);

                if (finished)
                {
                    unit.State = SimTaskType.Idle; // Bina bitti, işçi boşa çıksın
                }
            }
        }

        // --- SAVAŞ ---
        private static void UpdateCombat(SimUnitData unit, SimWorldState world, float dt)
        {
            if (!world.Units.TryGetValue(unit.TargetID, out SimUnitData enemy))
            {
                unit.State = SimTaskType.Idle;
                return;
            }

            if (enemy.State == SimTaskType.Dead)
            {
                unit.State = SimTaskType.Idle;
                return;
            }

            float dist = SimGridSystem.GetDistance(unit.GridPosition, enemy.GridPosition);
            if (dist > unit.AttackRange)
            {
                unit.Path = SimGridSystem.FindPath(world, unit.GridPosition, enemy.GridPosition);
                if (unit.Path.Count > 0)
                {
                    unit.State = SimTaskType.Moving;
                }
                return;
            }

            unit.AttackTimer += dt;
            if (unit.AttackTimer >= unit.AttackSpeed)
            {
                unit.AttackTimer = 0f;
                enemy.Health -= unit.Damage;

                if (enemy.Health <= 0)
                {
                    enemy.State = SimTaskType.Dead;
                    enemy.Health = 0;
                    unit.State = SimTaskType.Idle;
                }
            }
        }
    }
}