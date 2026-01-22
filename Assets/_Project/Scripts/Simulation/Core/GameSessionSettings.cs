using UnityEngine;

public static class GameSessionSettings
{
    public static bool IsLoadedFromMenu = false;

    // Önekler kalktı:
    public static PlayerControllerType P1Controller;
    public static AIOpponentType P1BotType;

    public static PlayerControllerType P2Controller;
    public static AIOpponentType P2BotType;
    public static AIDifficulty P2Difficulty;
}