using UnityEngine;
using TMPro; // Required for TextMeshPro
using UnityEngine.UI; // Required for Button Image color changes

/*
 * UIManager.cs
 *
 * Manages the UI updates for resources, population, and production panels.
 * Handles both Player 1 (User) and Player 2 (Enemy AI) UI elements.
 * Controls button availability visualization (greying out buttons if resources are insufficient).
 */
public class UIManager : MonoBehaviour
{
    // --- Singleton Setup ---
    public static UIManager Instance { get; private set; }

    [Header("Player 1 (User) UI References")]
    public TextMeshProUGUI woodText;
    public TextMeshProUGUI stoneText;
    public TextMeshProUGUI meatText;
    public TextMeshProUGUI populationText;

    [Header("Player 2 (Enemy AI) UI References")]
    public TextMeshProUGUI enemyWoodText;
    public TextMeshProUGUI enemyStoneText;
    public TextMeshProUGUI enemyMeatText;
    public TextMeshProUGUI enemyPopulationText;

    [Header("Production UI References")]
    public GameObject baseProductionPanel;
    public GameObject barracksProductionPanel;

    [Header("Production Button Images")]
    public Image trainWorkerButtonImage;
    public Image trainSoldierButtonImage;

    [Header("Build Buttons (Prefabs for Cost Checking)")]
    public GameObject housePrefab;
    public GameObject barracksPrefab;
    public GameObject woodcutterPrefab;
    public GameObject stonepitPrefab;
    public GameObject farmPrefab;
    public GameObject wallPrefab;
    public GameObject deffenceTowerPrefab;

    [Header("Build Button Images")]
    public Image houseButtonImage;
    public Image barracksButtonImage;
    public Image woodcutterButtonImage;
    public Image stonepitButtonImage;
    public Image farmButtonImage;
    public Image wallButtonImage;
    public Image deffenceTowerButtonImage;

    [Header("Color Settings")]
    public Color availableColor = Color.green;
    public Color unavailableColor = Color.gray;

    // --- System References ---
    private ResourceManager resourceManager;
    private InputManager inputManager;

