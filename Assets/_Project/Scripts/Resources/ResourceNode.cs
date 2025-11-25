using UnityEngine;
using System.Collections;

/*
 * ResourceNode.cs
 * * Manages resource nodes (Trees, Rocks, Meat Bushes, etc.).
 * Handles gathering logic, visual feedback (flashing/fading), and depletion.
 */
public class ResourceNode : MonoBehaviour
{
    [Header("Resource Settings")]
    public ResourceType resourceType;
    public int amountLeft = 250;

    [Tooltip("Prefab to spawn when resource is depleted (e.g., Stump).")]
    public GameObject depletedPrefab;

    [Tooltip("Should this object be destroyed when empty, or replaced with depletedPrefab?")]
    public bool destroyOnEmpty = true;

    private Node myNode;

    // --- Visuals (Fade and Flash) ---
    private SpriteRenderer spriteRenderer;
    private Color originalColor;
    private int initialAmount;

    void Start()
    {
        if (TilemapVisualizer.Instance != null)
        {
            myNode = TilemapVisualizer.Instance.NodeFromWorldPoint(transform.position);
            if (myNode == null)
            {
                Debug.LogError($"ResourceNode ({this.name}) is not on a valid Node!", this);
            }
            else
            {
                // Resource nodes are obstacles (unwalkable)
                myNode.isWalkable = false;
            }
        }

        // --- Visual Setup ---
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color; // Store original color
        }

        initialAmount = amountLeft;
    }

    /// <summary>
    /// Gathers resources from this node.
    /// Triggers a yellow flash effect on the sprite.
    /// </summary>
    /// <param name="gatherAmount">Amount to gather</param>
    /// <returns>Actual amount gathered</returns>
    public int GatherResource(int gatherAmount)
    {
        if (amountLeft <= 0)
        {
            return 0;
        }

        int amountTaken = 0;

        if (gatherAmount > amountLeft)
        {
            amountTaken = amountLeft;
            amountLeft = 0;
        }
        else
        {
            amountTaken = gatherAmount;
            amountLeft -= gatherAmount;
        }

        // Log disabled to reduce clutter, enable if needed
        // Debug.Log($"Resource collected: {amountTaken} {resourceType}. Remaining: {amountLeft}");

        // --- VISUAL LOGIC ---

        // 1. Calculate new alpha based on remaining amount (fades out as it depletes)
        float progress = (float)amountLeft / (float)initialAmount;
        // Ensure alpha doesn't go too low before destruction, but let's keep it simple
        Color newAlphaColor = new Color(originalColor.r, originalColor.g, originalColor.b, Mathf.Max(0.2f, progress));

        // 2. Update SpriteRenderer
        if (spriteRenderer != null)
        {
            // Set the new base color (with updated alpha)
            spriteRenderer.color = newAlphaColor;

            // 3. Trigger "Yellow Flash" effect
            StopCoroutine("FlashYellow");
            StartCoroutine(FlashYellow(newAlphaColor)); // Pass the new alpha color to return to
        }

        // Check if depleted
        if (amountLeft <= 0)
        {
            // Update the Node to be walkable again (Grass)
            if (myNode != null)
            {
                myNode.isWalkable = true;
                myNode.type = NodeType.Grass;
                if (TilemapVisualizer.Instance != null)
                {
                    TilemapVisualizer.Instance.RedrawNode(myNode);
                }
            }

            // Destruction Logic
            if (depletedPrefab != null)
            {
                Instantiate(depletedPrefab, transform.position, transform.rotation);
            }
            
            if (destroyOnEmpty)
            {
                Destroy(gameObject);
            }
        }

        return amountTaken;
    }

    /// <summary>
    /// Flashes the sprite yellow for 0.1 seconds when gathered,
    /// then returns to the specified color (preserving alpha).
    /// </summary>
    private IEnumerator FlashYellow(Color colorToReturnTo)
    {
        if (spriteRenderer == null) yield break;

        // Flash Yellow (keeping the target alpha to prevent snapping opacity)
        spriteRenderer.color = new Color(Color.yellow.r, Color.yellow.g, Color.yellow.b, colorToReturnTo.a);

        yield return new WaitForSeconds(0.1f);

        // Return to original color if still exists
        if (spriteRenderer != null)
        {
            spriteRenderer.color = colorToReturnTo;
        }
    }
}