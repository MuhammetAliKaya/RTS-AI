using RTS.Simulation.Data;
using RTS.Simulation.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RTS.Simulation.Systems
{
    public class SimUnitSystem
    {
        // ==============================================================================================
        // BÖLÜM 1: INSTANCE (ÖRNEK) YAPISI - DRL EĞİTİMİ İÇİN (Event Destekli)
        // ==============================================================================================

        // --- EVENTS (Sadece Instance üzerinden çalışanlar dinleyebilir) ---
        public event Action<SimUnitData, SimUnitData, float> OnUnitAttackedUnit;
        public event Action<SimUnitData, SimBuildingData, float> OnUnitAttackedBuilding;
        public event Action<SimUnitData, SimUnitData> OnUnitKilledEnemy;
        public event Action<SimUnitData, SimBuildingData> OnUnitDestroyedBuilding;

        private SimWorldState _world;

        public float PathRetryTimer = 0f;

        private List<int> _tempUnitList = new List<int>(); // Sınıf seviyesinde tanımla

        public SimUnitSystem(SimWorldState world = null)
        {
            _world = world ?? SimGameContext.ActiveWorld;
        }

        // --- INSTANCE METOTLARI (Event Tetikler) ---
        public void UpdateUnit(SimUnitData unit, float dt)
        {
            if (unit.State == SimTaskType.Dead) return;

            switch (unit.State)
            {
                case SimTaskType.Moving: UpdateMovement(unit, _world, dt); break; // Mantık aynı, static'i çağırabilir
                case SimTaskType.Gathering: UpdateGathering(unit, _world, dt); break;
                case SimTaskType.Building: UpdateConstruction(unit, _world, dt); break;
                case SimTaskType.Attacking: UpdateCombatInstance(unit, dt); break; // DİKKAT: Combat Instance özeldir!
                case SimTaskType.Idle: break;
            }
        }

        public void UpdateAllUnits(float dt)
        {
            _tempUnitList.Clear();
            _tempUnitList.AddRange(_world.Units.Keys);

            foreach (var unitID in _tempUnitList)
            {
                if (_world.Units.TryGetValue(unitID, out SimUnitData unit))
                {
                    UpdateUnit(unit, dt);
                }
            }
        }

        // Event tetikleyen özel Combat fonksiyonu
        private void UpdateCombatInstance(SimUnitData unit, float dt)
        {
            bool isUnit = _world.Units.TryGetValue(unit.TargetID, out SimUnitData enemyUnit);
            bool isBuilding = _world.Buildings.TryGetValue(unit.TargetID, out SimBuildingData enemyBuilding);

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
                unit.AttackTimer += dt;
                if (unit.AttackTimer >= unit.AttackSpeed)
                {
                    unit.AttackTimer = 0f;

                    if (isUnit)
                    {
                        enemyUnit.Health -= unit.Damage;

                        // EVENT TETİKLEME
                        OnUnitAttackedUnit?.Invoke(unit, enemyUnit, unit.Damage);

                        if (enemyUnit.Health <= 0)
                        {
                            OnUnitKilledEnemy?.Invoke(unit, enemyUnit);
                            KillUnit(enemyUnit, _world);
                        }
                    }
                    else if (isBuilding)
                    {
                        enemyBuilding.Health -= unit.Damage;

                        // EVENT TETİKLEME
                        OnUnitAttackedBuilding?.Invoke(unit, enemyBuilding, unit.Damage);

                        if (enemyBuilding.Health <= 0)
                        {
                            OnUnitDestroyedBuilding?.Invoke(unit, enemyBuilding);
                            DestroyBuilding(enemyBuilding, _world);
                        }
                    }
                }
            }
            else
            {
                // Menzilde değilse yürü (Static fonksiyonu kullanabiliriz)
                unit.State = SimTaskType.Idle;
                MoveToTarget(unit, targetPos, isBuilding, _world);
            }
        }


        // Wrapper Metotlar (Instance -> Static yönlendirmesi, kod tekrarını önler)
        public void OrderMove(SimUnitData unit, int2 targetPos) => OrderMove(unit, targetPos, _world);
        public void OrderBuild(SimUnitData worker, SimBuildingData building) => OrderBuild(worker, building, _world);
        public void OrderAttack(SimUnitData unit, SimBuildingData building) => OrderAttack(unit, building, _world);
        public void OrderAttackUnit(SimUnitData unit, SimUnitData target) => OrderAttackUnit(unit, target, _world);
        public bool TryAssignGatherTask(SimUnitData unit, SimResourceData res) => TryAssignGatherTask(unit, res, _world);


        // ==============================================================================================
        // BÖLÜM 2: STATIC (STATİK) YAPISI - ESKİ KODLARIN ÇALIŞMASI İÇİN (Legacy Support)
        // ==============================================================================================

        public static void UpdateUnit(SimUnitData unit, SimWorldState world, float dt)
        {
            if (unit.State == SimTaskType.Dead) return;

            switch (unit.State)
            {
                case SimTaskType.Moving: UpdateMovement(unit, world, dt); break;
                case SimTaskType.Gathering: UpdateGathering(unit, world, dt); break;
                case SimTaskType.Building: UpdateConstruction(unit, world, dt); break;
                case SimTaskType.Attacking: UpdateCombatStatic(unit, world, dt); break; // Event TETİKLEMEZ
                case SimTaskType.Idle: break;
            }
        }

        // Eski Statik Combat (Event Yok)
        private static void UpdateCombatStatic(SimUnitData unit, SimWorldState world, float dt)
        {
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
                unit.State = SimTaskType.Idle;
                MoveToTarget(unit, targetPos, isBuilding, world);
            }
        }

        // --- ORTAK MANTIK FONKSİYONLARI (Hem Static Hem Instance Kullanır) ---

        // private static void MoveToTarget(SimUnitData unit, int2 targetPos, bool isBuilding, SimWorldState world)
        // {
        //     if (unit.Path == null || unit.Path.Count == 0)
        //     {
        //         if (isBuilding)
        //         {
        //             int2? standPos = SimGridSystem.FindWalkableNeighbor(world, targetPos);
        //             if (standPos.HasValue) unit.Path = SimGridSystem.FindPath(world, unit.GridPosition, standPos.Value);
        //         }
        //         else
        //         {
        //             unit.Path = SimGridSystem.FindPath(world, unit.GridPosition, targetPos);
        //         }
        //     }
        //     unit.State = SimTaskType.Moving;
        // }

        private static void MoveToTarget(SimUnitData unit, int2 targetPos, bool isBuilding, SimWorldState world)
        {
            // 1. Bekleme süresindeyse (duvara takıldıysa) işlem yapma
            if (unit.PathRetryTimer > 0f) return;

            // 2. Zaten hedef karesindeysek çık
            if (unit.GridPosition.Equals(targetPos)) return;

            // 3. Yol hesapla (Sadece yol boşsa)
            if (unit.Path == null || unit.Path.Count == 0)
            {
                if (isBuilding)
                {
                    int2? standPos = SimGridSystem.FindWalkableNeighbor(world, targetPos);
                    if (standPos.HasValue)
                        unit.Path = SimGridSystem.FindPath(world, unit.GridPosition, standPos.Value);
                }
                else
                {
                    unit.Path = SimGridSystem.FindPath(world, unit.GridPosition, targetPos);
                }

                // 4. Sonuç Kontrolü
                if (unit.Path != null && unit.Path.Count > 0)
                {
                    unit.State = SimTaskType.Moving; // Yol bulduk, yürü
                }
                else
                {
                    // Yol BULAMADIK! 
                    // Saldırı animasyonunda kalmaması için zorla IDLE yapıyoruz.
                    unit.State = SimTaskType.Idle;
                    // 0.5 ile 1.5 sn arası rastgele bekle (CPU rahatlatır)
                    unit.PathRetryTimer = 0.5f + (float)new System.Random().NextDouble();
                }
            }
            else
            {
                // Zaten yolu var, yürümeye devam
                unit.State = SimTaskType.Moving;
            }
        }

        private static void UpdateMovement(SimUnitData unit, SimWorldState world, float dt)
        {
            if (unit.Path == null || unit.Path.Count == 0)
            {
                if (unit.TargetID != -1)
                {
                    // A. KAYNAK
                    if (world.Resources.ContainsKey(unit.TargetID))
                    {
                        unit.State = SimTaskType.Gathering;
                    }
                    // B. DÜŞMAN BİRLİK
                    else if (world.Units.TryGetValue(unit.TargetID, out SimUnitData targetUnit))
                    {
                        if (targetUnit.PlayerID != unit.PlayerID)
                        {
                            // YENİ EKLEME: Uzaklık kontrolü yap!
                            float distSq = SimGridSystem.GetDistanceSq(unit.GridPosition, targetUnit.GridPosition);
                            float rangeSq = unit.AttackRange * unit.AttackRange;

                            // Sadece menzildeysek saldır, değilse yürü
                            if (distSq <= rangeSq) unit.State = SimTaskType.Attacking;
                            else
                            {
                                unit.State = SimTaskType.Idle; // Önce duruşu düzelt
                                MoveToTarget(unit, targetUnit.GridPosition, false, world);
                            }
                        }
                        else unit.State = SimTaskType.Idle;
                    }
                    // C. BİNA
                    else if (world.Buildings.TryGetValue(unit.TargetID, out SimBuildingData targetBuilding))
                    {
                        // Bina İnşaatı veya Saldırı
                        if (targetBuilding.PlayerID == unit.PlayerID && !targetBuilding.IsConstructed)
                        {
                            float distSq = SimGridSystem.GetDistanceSq(unit.GridPosition, targetBuilding.GridPosition);
                            if (distSq <= 2.1f) unit.State = SimTaskType.Building;
                            else MoveToTarget(unit, targetBuilding.GridPosition, true, world);
                        }
                        else if (targetBuilding.PlayerID != unit.PlayerID)
                        {
                            // Bina Saldırısı: Menzil kontrolü
                            float distSq = SimGridSystem.GetDistanceSq(unit.GridPosition, targetBuilding.GridPosition);
                            float rangeSq = unit.AttackRange * unit.AttackRange;

                            if (distSq <= rangeSq) unit.State = SimTaskType.Attacking;
                            else
                            {
                                unit.State = SimTaskType.Idle; // Önce duruşu düzelt
                                MoveToTarget(unit, targetBuilding.GridPosition, true, world);
                            }
                        }
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

        private static void UpdateGathering(SimUnitData unit, SimWorldState world, float dt)
        {
            if (!world.Resources.TryGetValue(unit.TargetID, out SimResourceData res))
            {
                unit.State = SimTaskType.Idle;
                return;
            }

            // --- GÜVENLİK KONTROLÜ: KAYNAĞIN YANINDA MIYIZ? ---
            float distSq = SimGridSystem.GetDistanceSq(unit.GridPosition, res.GridPosition);
            if (distSq > 2.1f) // Yan yana (çapraz dahil) maksimum mesafe ~2 birim karedir.
            {

                // Yanında değilsek oraya yürü
                if (TryAssignGatherTask(unit, res, world))
                {
                    // State otomatik Moving olacak
                    return;
                }
                else
                {
                    // Gidemiyorsak iptal et
                    unit.State = SimTaskType.Idle;
                    return;
                }
            }
            else
            {
                // Debug.Log("// TOPLA UpdateMovement VARDIK YANINDAYIZ");
            }
            // ----------------------------------------------------

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

        // --- STATIC KOMUT METOTLARI (Eski kodlar için 3 parametreli) ---

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
            // Debug.Log("// TOPLA TryAssignGatherTask");
            int2? standPos = SimGridSystem.FindWalkableNeighbor(world, targetRes.GridPosition);
            if (standPos.HasValue)
            {
                // Debug.Log("// TOPLA standPos.HasValue");

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
        public static void MoveTo(SimUnitData unit, int2 targetPos, SimWorldState world)
        {
            OrderMove(unit, targetPos, world);
        }
    }
}