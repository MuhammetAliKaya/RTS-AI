using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public enum GameState
{
    Loading,
    Playing,
    Paused,
    GameOver
}

public enum OpponentType
{
    HumanPlayer,
    ScriptedAI_Ref,
    Unused_Slot_3
}

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Game State")]
    public GameState currentState = GameState.Loading;

    [Header("Time Control")]
    public float initialTimeScale = 1.0f;

    [Header("Simulation Settings")]
    public bool isAIVsAI = false;
    public bool isHeadlessSimulation = false;
    public OpponentType player1Opponent = OpponentType.HumanPlayer;

    [Header("Systems")]
    public ResourceManager resourceManager;
    public TilemapVisualizer tilemapVisualizer;

    [Header("Controllers")]
    public InputManager player1Controller;
    public EnemyAIController player2Controller;
    public ReferenceAIController player1ReferenceAI;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        resourceManager = GetComponent<ResourceManager>();
        player1Controller = FindFirstObjectByType<InputManager>();
        player2Controller = FindFirstObjectByType<EnemyAIController>();
        player1ReferenceAI = FindFirstObjectByType<ReferenceAIController>();
        tilemapVisualizer = FindFirstObjectByType<TilemapVisualizer>();
    }

    void Start()
    {
        if (tilemapVisualizer != null)
        {
            tilemapVisualizer.Initialize();
        }
        else
        {
            Debug.LogError("[GameManager] TilemapVisualizer not found! Game cannot start.");
            this.enabled = false;
            return;
        }

        if (isAIVsAI)
        {
            if (player1Controller != null) { player1Controller.enabled = false; }

            if (player1ReferenceAI == null || player2Controller == null)
            {
                Debug.LogError("[GameManager] Controllers missing for AI vs AI!");
                this.enabled = false;
                return;
            }
            player1ReferenceAI.Initialize();
        }
        else
        {
            if (player1ReferenceAI != null) { player1ReferenceAI.enabled = false; }
        }

        if (isHeadlessSimulation)
        {
            Debug.LogWarning("SIMULATION MODE ACTIVE.");
            Camera.main.gameObject.SetActive(false);
            if (tilemapVisualizer != null) tilemapVisualizer.enabled = false;
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas != null) canvas.gameObject.SetActive(false);
            if (player1Controller != null) player1Controller.enabled = false;
        }

        currentState = GameState.Playing;
        Time.timeScale = initialTimeScale;
    }

    void Update()
    {
        if (currentState == GameState.Playing)
        {
            player2Controller?.RunAI();

            if (isAIVsAI && player1Opponent == OpponentType.ScriptedAI_Ref)
            {
                player1ReferenceAI?.RunAI();
            }
        }
    }

    public void SetGameSpeed(float newSpeed)
    {
        initialTimeScale = Mathf.Max(0.1f, newSpeed);
        if (currentState == GameState.Playing)
        {
            Time.timeScale = initialTimeScale;
        }
    }

    public void TogglePause()
    {
        if (currentState == GameState.Playing)
        {
            currentState = GameState.Paused;
            Time.timeScale = 0f;
        }
        else if (currentState == GameState.Paused)
        {
            currentState = GameState.Playing;
            Time.timeScale = initialTimeScale;
        }
    }
}