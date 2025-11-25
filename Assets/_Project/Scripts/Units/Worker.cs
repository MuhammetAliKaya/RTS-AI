using UnityEngine;
using System.Collections.Generic;
using System.Collections;

/*
 * Worker.cs
 * Represents the Worker unit.
 * Handles Resource Gathering and Building Construction logic.
 * Inherits from the 'Unit' base class.
 */
public class Worker : Unit
{
    [Header("Worker Stats")]
    public float gatherRate = 2.0f;
    public int gatherAmount = 10;
    public float buildRate = 1.0f;
    public int buildAmount = 10;

    // --- Internal Task State ---
    private ResourceNode currentTargetResource;
    private Building currentTargetBuilding;
    private GameObject buildingPrefabToBuild;
    private Node buildTargetNode;
    private Node targetMoveNode;
    private float taskTimer;

    public override void Die()
    {
        Debug.Log("Worker died.");
        StopCurrentTask();
        base.Die();
    }

    public override bool IsIdle()
    {
        bool baseIdle = base.IsIdle();
        if (!baseIdle) return false;
        return currentTargetResource == null && currentTargetBuilding == null && buildingPrefabToBuild == null;
    }

    // --- GATHERING LOGIC ---
    public void Gather(ResourceNode resource)
    {
        if (resource == null) return;
        
        StopCurrentTask();
        
        currentTargetResource = resource;
        
        Node resourceNode = TilemapVisualizer.Instance.NodeFromWorldPoint(resource.transform.position);
        if (resourceNode == null) 
        { 
            StopCurrentTask(); 
            return; 
        }
        
        Node startNode = TilemapVisualizer.Instance.NodeFromWorldPoint(this.transform.position);
        if (startNode == null) 
        { 
            StopCurrentTask(); 
            return; 
        }
        
        // Find a walkable spot adjacent to the resource
        Node walkableSpot = FindWalkableSpotNear(resourceNode, startNode);

        if (walkableSpot != null)
        {
            targetMoveNode = walkableSpot;
            Vector3 targetPosition = TilemapVisualizer.Instance.WorldPositionFromNode(targetMoveNode);
            
            bool pathFound = base.MoveTo(targetPosition);
            if (!pathFound) 
            { 
                StopCurrentTask(); 
            } 
            else 
            { 
                IsBusy = true; 
            }
        }
        else 
        { 
            StopCurrentTask(); 
        }
    }

    // --- BUILDING LOGIC ---
    public bool StartBuildingTask(Node targetBuildNode, GameObject prefabToBuild)
    {
        if (targetBuildNode == null || prefabToBuild == null) return false;
        
        StopCurrentTask();
        
        Node startNode = TilemapVisualizer.Instance.NodeFromWorldPoint(this.transform.position);
        
        // Find a walkable spot adjacent to the build site
        Node walkableSpot = FindWalkableSpotNear(targetBuildNode, startNode);
        
        if (walkableSpot != null)
        {
            targetMoveNode = walkableSpot;
            Vector3 targetPosition = TilemapVisualizer.Instance.WorldPositionFromNode(targetMoveNode);
            
            bool pathFound = base.MoveTo(targetPosition);
            
            if (!pathFound) 
            { 
                StopCurrentTask(); 
                return false; 
            }
            
            // Task assigned successfully
            buildingPrefabToBuild = prefabToBuild;
            buildTargetNode = targetBuildNode;
            IsBusy = true;
            return true;
        }
        else 
        { 
            StopCurrentTask(); 
            return false; 
        }
    }

    private void StopCurrentTask()
    {
        // If we were constructing a building but stopped (e.g. manually moved away), notify the building to self-destruct if incomplete
        if (currentTargetBuilding != null && !currentTargetBuilding.isFunctional)
        {
            currentTargetBuilding.NotifyWorkerStoppedBuilding();
        }
        
        currentTargetResource = null;
        currentTargetBuilding = null;
        taskTimer = 0f;
        buildingPrefabToBuild = null;
        buildTargetNode = null;
        targetMoveNode = null;
        IsBusy = false;
    }

