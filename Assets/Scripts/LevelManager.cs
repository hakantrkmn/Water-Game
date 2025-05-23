using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;

public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    public Tile startTile;
    public Tile endTile;
    private HashSet<Tile> tilesWithWater = new HashSet<Tile>();
    
    [Header("Water Flow Settings")]
    public float waterFlowDelay = 0.2f; // Delay between each step of water flow
    
    private Coroutine waterFlowCoroutine;
    public bool isLevelCompleted = false;

    public int totalTilesWithWater = 0;
    public int maxFillableTiles = 0;
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    /// <summary>
    /// Start is called on the frame when a script is enabled just before
    /// any of the Update methods is called the first time.
    /// </summary>

    void OnEnable()
    {
        EventManager.LevelGenerated += OnLevelGenerated;
    }

    private void OnLevelGenerated()
    {
        SimulateWaterFlow();
    }

    void OnDisable()
    {
        EventManager.LevelGenerated -= OnLevelGenerated;
    }

    public void SetStartAndEndTiles(Tile start, Tile end)
    {
        startTile = start;
        endTile = end;
    }

    public void OnTileRotated()
    {
        SimulateWaterFlow();
    }

    public void SimulateWaterFlow()
    {
        if (startTile == null) return;

        // Stop any existing water flow animation
        if (waterFlowCoroutine != null)
        {
            StopCoroutine(waterFlowCoroutine);
        }
        
        // Clear previous water flow
        foreach (var tile in tilesWithWater)
        {
            tile.hasWater = false;
        }
        tilesWithWater.Clear();
        
        // Start new water flow animation
        waterFlowCoroutine = StartCoroutine(AnimateWaterFlow());
    }
    
    private IEnumerator AnimateWaterFlow()
    {
        // Start with the start tile
        startTile.SetWaterFlow(true);
        tilesWithWater.Add(startTile);
        
        // Keep track of tiles to process in each wave
        List<Tile> currentWave = new List<Tile> { startTile };
        
        // Set of tiles that already had water before this simulation
        HashSet<Tile> preExistingWaterTiles = new HashSet<Tile>();
        
        // Continue until no more tiles can be filled
        while (currentWave.Count > 0)
        {
            // Wait for the specified delay - but only if the current wave isn't all pre-existing water tiles
            bool allPreExisting = currentWave.All(t => preExistingWaterTiles.Contains(t));
            if (!allPreExisting)
            {
                yield return new WaitForSeconds(waterFlowDelay);
            }
            
            // Find all tiles that will receive water in this wave
            List<Tile> nextWave = new List<Tile>();
            
            foreach (var tile in currentWave)
            {
                // Check each open direction
                foreach (int direction in tile.openDirections)
                {
                    // Get neighbor in this direction
                    Tile neighbor = tile.neighbors[direction - 1];
                    if (neighbor == null) continue;
                    
                    // Find opposite direction
                    int oppositeDirection = Tile.Opposite(direction);
                    
                    // If neighbor has matching opening and doesn't have water yet or isn't in our current tracking set
                    if (neighbor.openDirections.Contains(oppositeDirection) && !tilesWithWater.Contains(neighbor))
                    {
                        // Add to next wave
                        nextWave.Add(neighbor);
                        
                        // If the neighbor already had water, add it to our pre-existing set
                        if (neighbor.lastState)
                        {
                            preExistingWaterTiles.Add(neighbor);
                        }
                        
                        // Debug log
                        Debug.Log($"Water flowing: {tile.gameObject.name} -> {neighbor.gameObject.name}, " +
                                $"Direction: {direction} -> {oppositeDirection}");
                    }
                }
            }
            
            // Add water to all tiles in the next wave
            foreach (var tile in nextWave)
            {
                // Use a delay of 0 for tiles that already had water
                float tileDelay = preExistingWaterTiles.Contains(tile) ? 0f : waterFlowDelay;
                tile.SetWaterFlow(true, tileDelay);
                tilesWithWater.Add(tile);
            }
            
            // Move to the next wave
            currentWave = nextWave;
        }
        
        Tile[] allTiles = EventManager.GetAllTiles();
        totalTilesWithWater = tilesWithWater.Count;
        //setwaterflow to false for tiles that not in tilesWithWater
        foreach(var tile in allTiles)
        {
            if(!tilesWithWater.Contains(tile))
            {
                tile.SetWaterFlow(false);
            }
        }
        
        // Check if water reached the end tile
        if (endTile != null)
        {
            if (endTile.hasWater)
            {
                if(totalTilesWithWater >= maxFillableTiles)
                {
                    isLevelCompleted = true;
                    EventManager.LevelCompleted?.Invoke();
                    Debug.Log("Success! Water reached the end tile!");
                    // Optional: Trigger level completion event
                    // EventManager.LevelCompleted?.Invoke();
                }
            }
            else
            {
                Debug.Log("Water did not reach the end tile.");
            }
        }
        
        // Debug: Show all tiles with water
        Debug.Log($"Tiles with water: {string.Join(", ", tilesWithWater.Select(t => t.gameObject.name))}");
    }
} 