    // --- Player Data References ---
    private PlayerResourceData player1Resources;
    private PlayerResourceData player2Resources;
    private int player1_ID = 1;
    private int player2_ID = 2;


    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); }
        else { Instance = this; }
    }

    void Start()
    {
        GameManager gm = FindFirstObjectByType<GameManager>();
        if (gm == null) 
        { 
            Debug.LogError("[UIManager] GameManager not found!"); 
            this.enabled = false; 
            return; 
        }

        resourceManager = gm.resourceManager;
        inputManager = gm.player1Controller;

        if (resourceManager == null || inputManager == null)
        { 
            Debug.LogError("[UIManager] Initialization error: Missing manager references!"); 
            this.enabled = false; 
            return; 
        }

        // Get resource data for both players (to display stats)
        player1Resources = resourceManager.GetPlayerResources(player1_ID);
        player2Resources = resourceManager.GetPlayerResources(player2_ID);

        if (woodText == null || stoneText == null || meatText == null || populationText == null)
        { 
            Debug.LogError("[UIManager] One of the Text references is missing in Inspector!", this); 
        }

        HideAllProductionPanels();
    }

    void Update()
    {
        if (resourceManager != null)
        {
            // Update Player 1 (User) UI
            if (player1Resources != null)
            {
                woodText.text = $"Wood: {player1Resources.wood}";
                stoneText.text = $"Stone: {player1Resources.stone}";
                meatText.text = $"Meat: {player1Resources.meat}";
                populationText.text = $"Pop: {player1Resources.currentPopulation} / {player1Resources.maxPopulation}";
            }

            // Update Player 2 (Enemy AI) UI - Useful for debugging/spectating
            if (player2Resources != null)
            {
                if (enemyWoodText != null) enemyWoodText.text = $"Wood: {player2Resources.wood}";
                if (enemyStoneText != null) enemyStoneText.text = $"Stone: {player2Resources.stone}";
                if (enemyMeatText != null) enemyMeatText.text = $"Meat: {player2Resources.meat}";
                if (enemyPopulationText != null) enemyPopulationText.text = $"Pop: {player2Resources.currentPopulation} / {player2Resources.maxPopulation}";
            }

            // Update Button Colors (Only for Player 1)
            UpdateButtonAvailability();
        }
    }

    /// <summary>
    /// Updates the color of Player 1's build buttons based on resource availability.
    /// </summary>
    private void UpdateButtonAvailability()
    {
        // Building Buttons
        CheckAndSetButtonColor(housePrefab, houseButtonImage, player1_ID);
        CheckAndSetButtonColor(barracksPrefab, barracksButtonImage, player1_ID);
        CheckAndSetButtonColor(woodcutterPrefab, woodcutterButtonImage, player1_ID);
        CheckAndSetButtonColor(stonepitPrefab, stonepitButtonImage, player1_ID);
        CheckAndSetButtonColor(farmPrefab, farmButtonImage, player1_ID);
        CheckAndSetButtonColor(wallPrefab, wallButtonImage, player1_ID);
        CheckAndSetButtonColor(deffenceTowerPrefab, deffenceTowerButtonImage, player1_ID);

        // Unit Training Buttons
        CheckAndSetTrainButtonColor();
    }

    /// <summary>
    /// Checks if the player can afford the building and sets the button color.
    /// </summary>
    private void CheckAndSetButtonColor(GameObject prefab, Image buttonImage, int playerID)
    {
        if (prefab == null || buttonImage == null) return;

        Building building = prefab.GetComponent<Building>();
        if (building == null || building.cost == null) return;

        bool canAfford = resourceManager.CanAfford(
            playerID,
            building.cost.woodCost,
            building.cost.stoneCost,
            building.cost.meatCost
        );

        buttonImage.color = canAfford ? availableColor : unavailableColor;
    }

    /// <summary>
    /// Checks if the player can afford to train units and sets the button color.
    /// </summary>
    private void CheckAndSetTrainButtonColor()
    {
        if (trainWorkerButtonImage == null || trainSoldierButtonImage == null) return;
        if (inputManager == null || inputManager.selectedBuilding == null || player1Resources == null) return;

        bool hasPopulationCapacity = player1Resources.currentPopulation < player1Resources.maxPopulation;

        // 1. Worker Check (If Base is selected)
        Base baseComponent = inputManager.selectedBuilding as Base;
        if (baseComponent != null)
        {
            ProductionCost workerCost = baseComponent.workerProductionCost;
            bool canAffordWorker = resourceManager.CanAfford(player1_ID, workerCost.woodCost, workerCost.stoneCost, workerCost.meatCost);
            trainWorkerButtonImage.color = (hasPopulationCapacity && canAffordWorker) ? availableColor : unavailableColor;
        }

        // 2. Soldier Check (If Barracks is selected)
        Barracks barracksComponent = inputManager.selectedBuilding as Barracks;
        if (barracksComponent != null)
        {
            ProductionCost soldierCost = barracksComponent.soldierProductionCost;
            bool canAffordSoldier = resourceManager.CanAfford(player1_ID, soldierCost.woodCost, soldierCost.stoneCost, soldierCost.meatCost);
            trainSoldierButtonImage.color = (hasPopulationCapacity && canAffordSoldier) ? availableColor : unavailableColor;
        }
    }

    // --- UI Panel Control ---

    public void ShowBuildingPanel(Building building)
    {
        HideAllProductionPanels();
        
        if (building is Base) 
        { 
            if (baseProductionPanel != null) baseProductionPanel.SetActive(true); 
        }
        else if (building is Barracks) 
        { 
            if (barracksProductionPanel != null) barracksProductionPanel.SetActive(true); 
        }
    }

    public void HideAllProductionPanels()
    {
        if (baseProductionPanel != null) { baseProductionPanel.SetActive(false); }
        if (barracksProductionPanel != null) { barracksProductionPanel.SetActive(false); }
    }

    // --- Button Click Events (Linked from Unity Editor) ---

    public void OnClick_TrainWorker()
    {
        if (inputManager == null) return;

        Building selectedBuilding = inputManager.selectedBuilding;
        if (selectedBuilding != null && selectedBuilding is Base)
        { 
            (selectedBuilding as Base).StartTrainingWorker(); 
        }
    }

    public void OnClick_TrainSoldier()
    {
        if (inputManager == null) return;

        Building selectedBuilding = inputManager.selectedBuilding;
        if (selectedBuilding != null && selectedBuilding is Barracks)
        { 
            (selectedBuilding as Barracks).StartTrainingSoldier(); 
        }
    }
}