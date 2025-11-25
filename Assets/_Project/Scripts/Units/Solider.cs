using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Soldier : Unit
{
    [Header("Soldier Stats")]
    public float attackRange = 1.5f;
    public float attackRate = 1.0f;
    public int attackDamage = 10;

    [Header("AI / Auto-Attack")]
    public float visionRange = 4.0f;

    private Unit currentTargetUnit;
    private Building currentTargetBuilding;
    private float attackTimer;
    private bool isChasingTarget;

    public override void Die()
    {
        Debug.Log("Soldier died.");
        base.Die();
    }

    public override bool IsIdle()
    {
        bool baseIdle = base.IsIdle();
        if (!baseIdle) return false;
        return currentTargetUnit == null && currentTargetBuilding == null;
    }

    public void Attack(Unit target)
    {
        if (target == null) return;
        currentTargetBuilding = null;
        currentTargetUnit = target;
        isChasingTarget = true;
        IsBusy = true;
        Debug.Log($"[Soldier] Unit Attack: {target.name}");
    }

    public void AttackBuilding(Building target)
    {
        if (target == null) return;
        currentTargetUnit = null;
        currentTargetBuilding = target;
        isChasingTarget = true;
        IsBusy = true;
        Debug.Log($"[Soldier] Building Attack: {target.name}");
    }

    protected override void Update()
    {
        if (currentTargetBuilding != null)
        {
            HandleBuildingAttack();
            return;
        }

        if (currentTargetUnit != null)
        {
            HandleUnitAttack();
            return;
        }

        // If no specific target and idle, scan for enemies nearby
        if (currentPath == null)
        {
            ScanForEnemies();
        }

        base.HandleMovement();
    }

    private void ScanForEnemies()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, visionRange);
        foreach (Collider2D hit in hits)
        {
            Unit otherUnit = hit.GetComponent<Unit>();
            if (otherUnit != null && otherUnit.playerID != this.playerID && otherUnit.currentHealth > 0)
            {
                Attack(otherUnit);
                return;
            }
        }
    }

    private void HandleBuildingAttack()
    {
        if (currentTargetBuilding.currentHealth <= 0)
        {
            currentTargetBuilding = null;
            isChasingTarget = false;
            return;
        }

        Node startNode = TilemapVisualizer.Instance.NodeFromWorldPoint(this.transform.position);
        Node targetBuildingNode = TilemapVisualizer.Instance.NodeFromWorldPoint(currentTargetBuilding.transform.position);
        
        // Find a spot next to the building to attack from
        Node walkableSpot = FindWalkableSpotNear(targetBuildingNode, startNode);
        
        if (walkableSpot == null)
        {
            currentTargetBuilding = null;
            isChasingTarget = false;
            return;
        }
        
        Vector3 targetMovePos = TilemapVisualizer.Instance.WorldPositionFromNode(walkableSpot);

        if (startNode == walkableSpot)
        {
            // We are at the attack spot
            isChasingTarget = false;
            currentPath = null;
            
            attackTimer += Time.deltaTime;
            if (attackTimer >= (1f / attackRate))
            {
                attackTimer = 0f;
                PerformBuildingAttack();
            }
        }
        else
        {
            // Move towards the attack spot
            isChasingTarget = true;
            base.MoveTo(targetMovePos);
            base.HandleMovement();
        }
    }

    private void HandleUnitAttack()
    {
        if (currentTargetUnit.currentHealth <= 0)
        {
            currentTargetUnit = null;
            isChasingTarget = false;
            return;
        }

        Node startNode = TilemapVisualizer.Instance.NodeFromWorldPoint(this.transform.position);
        Node targetUnitNode = TilemapVisualizer.Instance.NodeFromWorldPoint(currentTargetUnit.transform.position);
        Node walkableSpot = FindWalkableSpotNear(targetUnitNode, startNode);

        if (walkableSpot == null)
        {
            base.HandleMovement();
            return;
        }
        
        Vector3 targetMovePos = TilemapVisualizer.Instance.WorldPositionFromNode(walkableSpot);

        if (Vector2.Distance(this.transform.position, targetMovePos) > attackRange)
        {
            isChasingTarget = true;
            base.MoveTo(targetMovePos);
            base.HandleMovement();
        }
        else
        {
            if (isChasingTarget)
            {
                isChasingTarget = false;
                currentPath = null;
            }
            
            attackTimer += Time.deltaTime;
            if (attackTimer >= (1f / attackRate))
            {
                attackTimer = 0f;
                PerformUnitAttack();
            }
        }
    }

    private void PerformUnitAttack()
    {
        if (currentTargetUnit == null) return;
        StartCoroutine(AttackShake());
        currentTargetUnit.TakeDamage(attackDamage);
    }

    private void PerformBuildingAttack()
    {
        if (currentTargetBuilding == null) return;
        StartCoroutine(AttackShake());
        currentTargetBuilding.TakeDamage(attackDamage);
    }

    private IEnumerator AttackShake()
    {
        if (spriteRenderer == null) yield break;
        
        float thrustDistance = 0.2f;
        float duration = 0.1f;
        int direction = spriteRenderer.flipX ? -1 : 1;
        
        Vector3 originalPos = transform.position;
        Vector3 thrustPos = originalPos + new Vector3(direction * thrustDistance, 0, 0);
        
        float timer = 0f;
        while (timer < duration / 2f)
        {
            transform.position = Vector3.Lerp(originalPos, thrustPos, timer / (duration / 2f));
            timer += Time.deltaTime;
            yield return null;
        }
        
        timer = 0f;
        while (timer < duration / 2f)
        {
            transform.position = Vector3.Lerp(thrustPos, originalPos, timer / (duration / 2f));
            timer += Time.deltaTime;
            yield return null;
        }
        
        transform.position = originalPos;
    }

    private Node FindWalkableSpotNear(Node targetNode, Node startNode)
    {
        if (gridSystem == null || startNode == null) return null;
        
        List<Node> validSpots = new List<Node>();
        
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0) continue;
                
                Node neighbour = gridSystem.GetNode(targetNode.x + x, targetNode.y + y);
                if (neighbour != null && neighbour.isWalkable)
                {
                    validSpots.Add(neighbour);
                }
            }
        }

        if (validSpots.Count == 0) return null;

        Node closestSpot = null;
        float minDistance = float.MaxValue;

        foreach (Node spot in validSpots)
        {
            int dx = spot.x - startNode.x;
            int dy = spot.y - startNode.y;
            float distance = (dx * dx) + (dy * dy);
            
            if (distance < minDistance)
            {
                minDistance = distance;
                closestSpot = spot;
            }
        }
        
        return closestSpot;
    }

    public override bool MoveTo(Vector3 targetWorldPosition)
    {
        // Reset attack targets when ordered to move manually
        currentTargetUnit = null;
        currentTargetBuilding = null;
        isChasingTarget = false;
        return base.MoveTo(targetWorldPosition);
    }
}