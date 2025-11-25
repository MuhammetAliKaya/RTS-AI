using UnityEngine;

/*
 * PlayerResourceData.cs
 *
 * This is a save-friendly data structure that holds
 * EVERY PLAYER'S resources, population, and Player ID.
 */

[System.Serializable]
public class PlayerResourceData
{
    public int playerID;
    public int wood;
    public int stone;
    public int meat;
    public int currentPopulation;
    public int maxPopulation;

    /// <summary>
    /// Constructor for default values to be used when starting a new game.
    /// </summary>
    public PlayerResourceData(int id)
    {
        this.playerID = id;

        // Starting resources
        this.wood = 0;
        this.stone = 0;
        this.meat = 50;

        this.currentPopulation = 0;
        this.maxPopulation = 10;
    }
}