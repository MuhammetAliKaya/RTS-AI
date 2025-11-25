using UnityEngine;
using System.Collections.Generic;
using System.Collections;

[System.Serializable]
public class BuildingCost
{
    public int woodCost;
    public int stoneCost;
    public int meatCost;

    public bool HasAnyCost()
    {
        return woodCost > 0 || stoneCost > 0 || meatCost > 0;
    }
}

/*
 * Building.cs
 * * Abstract base class for all buildings.
 * Handles health, construction progress, visual tinting, and destruction.
 */
public abstract class Building : MonoBehaviour
{
    [Header("Building Stats")]
    public int maxHealth = 100;
    public int currentHealth;
    public bool isFunctional { get; protected set; }

    [Header("Cost")]
    public BuildingCost cost;

    [Header("Ownership")]
    public int playerID = 1;

    [Header("Visuals")]
    public List<Color> playerTints;

    [Tooltip("If true, building starts as a transparent foundation (10% alpha).")]
    public bool startAsFoundation = true;

    protected SpriteRenderer spriteRenderer;
    protected Node buildingNode;

    private Color originalPlayerColor = Color.white;
    private float initialAlpha = 0.1f;


    // --- Awake() and Start() Logic ---

    protected virtual void Awake()
    {
        // ONLY set basic state. Do NOT assign color here.
        currentHealth = 0;
        isFunctional = false;

        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }
    }

    protected virtual void Start()
    {
        // Find Node and make unwalkable
        buildingNode = TilemapVisualizer.Instance.NodeFromWorldPoint(transform.position);
        if (buildingNode != null)
        {
            buildingNode.isWalkable = false;
        }
        else
        {
            Debug.LogError($"Building ({this.name}) placed on invalid Node!", this);
        }

        if (spriteRenderer != null)
        {
            // Set color index based on playerID (ID 1 = index 0, ID 2 = index 1)
            int colorIndex = playerID - 1;

            if (playerTints != null && playerTints.Count > colorIndex && colorIndex >= 0)
            {
                originalPlayerColor = playerTints[colorIndex];
                spriteRenderer.color = originalPlayerColor;
            }
            else
            {
                originalPlayerColor = Color.white;
                Debug.LogWarning($"No 'playerTints' set for Building ({this.name}) or invalid playerID.", this);
            }
        }

        // Set initial alpha if starting as foundation
        if (!isFunctional && startAsFoundation)
        {
            SetPlayerTint(initialAlpha);
        }
    }

    // --- INITIALIZATION ---
    public virtual void ForceCompleteBuild()
    {
        currentHealth = maxHealth;
        isFunctional = true;
        OnBuildingComplete();
    }

    // --- LOGIC FUNCTIONS ---

    public virtual void Construct(int amount)
    {
        if (isFunctional || currentHealth < 0) return;

        currentHealth += amount;

        // Update Alpha Fade-in
        if (spriteRenderer != null && startAsFoundation)
        {
            float progress = (float)currentHealth / (float)maxHealth;
            float currentAlpha = Mathf.Lerp(initialAlpha, 1.0f, progress);
            SetPlayerTint(currentAlpha);
        }

        if (currentHealth >= maxHealth)
        {
            currentHealth = maxHealth;
            isFunctional = true;
            OnBuildingComplete();
        }
    }

    public virtual void TakeDamage(int damage)
    {
        if (currentHealth <= 0 && !isFunctional) return;

        currentHealth -= damage;

        StartCoroutine(FlashRed());

        // Update Alpha when taking damage
        if (spriteRenderer != null && startAsFoundation)
        {
            float progress = (float)currentHealth / (float)maxHealth;
            float currentAlpha = Mathf.Lerp(initialAlpha, 1.0f, progress);
            SetPlayerTint(currentAlpha);
        }

        if (currentHealth <= 0)
        {
            currentHealth = 0;
            isFunctional = false;
            Die();
        }
    }

    private IEnumerator FlashRed()
    {
        if (spriteRenderer == null) yield break;

        spriteRenderer.color = Color.red;
        yield return new WaitForSeconds(0.1f);

        if (spriteRenderer != null)
        {
            // Return to original player color
            spriteRenderer.color = originalPlayerColor;
        }
    }

    protected virtual void OnBuildingComplete()
    {
        Debug.Log($"{this.name} construction complete!");

        // Set color to fully opaque (100% Alpha)
        SetPlayerTint(1.0f);
    }

    // --- HELPER FUNCTIONS ---
    /// <summary>
    /// Sets the SpriteRenderer color to the correct player tint with the specified alpha.
    /// </summary>
    protected void SetPlayerTint(float alpha)
    {
        if (spriteRenderer == null) return;

        int colorIndex = playerID - 1;
        Color newColor = Color.white;

        // Get color from list
        if (playerTints != null && playerTints.Count > colorIndex && colorIndex >= 0)
        {
            newColor = playerTints[colorIndex];
        }

        newColor.a = alpha; // Set alpha

        spriteRenderer.color = newColor;
    }

    public virtual void NotifyWorkerStoppedBuilding()
    {
        if (!isFunctional)
        {
            Debug.Log($"Construction of ({this.name}) was cancelled/incomplete. Building destroyed.");
            Die();
        }
    }

    public abstract void Die();
}