    // --- STATE MACHINE LOOP ---
    protected override void Update()
    {
        // 1. Handle Movement (Base Class)
        if (currentPath != null)
        {
            base.HandleMovement();
            return;
        }

        // If no target node for task, do nothing
        if (targetMoveNode == null) { return; }

        // Verify we are at the target spot
        Node myNode = TilemapVisualizer.Instance.NodeFromWorldPoint(this.transform.position);
        if (myNode != targetMoveNode) 
        { 
            // We stopped moving but aren't at the target? Something broke (e.g. collision). Stop task.
            StopCurrentTask(); 
            return; 
        }

        // 2. Handle Construction Initialization
        if (buildingPrefabToBuild != null)
        {
            // Instantiate the building structure
            Vector3 buildPos = TilemapVisualizer.Instance.WorldPositionFromNode(buildTargetNode);
            GameObject newBuildingObj = Instantiate(buildingPrefabToBuild, buildPos, Quaternion.identity);
            
            Building newBuilding = newBuildingObj.GetComponent<Building>();
            if (newBuilding == null) 
            { 
                Destroy(newBuildingObj); 
                StopCurrentTask(); 
                return; 
            }
            
            newBuilding.playerID = this.playerID;
            currentTargetBuilding = newBuilding;
            
            // Clear setup variables, now we focus on 'currentTargetBuilding'
            buildingPrefabToBuild = null;
            buildTargetNode = null;
        }

        // 3. Handle Resource Gathering Action
        if (currentTargetResource != null)
        {
            // Check distance again to be safe
            if (Vector2.Distance(this.transform.position, currentTargetResource.transform.position) > 1.6f)
            {
                StopCurrentTask();
            }
            else
            {
                taskTimer += Time.deltaTime;
                if (taskTimer >= gatherRate)
                {
                    taskTimer = 0f;
                    StartCoroutine(GatherShake());
                    
                    int amountTaken = currentTargetResource.GatherResource(gatherAmount);
                    if (amountTaken > 0)
                    {
                        GameManager.Instance.resourceManager.AddResource(this.playerID, currentTargetResource.resourceType, amountTaken);
                    }
                    else
                    {
                        // Resource depleted
                        StopCurrentTask();
                    }
                }
            }
        }
        // 4. Handle Constructing Action
        else if (currentTargetBuilding != null)
        {
            if (Vector2.Distance(this.transform.position, currentTargetBuilding.transform.position) > 1.6f)
            {
                StopCurrentTask();
            }
            else
            {
                taskTimer += Time.deltaTime;
                if (taskTimer >= buildRate)
                {
                    taskTimer = 0f;
                    StartCoroutine(ConstructShake());
                    
                    currentTargetBuilding.Construct(buildAmount);
                    
                    if (currentTargetBuilding.isFunctional)
                    {
                        // Job done
                        StopCurrentTask();
                    }
                }
            }
        }
    }

    public override bool MoveTo(Vector3 targetWorldPosition)
    {
        // Any manual move command stops current job
        StopCurrentTask();
        return base.MoveTo(targetWorldPosition);
    }

    private Node FindWalkableSpotNear(Node targetNode, Node startNode)
    {
        if (gridSystem == null || startNode == null) return null;
        
        List<Node> validSpots = new List<Node>();
        
        // Check 8 neighbours
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
        
        // Find closest valid spot to the worker
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

    // --- NEWLY ADDED (Static Helper) ---
    // Checks if a resource node has at least one walkable neighbour.
    // Used by AI to determine if it's worth trying to gather.
    public static bool CheckIfGatherable(ResourceNode resource)
    {
        if (resource == null || TilemapVisualizer.Instance == null) return false;
        
        GridSystem grid = TilemapVisualizer.Instance.gridSystem;
        if (grid == null) return false;
        
        Node resourceNode = TilemapVisualizer.Instance.NodeFromWorldPoint(resource.transform.position);
        if (resourceNode == null) return false;

        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0) continue;
                
                Node neighbour = grid.GetNode(resourceNode.x + x, resourceNode.y + y);
                if (neighbour != null && neighbour.isWalkable) return true;
            }
        }
        return false;
    }
    // -------------------------------------------

    // Visual Feedback Coroutines
    private IEnumerator ConstructShake()
    {
        if (spriteRenderer == null) yield break;
        spriteRenderer.flipX = true;
        yield return new WaitForSeconds(0.05f);
        spriteRenderer.flipX = false;
        yield return new WaitForSeconds(0.05f);
    }

    private IEnumerator GatherShake()
    {
        if (spriteRenderer == null) yield break;
        spriteRenderer.flipX = true;
        yield return new WaitForSeconds(0.05f);
        spriteRenderer.flipX = false;
        yield return new WaitForSeconds(0.05f);
    }
}