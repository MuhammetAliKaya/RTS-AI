using UnityEngine;
using System.Collections; // For Coroutine

/*
 * Farm.cs
 * Custom script for the 'Farm' building.
 * Inherits from the 'Building' base class.
 *
 * TASK (LOGIC):
 * 1. Starts a 'Coroutine' when 'OnBuildingComplete' is called (construction finished),
 * which passively produces Meat.
 * 2. Stops the Coroutine when the building is destroyed ('Die').
 */
public class Farm : Building // Inherits from 'Building' instead of 'MonoBehaviour'
{
    [Header("Farm Settings")]
    [Tooltip("Resource production interval in seconds (tick rate).")]
    public float productionRate = 15.0f; // Meat might be slower

    [Tooltip("Amount of Meat produced per production tick.")]
    public int meatPerProduction = 5; // 5 Meat

    private Coroutine productionCoroutine;


    public override void Die()
    {
        Debug.Log("Farm destroyed, passive production stopped.");

        if (productionCoroutine != null)
        {
            StopCoroutine(productionCoroutine);
        }

        if (buildingNode != null)
        {
            buildingNode.isWalkable = true;
        }

        Destroy(gameObject);
    }

    protected override void OnBuildingComplete()
    {
        base.OnBuildingComplete();

        Debug.Log("Farm finished! Passive Meat production starting.");
        productionCoroutine = StartCoroutine(ProduceResourcesCoroutine());
    }

    private IEnumerator ProduceResourcesCoroutine()
    {
        while (isFunctional)
        {
            yield return new WaitForSeconds(productionRate);

            if (GameManager.Instance.resourceManager != null)
            {
                // --- THE ONLY DIFFERENCE HERE ---
                GameManager.Instance.resourceManager.AddResource(playerID, ResourceType.Meat, meatPerProduction);
                Debug.Log($"Passive Income: +{meatPerProduction} Meat");
            }
        }
    }
}