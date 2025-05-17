// TileGenerator.cs
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// New class to hold solution path details
public class SolutionPathInfo {
    public List<Vector2Int> PathPositions; // Includes start and end
    public Dictionary<Vector2Int, int> Rotations; // Tile Pos on path (excluding end) -> Rotation Value (0-3)

    public SolutionPathInfo(List<Vector2Int> path, Dictionary<Vector2Int, int> rotations) {
        PathPositions = path;
        Rotations = rotations;
    }
}

public class LevelGenerator : MonoBehaviour
{
    [Header("Grid Settings")]
    public int width = 10;
    public int height = 10;
    public float spacing = 1.1f;

    [Header("Difficulty (1=Easy, 10=Hard)")]
    [Range(1, 10)]
    public int difficultyRating = 5;

    [Header("Tile Prefabs")]
    public GameObject emptyPrefab;
    public GameObject straightPrefab;   // I‑shape
    public GameObject cornerPrefab;     // L‑shape
    public GameObject tPrefab;          // T‑shape
    public GameObject crossPrefab;      // +‑shape
    public GameObject startPrefab;
    public GameObject endPrefab;

    [Header("Critical Path Settings")]
    [Tooltip("Material to highlight tiles on the critical path")]
    public Material criticalPathMaterial;
    [Tooltip("Material to highlight key tiles that must be correctly positioned")]
    public Material keyTileMaterial;

    public int maxFillableTiles = 0;    
    private Tile[,] grid;
    public List<Tile> tiles = new List<Tile>();
    private Transform container;
    private Vector2Int startPos;
    private Vector2Int endPos;
    
    // Permanent start and end tile references
    private Tile startTile;
    private Tile endTile;
    
    // Difficulty-based adjustable values
    private float deadEndProbability;
    private float misleadingPathProbability;
    private int maxPathLength;
    private float pathStraightnessBias; // New
    
    // Critical path tracking
    private List<Vector2Int> criticalPath;
    private List<Vector2Int> keyTilePositions;
    private int minValidPathsRequired = 1;
    private int maxValidPathsAllowed = 10;

#if UNITY_EDITOR
[CustomEditor(typeof(LevelGenerator))]
public class LevelGeneratorEditor : Editor 
{
    public override void OnInspectorGUI()
    {
        // Draw the default inspector
        DrawDefaultInspector();
        
        // Get reference to the LevelGenerator
        LevelGenerator generator = (LevelGenerator)target;
        
        // Add some space
        EditorGUILayout.Space();
        
        // Create Level button
        if (GUILayout.Button("Create Level"))
        {
            // Create the level in edit mode
            generator.CreateLevelInEditor();
        }
        
        // Test Solvability button
        if (GUILayout.Button("Test Solvability"))
        {
            bool isSolvable = generator.ValidateLevelSolvability();
            string message = isSolvable 
                ? "Level is solvable with proper tile rotations!" 
                : "Level is NOT solvable even with all possible rotations!";
                
            EditorUtility.DisplayDialog("Level Solvability", message, "OK");
        }

        // Calculate Max Fillable Tiles button
        if (GUILayout.Button("Calculate Max Fillable Tiles"))
        {
            SolutionPathInfo mainSolutionPath;
            int maxFillCount = generator.CalculateMaxFillableTiles(out mainSolutionPath);
            
            string pathDetails = "No S-E path found.";
            if (mainSolutionPath != null && mainSolutionPath.PathPositions != null && mainSolutionPath.PathPositions.Count > 0)
            {
                pathDetails = $"Main S-E path has {mainSolutionPath.PathPositions.Count} tiles.";
                // Optional: Log more details about the path to console
                // Debug.Log($"Main S-E Path Details: {string.Join(", ", mainSolutionPath.PathPositions)}");
                // foreach(var entry in mainSolutionPath.Rotations)
                // {
                //    Debug.Log($"  Tile {entry.Key} uses rotation {entry.Value}");
                // }
            }

            string message = $"Max Fillable Tiles: {maxFillCount}\n(Based on one S-E path: {pathDetails})";
            EditorUtility.DisplayDialog("Max Fill Calculation", message, "OK");
        }
        
        // Add some space
        EditorGUILayout.Space();
        
        // Clear Level button
        if (GUILayout.Button("Clear Level"))
        {
            if (generator.transform.childCount > 0)
            {
                while (generator.transform.childCount > 0)
                {
                    DestroyImmediate(generator.transform.GetChild(0).gameObject);
                }
                generator.container = null;
                
                // Reset references
                generator.startTile = null;
                generator.endTile = null;
                generator.tiles.Clear();
                
                // Mark scene as dirty
                EditorUtility.SetDirty(generator);
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }
        }
    }
}
#endif


    void OnEnable()
    {
        EventManager.GetAllTiles += GetAllTiles;
    }

    void OnDisable()
    {
        EventManager.GetAllTiles -= GetAllTiles;
    }
    // Helper method to get or create the container
    private Transform GetOrCreateContainer()
    {
        // Check if we already have a child - that would be our container
        if (transform.childCount > 0)
        {
            container = transform.GetChild(0);
            if (container.name != "Tiles")
            {
                container.name = "Tiles";
            }
            return container;
        }
        
        // Create a new container as our child
        GameObject containerGO = new GameObject("Tiles");
        containerGO.transform.SetParent(transform, false);
        containerGO.transform.localPosition = Vector3.zero;
        container = containerGO.transform;
        return container;
    }

    // This method is for runtime initialization
    void Start()
    {
        Debug.Log(ES3.Load("level"));
        // Check if we already have a level (child container with tiles)
        if (transform.childCount > 0)
        {
            container = transform.GetChild(0);
            Debug.Log($"Using existing level from child container with {container.childCount} tiles");
            
            // Initialize the grid
            grid = new Tile[height, width];
            tiles.Clear();
            
            // Find all tile components in the container
            Tile[] existingTiles = container.GetComponentsInChildren<Tile>();
            Debug.Log($"Found {existingTiles.Length} tiles in the container");
            
            foreach (Tile tile in existingTiles)
            {
                // Add to tiles list
                tiles.Add(tile);
                
                // Calculate grid position from world position
                int x = Mathf.RoundToInt(tile.transform.position.x / spacing);
                int y = Mathf.RoundToInt(tile.transform.position.z / spacing);
                
                // Ensure we're within grid bounds
                if (x >= 0 && x < width && y >= 0 && y < height)
                {
                    grid[y, x] = tile;
                    
                    // Check if this is a start or end tile
                    if (tile.gameObject.name.Contains("StartTile"))
                    {
                        startTile = tile;
                        startPos = new Vector2Int(x, y);
                    }
                    else if (tile.gameObject.name.Contains("EndTile"))
                    {
                        endTile = tile;
                        endPos = new Vector2Int(x, y);
                    }
                }
                else
                {
                    Debug.LogWarning($"Tile at position ({x}, {y}) is outside the grid bounds of {width}x{height}");
                }
            }
            
            // Now that grid is initialized, assign neighbors
            AssignNeighbors();
            
            // Notify LevelManager about existing start and end tiles
            if (LevelManager.Instance != null && startTile != null && endTile != null)
            {
                LevelManager.Instance.SetStartAndEndTiles(startTile, endTile);
                Debug.Log($"Set start tile {startTile.name} and end tile {endTile.name} on LevelManager");
            }
            else
            {
                Debug.LogWarning($"Could not set tiles on LevelManager. LevelManager: {(LevelManager.Instance != null ? "Found" : "Not found")}, " +
                                $"Start tile: {(startTile != null ? "Found" : "Not found")}, " +
                                $"End tile: {(endTile != null ? "Found" : "Not found")}");
            }
            
            // Notify that level is ready
            if (EventManager.LevelGenerated != null)
            {
                EventManager.LevelGenerated.Invoke();
            }
        }
        else
        {
            // No level exists, create one at runtime
            Debug.Log("No level found, creating a new one at runtime");
            CreateLevel();
        }

        EventManager.LevelGenerated.Invoke();
    }
    
    // Class to hold tile planning information
    private class TilePlan
    {
        public Vector2Int position;
        public GameObject prefab;
        public List<int> directions;
        public bool isOnCriticalPath;
        public bool isKeyTile;

        public TilePlan(Vector2Int pos, GameObject prefab, List<int> dirs)
        {
            this.position = pos;
            this.prefab = prefab;
            this.directions = new List<int>(dirs);
            this.isOnCriticalPath = false;
            this.isKeyTile = false;
        }
    }

    // This method can be called from editor or runtime
    public void CreateLevel()
    {
        // Zorluk kontrolü
        difficultyRating = Mathf.Clamp(difficultyRating, 1, 10);
        Debug.Log($"Zorluk seviyesi: {difficultyRating}");
        
        // Zorluk parametrelerini ayarla
        SetDifficultyParameters();

        // Prefab kontrolü
        if (startPrefab == null || endPrefab == null)
        {
            Debug.LogError("Start veya End prefab atanmamış!");
            return;
        }

        // Grid boyut kontrolü
        if (width < 4 || height < 4)
        {
            Debug.LogError($"Grid boyutları çok küçük! Minimum 4x4 olmalı. Şu anki: {width}x{height}");
            return;
        }

        // Plan the level first
        List<TilePlan> levelPlan = PlanLevel();
        
        if (levelPlan == null || levelPlan.Count == 0)
        {
            Debug.LogError("Level planlaması başarısız oldu!");
            return;
        }

        // Now instantiate the planned level
        InstantiatePlannedLevel(levelPlan);
        
        // Notify level was generated if successful
        if (Application.isPlaying && EventManager.LevelGenerated != null)
        {
            EventManager.LevelGenerated.Invoke();
        }
    }
    
    // Plan the entire level without instantiating anything
    private List<TilePlan> PlanLevel()
    {
        try
        {
            Debug.Log("Starting level planning...");
            grid = new Tile[height, width];
            tiles.Clear();

            // Use GenerateStartEndPositions for start and end
            var positions = GenerateStartEndPositions();
            startPos = positions[0];
            endPos = positions[1];

            // Ensure positions are valid
            if (!InBounds(startPos) || !InBounds(endPos))
            {
                Debug.LogError("Invalid start or end position!");
                return null;
            }

            Debug.Log($"Start position: {startPos}, End position: {endPos}");
            
            // Create the plan list and add start/end tiles
            List<TilePlan> plan = new List<TilePlan>();
            
            // Add start and end tiles to the plan
            plan.Add(new TilePlan(startPos, startPrefab, new List<int>()));
            plan.Add(new TilePlan(endPos, endPrefab, new List<int>()));
            
            // Create a temporary grid for path planning
            Dictionary<Vector2Int, List<int>> planningGrid = new Dictionary<Vector2Int, List<int>>();
            planningGrid[startPos] = new List<int>();
            planningGrid[endPos] = new List<int>();

            // Build the main path
            var mainPath = BuildPath(startPos, endPos);
            if (mainPath == null || mainPath.Count < 2)
            {
                Debug.LogError("Failed to create main path!");
                return null;
            }
            
            // Store the main path as the critical path
            criticalPath = new List<Vector2Int>(mainPath);
            
            // Identify key tile positions - these are critical junctions
            keyTilePositions = new List<Vector2Int>();
            IdentifyKeyTiles(mainPath, keyTilePositions);
            
            // Create deceptive and dead-end paths based on difficulty
            var allPaths = new List<List<Vector2Int>> { mainPath };
            GenerateDeceptivePaths(allPaths, mainPath);

            // Plan all paths
            PlanPaths(allPaths, planningGrid);

            // Plan dead ends at higher difficulties
            if (difficultyRating > 3)
            {
                PlanDeadEnds(planningGrid);
            }

            // Create gaps in the critical path for key tiles (at higher difficulties)
            if (difficultyRating > 3 && keyTilePositions.Count > 0)
            {
                CreateGapsForKeyTiles(planningGrid, keyTilePositions);
            }

            // Plan remaining tiles
            PlanRemainingTiles(planningGrid);
            
            // Validate the level difficulty
            bool isValidDifficulty = ValidateLevelDifficulty(planningGrid);
            int attempts = 0;
            
            // If the level is too easy or too hard, try to adjust it
            while (!isValidDifficulty && attempts < 3)
            {
                attempts++;
                Debug.Log($"Level difficulty validation failed. Attempt {attempts} to adjust...");
                
                // Adjust planning grid based on validation results
                AdjustLevelDifficulty(planningGrid);
                
                // Check if the adjusted level is valid
                isValidDifficulty = ValidateLevelDifficulty(planningGrid);
            }
            
            // Convert planning grid to tile plans
            foreach (var entry in planningGrid)
            {
                Vector2Int pos = entry.Key;
                List<int> dirs = entry.Value;
                
                // Skip start and end positions - they're already in the plan
                if (pos == startPos || pos == endPos)
                    continue;
                
                // Choose appropriate prefab based on directions
                GameObject prefab = ChoosePrefab(dirs);
                
                // Mark if this is a critical or key tile
                bool isCriticalTile = criticalPath.Contains(pos);
                bool isKeyTile = keyTilePositions.Contains(pos);
                
                // Add to plan
                var tilePlan = new TilePlan(pos, prefab, dirs);
                tilePlan.isOnCriticalPath = isCriticalTile;
                tilePlan.isKeyTile = isKeyTile;
                plan.Add(tilePlan);
            }
            
            Debug.Log($"Level planning completed. {plan.Count} tiles planned.");
            return plan;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Level planning error: {e.Message}\n{e.StackTrace}");
            return null;
        }
    }
    
    private void IdentifyKeyTiles(List<Vector2Int> mainPath, List<Vector2Int> keyTiles)
    {
        int keyTileCount = Mathf.RoundToInt(Mathf.Lerp(2, 8, (difficultyRating - 1) / 9f));
        // For very high difficulty, ensure more key tiles if the path is long enough
        if (difficultyRating >= 9 && mainPath.Count > 15) {
            keyTileCount = Mathf.Max(keyTileCount, Mathf.RoundToInt(Mathf.Lerp(5, 10, (difficultyRating - 8) / 2f))); // Scale from 5 up to 10 for diff 9-10
        }
        keyTileCount = Mathf.Min(keyTileCount, mainPath.Count - 2); // Cannot have more key tiles than path segments minus start/end
        if (keyTileCount < 0) keyTileCount = 0;

        Debug.Log($"Planning to create {keyTileCount} key tiles for difficulty {difficultyRating}");

        if (mainPath.Count <= 2 || keyTileCount == 0) // Added keyTileCount == 0 check
            return;

        HashSet<int> keyIndices = new HashSet<int>();
        int maxAttempts = keyTileCount * 5;

        while (keyIndices.Count < keyTileCount && maxAttempts > 0)
        {
            int index = Random.Range(2, mainPath.Count - 2);
            bool tooClose = keyIndices.Any(existing => Mathf.Abs(existing - index) <= 2);
            if (!tooClose)
                keyIndices.Add(index);
            maxAttempts--;
        }

        foreach (int index in keyIndices)
        {
            keyTiles.Add(mainPath[index]);
            Debug.Log($"Added key tile at position {mainPath[index]} (path index: {index})");
        }
    }

    // Create gaps in the critical path for key tiles
    private void CreateGapsForKeyTiles(Dictionary<Vector2Int, List<int>> planningGrid, List<Vector2Int> keyTiles)
    {
        // Only applicable at higher difficulties and if there are key tiles
        if (difficultyRating < 4 || keyTiles == null || keyTiles.Count == 0)
            return;
            
        int maxRemovedTiles = 0;
        if (difficultyRating == 10) maxRemovedTiles = Mathf.Min(keyTiles.Count > 0 ? keyTiles.Count - 1 : 0, Mathf.CeilToInt(keyTiles.Count * 0.8f));
        else if (difficultyRating == 9) maxRemovedTiles = Mathf.Min(keyTiles.Count > 0 ? keyTiles.Count - 1 : 0, Mathf.CeilToInt(keyTiles.Count * 0.6f));
        else if (difficultyRating >= 8) maxRemovedTiles = Mathf.Min(keyTiles.Count > 0 ? keyTiles.Count - 1 : 0, Mathf.CeilToInt(keyTiles.Count * 0.5f));
        else if (difficultyRating >= 4) maxRemovedTiles = Mathf.Min(keyTiles.Count > 0 ? keyTiles.Count - 1 : 0, Mathf.CeilToInt(keyTiles.Count * 0.3f));
        if (maxRemovedTiles < 0) maxRemovedTiles = 0;

        int actualRemovedCount = 0; // Running count of tiles whose connections are completely emptied
            
        // For each key tile, modify its connections to create a gap in the path
        foreach (var pos in keyTiles)
        {
            if (planningGrid.ContainsKey(pos))
            {
                float attemptEmptyChance = 0.0f;
                float attemptReduceChance = 0.0f;

                if (difficultyRating == 10) {
                    attemptEmptyChance = 0.9f; 
                    attemptReduceChance = 1.0f; 
                } else if (difficultyRating == 9) {
                    attemptEmptyChance = 0.75f;
                    attemptReduceChance = 0.9f;
                } else if (difficultyRating >= 8) { // difficultyRating == 8
                    attemptEmptyChance = 0.6f;
                    attemptReduceChance = 0.8f;
                } else if (difficultyRating >= 4) { // difficultyRating 4-7
                    attemptEmptyChance = 0.4f; 
                    attemptReduceChance = 0.7f;
                }

                bool emptiedThisTile = false;
                // Try to empty the tile's connections
                if (actualRemovedCount < maxRemovedTiles && Random.value < attemptEmptyChance && planningGrid[pos].Count > 0) // Also check if it has connections to remove
                {
                    planningGrid[pos] = new List<int>(); // Empty it
                    Debug.Log($"Emptied key tile at {pos} for difficulty {difficultyRating}. (Removed: {actualRemovedCount + 1}/{maxRemovedTiles})");
                    emptiedThisTile = true;
                    actualRemovedCount++;
                }

                // If not emptied, and it has more than 1 connection, try to reduce its connections
                if (!emptiedThisTile && planningGrid[pos].Count > 1 && Random.value < attemptReduceChance)
                {
                    // Only reduce if it currently has more than 2 connections, aiming to make it a 2-connection (straight/corner)
                    if (planningGrid[pos].Count > 2) 
                    {
                        List<int> originalDirs = new List<int>(planningGrid[pos]); // These are the connections vital for the current critical path plan
                    planningGrid[pos].Clear();
                    
                        if (difficultyRating >= 9 && originalDirs.Count >= 2) // Special handling for Diff 9 & 10 to force specific straights or corners
                        {
                            bool madeStraight = false;
                            // Try to force a specific straight connection if possible from originalDirs
                            for (int j = 0; j < originalDirs.Count; j++) {
                                for (int k = j + 1; k < originalDirs.Count; k++) {
                                    if (Mathf.Abs(originalDirs[j] - originalDirs[k]) == 2) { // Opposite directions
                                        planningGrid[pos].Add(originalDirs[j]);
                                        planningGrid[pos].Add(originalDirs[k]);
                                        Debug.Log($"FORCED key tile {pos} to a specific STRAIGHT with dirs {originalDirs[j]},{originalDirs[k]} (was {originalDirs.Count}) for difficulty {difficultyRating}");
                                        madeStraight = true;
                            break;
                        }
                                }
                                if (madeStraight) break;
                            }

                            if (!madeStraight) { // Could not force a straight, so force a specific corner
                                if (originalDirs.Count >= 2) {
                                    // Pick the first two (potentially shuffled) non-opposite original connections to form a corner
                                    List<int> shuffledOriginalDirs = originalDirs.OrderBy(a => Random.value).ToList();
                                    int dir1 = -1, dir2 = -1;
                                    for(int j=0; j < shuffledOriginalDirs.Count; ++j) {
                                        for (int k=j+1; k < shuffledOriginalDirs.Count; ++k) {
                                            if (Mathf.Abs(shuffledOriginalDirs[j] - shuffledOriginalDirs[k]) != 2) {
                                                dir1 = shuffledOriginalDirs[j];
                                                dir2 = shuffledOriginalDirs[k];
                                                break;
                                            }
                                        }
                                        if (dir1 != -1) break;
                                    }
                                    if (dir1 != -1 && dir2 != -1) {
                                        planningGrid[pos].Add(dir1);
                                        planningGrid[pos].Add(dir2);
                                        Debug.Log($"FORCED key tile {pos} to a specific CORNER with dirs {dir1},{dir2} (was {originalDirs.Count}) for difficulty {difficultyRating}");
                                    } else if (originalDirs.Count >= 1) { // Fallback if somehow still no pair, just take the first (should be rare)
                                         planningGrid[pos].Add(originalDirs[0]); // This will likely become a 1-connection dead-end piece
                                         if(originalDirs.Count >=2) planningGrid[pos].Add(originalDirs[1]); // take two if available.
                                         Debug.Log($"Reduced key tile {pos} (fallback) to dirs {string.Join(",", planningGrid[pos])} (was {originalDirs.Count}) for difficulty {difficultyRating}");
                                    }
                                } else if (originalDirs.Count == 1) { // If original was only 1 connection, keep it
                                     planningGrid[pos].Add(originalDirs[0]);
                                     Debug.Log($"Kept key tile {pos} as ONE dir {originalDirs[0]} (was {originalDirs.Count}) for difficulty {difficultyRating}");
                                }
                            }
                        } 
                        else // Logic for difficulties 4-8 (less strict reduction)
                        { 
                            List<int> shuffledOriginalDirs = originalDirs.OrderBy(a => Random.value).ToList();
                            int dir1 = -1, dir2 = -1;

                            // Try to find a pair that forms a corner from the original connections
                            foreach (int firstChoice in shuffledOriginalDirs) {
                                foreach (int secondChoice in originalDirs) { 
                                    if (firstChoice == secondChoice) continue;
                                    if (Mathf.Abs(firstChoice - secondChoice) != 2) { 
                                        dir1 = firstChoice;
                                        dir2 = secondChoice;
                                break;
                            }
                        }
                                if (dir1 != -1) break; 
                            }

                            if (dir1 != -1 && dir2 != -1) { 
                                planningGrid[pos].Add(dir1);
                                planningGrid[pos].Add(dir2);
                                Debug.Log($"Reduced key tile {pos} to a CORNER with dirs {dir1},{dir2} (was {originalDirs.Count}) for difficulty {difficultyRating}");
                            } else if (originalDirs.Count >= 2) { 
                                planningGrid[pos].Add(originalDirs[0]);
                                planningGrid[pos].Add(originalDirs[1]);
                                Debug.Log($"Reduced key tile {pos} to a STRAIGHT with dirs {originalDirs[0]},{originalDirs[1]} (was {originalDirs.Count}) for difficulty {difficultyRating}");
                            } else if (originalDirs.Count == 1) { 
                                 planningGrid[pos].Add(originalDirs[0]);
                                 Debug.Log($"Reduced key tile {pos} to ONE dir {originalDirs[0]} (was {originalDirs.Count}) for difficulty {difficultyRating}");
                            }
                        }
                        // Ensure we don't have more than 2 connections if reduction happened and wasn't a single kept dir.
                        if(planningGrid[pos].Count > 2) { // If forced logic resulted in >2 (should not happen with current code)
                            Debug.LogWarning($"Key tile {pos} reduction resulted in >2 connections. Trimming to 2.");
                            while(planningGrid[pos].Count > 2) planningGrid[pos].RemoveAt(planningGrid[pos].Count -1);
                        }
                        if(planningGrid[pos].Count == 0 && originalDirs.Count > 0) { // Safety: if it became empty but wasn't, restore one dir
                            Debug.LogWarning($"Key tile {pos} reduction resulted in 0 connections. Restoring first original dir.");
                            planningGrid[pos].Add(originalDirs[0]);
                        }
                    }
                }
            }
        }
    }
    
    // Validate if the level meets the difficulty requirements
    private bool ValidateLevelDifficulty(Dictionary<Vector2Int, List<int>> planningGrid)
    {
        // Count valid paths
        int validPathCount = CountValidPaths(startPos, endPos, planningGrid);

        // Calculate critical path metrics
        int criticalPathLength = criticalPath.Count;
        int minPathLength = Mathf.RoundToInt(Mathf.Lerp(width + height, (width + height) * 2, (difficultyRating - 1) / 9f));
        int turns = CalculatePathTurns(criticalPath);

        // Minimum turns scales with difficulty
        int minTurns = Mathf.RoundToInt(Mathf.Lerp(3, 15, (difficultyRating - 1) / 9f));

        Debug.Log($"Level validation: Paths: {validPathCount} (target: {minValidPathsRequired}-{maxValidPathsAllowed}), " +
                $"Length: {criticalPathLength} (min: {minPathLength}), Turns: {turns} (min: {minTurns})");

        return validPathCount >= minValidPathsRequired &&
            validPathCount <= maxValidPathsAllowed &&
            criticalPathLength >= minPathLength &&
            turns >= minTurns;
    }
    private int CalculatePathTurns(List<Vector2Int> path)
    {
        if (path.Count < 3)
            return 0;

        int turns = 0;
        for (int i = 1; i < path.Count - 1; i++)
        {
            Vector2Int prevDir = path[i] - path[i - 1];
            Vector2Int nextDir = path[i + 1] - path[i];
            if (prevDir != nextDir && prevDir != -nextDir)
                turns++;
        }
        return turns;
    }
    // Count possible valid paths from start to end
    private int CountValidPaths(Vector2Int start, Vector2Int end, Dictionary<Vector2Int, List<int>> planningGrid)
    {
        // This is a simplified estimation of path count, not an exact calculation
        // A real implementation would use a more sophisticated algorithm
        
        // Basic breadth-first search to count distinct paths 
        // Limited to maxValidPathsAllowed+1 to avoid excessive computation
        
        int pathCount = 0;
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        Dictionary<Vector2Int, int> pathsToNode = new Dictionary<Vector2Int, int>();
        
        // Start with the start position
        queue.Enqueue(start);
        visited.Add(start);
        pathsToNode[start] = 1; // 1 path to the start
        
        while (queue.Count > 0 && pathCount <= maxValidPathsAllowed + 1)
        {
            Vector2Int current = queue.Dequeue();
            
            // Found end, add the number of paths to this node to total
            if (current == end)
            {
                pathCount += pathsToNode[current];
                continue;
            }
            
            // Skip if this node has no connections in the planning grid
            if (!planningGrid.ContainsKey(current))
                continue;
                
            // Get the connections from this node
            List<int> directions = planningGrid[current];
            
            // Check each direction
            foreach (int dir in directions)
            {
                Vector2Int neighbor = GetNeighborPosition(current, dir);
                
                // Skip if out of bounds
                if (!InBounds(neighbor)) 
                    continue;
                    
                // Skip if the neighbor has no connections or doesn't connect back
                if (!planningGrid.ContainsKey(neighbor))
                    continue;
                    
                // Check if the neighbor connects back to this node
                int oppositeDir = GetOppositeDirection(dir);
                if (!planningGrid[neighbor].Contains(oppositeDir))
                    continue;
                
                // If not visited, add to queue
                if (!visited.Contains(neighbor))
                {
                    visited.Add(neighbor);
                    queue.Enqueue(neighbor);
                    pathsToNode[neighbor] = pathsToNode[current];
                }
                // If already visited, add to its path count
                else if (neighbor != end)
                {
                    pathsToNode[neighbor] += pathsToNode[current];
                }
            }
        }
        
        return Mathf.Min(pathCount, maxValidPathsAllowed + 1);
    }
    
    // Adjust the level difficulty if validation fails
    private void AdjustLevelDifficulty(Dictionary<Vector2Int, List<int>> planningGrid)
    {
        int validPathCount = CountValidPaths(startPos, endPos, planningGrid);
        int criticalPathLength = criticalPath.Count;
        int minPathLength = Mathf.RoundToInt(Mathf.Lerp(width + height, (width + height) * 2, (difficultyRating - 1) / 9f));

        if (validPathCount < minValidPathsRequired || criticalPathLength < minPathLength)
        {
            Debug.Log("Too few paths or short critical path. Adding connections...");
            // Extend critical path by adding detours
            int insertIndex = Random.Range(1, criticalPath.Count - 1);
            Vector2Int detour = new Vector2Int(
                Mathf.Clamp(criticalPath[insertIndex].x + Random.Range(-2, 3), 0, width - 1),
                Mathf.Clamp(criticalPath[insertIndex].y + Random.Range(-2, 3), 0, height - 1)
            );
            if (InBounds(detour) && !criticalPath.Contains(detour))
            {
                criticalPath.Insert(insertIndex, detour);
                planningGrid[detour] = new List<int> { 1, 3 }; // Example: straight tile
            }
        }
        else if (validPathCount > maxValidPathsAllowed)
        {
            Debug.Log("Too many valid paths. Removing connections...");
            var nonCriticalTiles = planningGrid.Keys
                .Where(p => !criticalPath.Contains(p) && p != startPos && p != endPos)
                .ToList();
            foreach (var pos in nonCriticalTiles.Take(5))
            {
                if (planningGrid[pos].Count > 2)
                {
                    int dirIndex = Random.Range(0, planningGrid[pos].Count);
                    int dir = planningGrid[pos][dirIndex];
                    planningGrid[pos].RemoveAt(dirIndex);
                    Vector2Int neighbor = GetNeighborPosition(pos, dir);
                    if (planningGrid.ContainsKey(neighbor))
                    {
                        planningGrid[neighbor].Remove(GetOppositeDirection(dir));
                    }
                }
            }
        }
    }
        
    private void SetDifficultyParameters()
    {
        // Scale difficulty parameters based on difficulty rating (1-10 scale)
        deadEndProbability = Mathf.Lerp(0.05f, 0.7f, (difficultyRating - 1) / 9f);          // Increase dead end probability
        misleadingPathProbability = Mathf.Lerp(0.1f, 0.8f, (difficultyRating - 1) / 9f);    // Increase deceptive path probability
        maxPathLength = Mathf.RoundToInt(Mathf.Lerp(width + height, (width + height) * 2.5f, (difficultyRating - 1) / 9f));
        
        if (difficultyRating == 10) {
            pathStraightnessBias = 0.20f;
        } else if (difficultyRating == 9) {
            pathStraightnessBias = 0.30f;
        } else {
            pathStraightnessBias = Mathf.Lerp(0.95f, 0.40f, (difficultyRating - 1) / 9f); 
        }
        
        // New parameters for improved difficulty scaling
        int criticalTileCount = Mathf.RoundToInt(Mathf.Lerp(1, 5, (difficultyRating - 1) / 9f));
        
        // Set valid path limits based on difficulty
        minValidPathsRequired = 1; // Always at least one solution
        maxValidPathsAllowed = Mathf.RoundToInt(Mathf.Lerp(10, 1, (difficultyRating - 1) / 9f));
        
        // Debug output
        Debug.Log($"Difficulty parameters - Dead ends: {deadEndProbability:F2}, " +
                  $"Deceptive paths: {misleadingPathProbability:F2}, " +
                  $"Max path length: {maxPathLength}, " +
                  $"Path Straightness Bias: {pathStraightnessBias:F2}, " + // New
                  $"Critical tiles: {criticalTileCount}, " +
                  $"Max valid paths: {maxValidPathsAllowed}");
    }

    private bool CreateStartAndEndTiles()
    {
        try
        {
            // Get or create the container
            container = GetOrCreateContainer();
            
            grid = new Tile[height, width];
            
            // Clear the tiles list
            tiles.Clear();

            // Start pozisyonunu belirle
            startPos = new Vector2Int(
                Mathf.Clamp(width / 4, 1, width - 2),
                Mathf.Clamp(1, 1, height / 3)
            );

            // End pozisyonunu belirle
            endPos = new Vector2Int(
                Mathf.Clamp((3 * width) / 4, 2, width - 2),
                Mathf.Clamp(height - 2, height * 2/3, height - 2)
            );

            Debug.Log($"Start pozisyonu: {startPos}, End pozisyonu: {endPos}");

            // Start Tile'ı oluştur
            GameObject startGo;
#if UNITY_EDITOR
            startGo = UnityEditor.PrefabUtility.InstantiatePrefab(startPrefab, container) as GameObject;
            startGo.transform.position = new Vector3(startPos.x * spacing, 0, startPos.y * spacing);
            startGo.transform.rotation = Quaternion.identity;
#else
            startGo = Instantiate(startPrefab,
                new Vector3(startPos.x * spacing, 0, startPos.y * spacing),
                Quaternion.identity,
                container);
#endif

            startGo.name = "StartTile";
            startTile = startGo.GetComponent<Tile>();
            if (startTile == null)
            {
                throw new System.Exception("Start tile'da Tile componenti bulunamadı!");
            }
            
            // Start tile'ı grid'e ekle
            grid[startPos.y, startPos.x] = startTile;
            tiles.Add(startTile);

            // End Tile'ı oluştur
            GameObject endGo;
#if UNITY_EDITOR
            endGo = UnityEditor.PrefabUtility.InstantiatePrefab(endPrefab, container) as GameObject;
            endGo.transform.position = new Vector3(endPos.x * spacing, 0, endPos.y * spacing);
            endGo.transform.rotation = Quaternion.identity;
#else
            endGo = Instantiate(endPrefab,
                new Vector3(endPos.x * spacing, 0, endPos.y * spacing),
                Quaternion.identity,
                container);
#endif

            endGo.name = "EndTile";
            endTile = endGo.GetComponent<Tile>();
            if (endTile == null)
            {
                throw new System.Exception("End tile'da Tile componenti bulunamadı!");
            }
            
            // End tile'ı grid'e ekle
            grid[endPos.y, endPos.x] = endTile;
            tiles.Add(endTile);

            // LevelManager'a bildir (edit modunda da çalışacak şekilde)
            var levelManager = FindObjectOfType<LevelManager>();
            if (levelManager != null)
            {
                levelManager.SetStartAndEndTiles(startTile, endTile);
            }
            else if (Application.isPlaying && LevelManager.Instance != null)
            {
                LevelManager.Instance.SetStartAndEndTiles(startTile, endTile);
            }

            Debug.Log("Start ve End tile'lar başarıyla oluşturuldu");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Start/End tile oluşturma hatası: {e.Message}");
            return false;
        }
    }

    // Helper method to safely destroy objects in both edit and play mode
    private void SafeDestroy(Object obj)
    {
        if (obj == null)
            return;
            
        if (Application.isPlaying)
        {
            Destroy(obj);
        }
        else
        {
            DestroyImmediate(obj);
        }
    }

    private void ResetLevelKeepStartEnd()
    {
        // Grid'i temizle ama Start ve End tile'ları koru
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var pos = new Vector2Int(x, y);
                if (pos != startPos && pos != endPos && grid[y, x] != null)
                {
                    SafeDestroy(grid[y, x].gameObject);
                    grid[y, x] = null;
                }
            }
        }
    }

private bool GenerateSimpleLevel()
{
    try
    {
        // Clear state
        if (container != null)
        {
            DestroyImmediate(container.gameObject);
        }
        container = GetOrCreateContainer();
        grid = new Tile[height, width];
        tiles.Clear();
        criticalPath = null;
        keyTilePositions = new List<Vector2Int>();

        // Simple L-shaped path
        startPos = new Vector2Int(1, 1);
        endPos = new Vector2Int(width - 2, height - 2);
        criticalPath = new List<Vector2Int> { startPos };

        Vector2Int current = startPos;
        // Move right
        while (current.x < endPos.x)
        {
            current = new Vector2Int(current.x + 1, current.y);
            criticalPath.Add(current);
        }
        // Move up
        while (current.y < endPos.y)
        {
            current = new Vector2Int(current.x, current.y + 1);
            criticalPath.Add(current);
        }

        // Place critical path tiles
        for (int i = 0; i < criticalPath.Count; i++)
        {
            var pos = criticalPath[i];
            GameObject prefab;
            List<int> directions = new List<int>();

            if (pos == startPos)
            {
                prefab = startPrefab;
                directions = new List<int> { criticalPath[1].x > pos.x ? 2 : 1 }; // East or North
            }
            else if (pos == endPos)
            {
                prefab = endPrefab;
                directions = new List<int> { criticalPath[i - 1].x < pos.x ? 4 : 3 }; // West or South
            }
            else
            {
                bool isCorner = i < criticalPath.Count - 1 && criticalPath[i].x == criticalPath[i + 1].x;
                prefab = isCorner ? cornerPrefab : straightPrefab;
                if (isCorner)
                {
                    directions = new List<int> { GetDirection(criticalPath[i - 1], pos), GetDirection(pos, criticalPath[i + 1]) };
                }
                else
                {
                    directions = criticalPath[i].x == criticalPath[i - 1].x ? new List<int> { 1, 3 } : new List<int> { 2, 4 };
                }
            }

            PlaceOrientedTile(pos, prefab, directions, false, true);
        }

        // Fill remaining tiles with empty tiles
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var pos = new Vector2Int(x, y);
                if (grid[y, x] != null)
                    continue;
                PlaceTile(pos, emptyPrefab, new List<int>());
            }
        }

        AssignNeighbors();
        if (ValidateLevelSolvability())
        {
            if (LevelManager.Instance != null)
            {
                LevelManager.Instance.SetStartAndEndTiles(grid[startPos.y, startPos.x], grid[endPos.y, endPos.x]);
            }
            EventManager.LevelGenerated?.Invoke();
            Debug.Log("Simple level generated successfully");
            return true;
        }
        else
        {
            Debug.LogError("Simple level is unsolvable");
            return false;
        }
    }
    catch (System.Exception e)
    {
        Debug.LogError($"Simple level generation error: {e.Message}\n{e.StackTrace}");
        return false;
    }
}
private bool GenerateTiles()
{
    int maxAttempts = 5; // Increased attempts for robustness
    int attempt = 0;

    while (attempt < maxAttempts)
    {
        try
        {
            Debug.Log($"Generating level, attempt {attempt + 1}");
            // Clear previous state
            if (container != null)
            {
                DestroyImmediate(container.gameObject);
            }
            container = GetOrCreateContainer();
            grid = new Tile[height, width];
            tiles.Clear();
            criticalPath = null;
            keyTilePositions = new List<Vector2Int>();

            // Set start and end positions
            startPos = new Vector2Int(width / 4, 1);
            endPos = new Vector2Int((3 * width) / 4, height - 2);

            if (!InBounds(startPos) || !InBounds(endPos))
            {
                Debug.LogError("Invalid start or end position!");
                return false;
            }

            if (Vector2Int.Distance(startPos, endPos) < 5) // Increased minimum distance
            {
                Debug.LogWarning("Start and end too close, adjusting...");
                endPos = new Vector2Int((3 * width) / 4, height - 2);
            }

            // Build critical path
            var mainPath = BuildPath(startPos, endPos);
            if (mainPath == null || mainPath.Count < 2)
            {
                Debug.LogError("Failed to build main path");
                attempt++;
                continue;
            }

            criticalPath = new List<Vector2Int>(mainPath);
            var allPaths = new List<List<Vector2Int>> { mainPath };
            GenerateDeceptivePaths(allPaths, mainPath);

            if (difficultyRating > 3)
            {
                IdentifyKeyTiles(mainPath, keyTilePositions);
            }

            PlacePaths(allPaths);
            if (difficultyRating > 3)
                AddDeadEnds();
            FillRemainingTiles();
            AssignNeighbors();

            // Validate solvability
            if (ValidateLevelSolvability())
            {
                if (LevelManager.Instance != null)
                {
                    LevelManager.Instance.SetStartAndEndTiles(grid[startPos.y, startPos.x], grid[endPos.y, endPos.x]);
                }
                EventManager.LevelGenerated?.Invoke();
                Debug.Log($"Level generated successfully on attempt {attempt + 1}");
                return true;
            }
            else
            {
                Debug.LogWarning($"Level unsolvable on attempt {attempt + 1}, retrying...");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Level generation error: {e.Message}\n{e.StackTrace}");
            attempt++;
            continue;
        }

        attempt++;
    }

    // Fallback: Generate a simple solvable level
    Debug.LogWarning("Max attempts reached, generating simple level");
    return GenerateSimpleLevel();
}  

private int GetDirection(Vector2Int from, Vector2Int to)
{
    if (to.y > from.y) return 1; // North
    if (to.x > from.x) return 2; // East
    if (to.y < from.y) return 3; // South
    if (to.x < from.x) return 4; // West
    Debug.LogWarning($"No valid direction from {from} to {to}, defaulting to North");
    return 1; // Default fallback
}
    
    private void GenerateDeceptivePaths(List<List<Vector2Int>> allPaths, List<Vector2Int> mainPath)
    {
        // int deceptivePathCount = Mathf.RoundToInt(Mathf.Lerp(2, 10, (difficultyRating - 1) / 9f)); // Old
        int deceptivePathCount = Mathf.RoundToInt(Mathf.Lerp(2, 15, (difficultyRating - 1) / 9f)); // New: up to 15
        deceptivePathCount = Mathf.Min(deceptivePathCount, mainPath.Count > 1 ? mainPath.Count - 2 : 0); // Ensure it's not negative or too large for short paths
        if (deceptivePathCount < 0) deceptivePathCount = 0;

        Debug.Log($"Creating {deceptivePathCount} deceptive paths for difficulty {difficultyRating}");

        HashSet<int> usedIndices = new HashSet<int>();
        for (int i = 0; i < deceptivePathCount; i++)
        {
            int startIndex;
            do
            {
                startIndex = Random.Range(1, mainPath.Count - 1);
            } while (usedIndices.Contains(startIndex));

            usedIndices.Add(startIndex);
            Vector2Int branchStart = mainPath[startIndex];

            // Create a fake target near the end for higher difficulties
            Vector2Int fakeTarget;
            if (difficultyRating > 5 && Random.value < 0.7f)
            {
                fakeTarget = new Vector2Int(
                    Mathf.Clamp(endPos.x + Random.Range(-2, 3), 1, width - 2),
                    Mathf.Clamp(endPos.y + Random.Range(-2, 3), 1, height - 2)
                );
                if (fakeTarget == endPos || Vector2Int.Distance(fakeTarget, endPos) < 2)
                {
                    fakeTarget = new Vector2Int(
                        Mathf.Clamp(endPos.x + (Random.value < 0.5f ? -3 : 3), 1, width - 2),
                        Mathf.Clamp(endPos.y + (Random.value < 0.5f ? -3 : 3), 1, height - 2)
                    );
                }
            }
            else
            {
                fakeTarget = GetRandomFakeTarget(branchStart, mainPath);
            }

            // Build a longer deceptive path
            List<Vector2Int> fakePath = BuildDeceptivePath(branchStart, fakeTarget);
            if (fakePath.Count > 4)
            {
                allPaths.Add(fakePath);

                // Add sub-branches for higher difficulties
                if (difficultyRating > 6 && Random.value < misleadingPathProbability)
                {
                    int subBranchCount = Mathf.RoundToInt(Mathf.Lerp(1, 4, (difficultyRating - 1) / 9f));
                    for (int s = 0; s < subBranchCount && fakePath.Count > 4; s++)
                    {
                        int subStartIndex = Random.Range(1, fakePath.Count - 2);
                        Vector2Int subBranchStart = fakePath[subStartIndex];
                        Vector2Int subFakeTarget = GetRandomFakeTarget(subBranchStart, fakePath);
                        List<Vector2Int> subFakePath = BuildDeceptivePath(subBranchStart, subFakeTarget);
                        if (subFakePath.Count > 3)
                            allPaths.Add(subFakePath);
                    }
                }
            }
        }

        Debug.Log($"Total paths created: {allPaths.Count}");
    }
    private Vector2Int GetRandomFakeTarget(Vector2Int source, List<Vector2Int> avoidPoints)
    {
        // Mevcut yoldan belirli bir uzaklıkta rastgele bir hedef belirle
        int minDistance = Mathf.RoundToInt(Mathf.Lerp(3, 5, (difficultyRating - 1) / 9f));
        int attempts = 0;
        Vector2Int target;
        
        do {
            // Zorluk arttıkça daha uzak noktalar tercih et
            int maxDist = Mathf.RoundToInt(Mathf.Lerp(width/3, width/2, (difficultyRating - 1) / 9f));
            int xOffset = Random.Range(-maxDist, maxDist + 1);
            int yOffset = Random.Range(-maxDist, maxDist + 1);
            
            target = new Vector2Int(
                Mathf.Clamp(source.x + xOffset, 1, width - 2),
                Mathf.Clamp(source.y + yOffset, 1, height - 2)
            );
            
            attempts++;
        } while (avoidPoints.Contains(target) && Vector2Int.Distance(source, target) < minDistance && attempts < 20);
        
        return target;
    }
    
    private List<Vector2Int> BuildDeceptivePath(Vector2Int start, Vector2Int target)
    {
        float windingFactor = Mathf.Lerp(0.3f, 0.95f, (difficultyRating - 1) / 9f); // This can still be used for other heuristics if needed
        int minLength = Mathf.RoundToInt(Mathf.Lerp(5, 15, (difficultyRating - 1) / 9f));
        int maxIterations = maxPathLength;

        List<Vector2Int> path = new List<Vector2Int> { start };
        Vector2Int current = start;
        int iteration = 0;

        while (iteration < maxIterations)
        {
            // bool directPath = Random.value > windingFactor && path.Count > minLength; // Old logic
            bool directPath = Random.value < pathStraightnessBias; // New logic, consistent with BuildPath
            Vector2Int next;

            if (directPath)
            {
                Vector2Int dir = new Vector2Int(
                    Mathf.Clamp(target.x - current.x, -1, 1),
                    Mathf.Clamp(target.y - current.y, -1, 1)
                );
                if (dir.x != 0 && dir.y != 0)
                    dir.y = Random.value < 0.5f ? 0 : dir.y;
                next = current + dir;
            }
            else
            {
                Vector2Int[] directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
                // Bias toward directions that mimic the critical path
                if (difficultyRating > 7)
                {
                    directions = directions.OrderBy(d =>
                    {
                        Vector2Int nextPos = current + d;
                        float distToEnd = Vector2Int.Distance(nextPos, endPos);
                        return distToEnd + Random.value * windingFactor;
                    }).ToArray();
                }
                next = current + directions[Random.Range(0, directions.Length)];
            }

            if (InBounds(next) && !path.Contains(next))
            {
                path.Add(next);
                current = next;

                // Stop near the target but don't connect
                if (Vector2Int.Distance(current, target) <= 2 && Random.value < 0.8f && difficultyRating > 5)
                {
                    Debug.Log($"Deceptive path stopped near target at {current}");
                    break;
                }
            }
            else if (path.Count > minLength && Random.value < 0.3f)
            {
                break;
            }

            iteration++;
        }

        return path;
    }
    
    private void AddDeadEnds()
    {
        Debug.Log("Çıkmaz sokaklar ekleniyor...");
        
        // Zorluk seviyesine göre çıkmaz sokak sayısını belirle (arttırıldı)
        int deadEndCount = Mathf.RoundToInt(Mathf.Lerp(2, 15, (difficultyRating - 1) / 9f));
        int placedDeadEnds = 0;
        int maxAttempts = deadEndCount * 4; // Daha fazla deneme yapılsın
        int attempts = 0;
        
        while (placedDeadEnds < deadEndCount && attempts < maxAttempts)
        {
            attempts++;
            
            // Rastgele bir konum seç
            int x = Random.Range(1, width - 1);
            int y = Random.Range(1, height - 1);
            Vector2Int pos = new Vector2Int(x, y);
            
            // Başlangıç/bitiş pozisyonları ve boş pozisyonları atla
            if (pos == startPos || pos == endPos || grid[y, x] == null)
                continue;
                
            // Belirli tile'lara çıkmaz sokak ekle - sadece belirli tile türleri için
            Tile tile = grid[y, x];
            bool canAddDeadEnd = tile.openDirections.Count >= 3;
            
            // Zorluğa göre düz ve köşeli tile'lara da çıkmaz sokak ekleyebiliriz
            if (difficultyRating > 6 && tile.openDirections.Count >= 2)
                canAddDeadEnd = true;
                
            if (canAddDeadEnd)
            {
                // Rastgele bir yön seç
                List<int> directions = new List<int>(tile.openDirections);
                int randomDirIndex = Random.Range(0, directions.Count);
                int deadEndDir = directions[randomDirIndex];
                
                // Bu yöndeki komşu pozisyonu hesapla
                Vector2Int neighborPos = GetNeighborPosition(pos, deadEndDir);
                
                // Komşu pozisyon sınırlar içinde mi ve boş mu kontrol et
                if (InBounds(neighborPos) && grid[neighborPos.y, neighborPos.x] == null)
                {
                    // Çıkmaz sokak için prefab seç
                    GameObject deadEndPrefab = Random.value < 0.85f ? cornerPrefab : straightPrefab;
                    
                    // Uygun rotasyonla tile yerleştir
                    InstantiateDeadEndTile(neighborPos, deadEndPrefab, GetOppositeDirection(deadEndDir));
                    
                    placedDeadEnds++;
                }
            }
        }
        
        Debug.Log($"Toplam {placedDeadEnds} çıkmaz sokak eklendi.");
    }
    
    // Helper method to instantiate a dead end tile with proper rotation
    private void InstantiateDeadEndTile(Vector2Int pos, GameObject prefab, int incomingDirection)
    {
        // Calculate rotation based on the incoming direction
        float rotationAngle = 0f;
        
        if (prefab == straightPrefab)
        {
            // Straight prefab - rotate based on incoming direction
            switch (incomingDirection)
            {
                case 1: // North
                case 3: // South
                    rotationAngle = 0f; // Default rotation for North-South
                    break;
                case 2: // East
                case 4: // West
                    rotationAngle = 90f; // Rotate 90 degrees for East-West
                    break;
            }
        }
        else if (prefab == cornerPrefab)
        {
            // Corner prefab - rotate based on incoming direction
            switch (incomingDirection)
            {
                case 1: // North
                    rotationAngle = 180f; // Rotate to connect from North
                    break;
                case 2: // East
                    rotationAngle = 270f; // Rotate to connect from East
                    break;
                case 3: // South
                    rotationAngle = 0f; // Default rotation for South
                    break;
                case 4: // West
                    rotationAngle = 90f; // Rotate to connect from West
                    break;
            }
        }
        
        // Create the dead end tile with calculated rotation
        var rotation = Quaternion.Euler(0f, rotationAngle, 0f);
        GameObject go;
        
#if UNITY_EDITOR
        go = UnityEditor.PrefabUtility.InstantiatePrefab(prefab, container) as GameObject;
        go.transform.position = new Vector3(pos.x * spacing, 0, pos.y * spacing);
        //go.transform.rotation = rotation;
#else
        go = Instantiate(prefab,
            new Vector3(pos.x * spacing, 0, pos.y * spacing),
            prefab.transform.rotation,
            container);
#endif
        
        go.name = $"DeadEnd_{pos.x}_{pos.y}";
        var deadEndTile = go.GetComponent<Tile>();
        
        // IMPORTANT: We don't modify openDirections - they come from the prefab
        
        // Add to grid and tiles list
        grid[pos.y, pos.x] = deadEndTile;
        tiles.Add(deadEndTile);
    }

    private Vector2Int GetNeighborPosition(Vector2Int pos, int direction)
    {
        switch (direction)
        {
            case 1: return new Vector2Int(pos.x, pos.y + 1);    // Kuzey
            case 2: return new Vector2Int(pos.x + 1, pos.y);    // Doğu
            case 3: return new Vector2Int(pos.x, pos.y - 1);    // Güney
            case 4: return new Vector2Int(pos.x - 1, pos.y);    // Batı
            default: return pos;
        }
    }
    
private int GetOppositeDirection(int dir)
{
    switch (dir)
    {
        case 1: return 3; // North -> South
        case 2: return 4; // East -> West
        case 3: return 1; // South -> North
        case 4: return 2; // West -> East
        default: return dir;
    }
}

    private Vector2Int[] GenerateStartEndPositions()
    {
        Vector2Int start, end;
        // Minimum distance scales with difficulty (at least 50%-80% of grid diagonal)
        float minDist = Mathf.Lerp(0.5f, 0.8f, (difficultyRating - 1) / 9f) * Mathf.Sqrt(width * width + height * height);
        int maxAttempts = 50;

        do
        {
            // Divide grid into quadrants to ensure start and end are in different regions
            int startQuadrant = Random.Range(0, 4);
            int endQuadrant;
            do
            {
                endQuadrant = Random.Range(0, 4);
            } while (endQuadrant == startQuadrant);

            // Start position
            switch (startQuadrant)
            {
                case 0: // Top-left
                    start = new Vector2Int(Random.Range(0, width / 2), Random.Range(height / 2, height));
                    break;
                case 1: // Top-right
                    start = new Vector2Int(Random.Range(width / 2, width), Random.Range(height / 2, height));
                    break;
                case 2: // Bottom-left
                    start = new Vector2Int(Random.Range(0, width / 2), Random.Range(0, height / 2));
                    break;
                case 3: // Bottom-right
                    start = new Vector2Int(Random.Range(width / 2, width), Random.Range(0, height / 2));
                    break;
                default:
                    start = new Vector2Int(Random.Range(0, width), Random.Range(0, height));
                    break;
            }

            // End position
            switch (endQuadrant)
            {
                case 0: // Top-left
                    end = new Vector2Int(Random.Range(0, width / 2), Random.Range(height / 2, height));
                    break;
                case 1: // Top-right
                    end = new Vector2Int(Random.Range(width / 2, width), Random.Range(height / 2, height));
                    break;
                case 2: // Bottom-left
                    end = new Vector2Int(Random.Range(0, width / 2), Random.Range(0, height / 2));
                    break;
                case 3: // Bottom-right
                    end = new Vector2Int(Random.Range(width / 2, width), Random.Range(0, height / 2));
                    break;
                default:
                    end = new Vector2Int(Random.Range(0, width), Random.Range(0, height));
                    break;
            }

            maxAttempts--;
        } while (Vector2Int.Distance(start, end) < minDist && maxAttempts > 0);

        if (maxAttempts <= 0)
        {
            Debug.LogWarning("Could not find valid start/end positions with sufficient distance. Using fallback.");
            start = new Vector2Int(Random.Range(0, width / 2), Random.Range(0, height / 2));
            end = new Vector2Int(Random.Range(width / 2, width), Random.Range(height / 2, height));
        }

        Debug.Log($"Start: {start}, End: {end}, Distance: {Vector2Int.Distance(start, end)}");
        return new Vector2Int[] { start, end };
    }
    
    private List<List<Vector2Int>> BuildMultiplePaths(Vector2Int start, Vector2Int end)
    {
        List<List<Vector2Int>> allPaths = new List<List<Vector2Int>>();
        
        // Ana yol
        var mainPath = BuildPath(start, end);
        allPaths.Add(mainPath);

        // Zorluk seviyesine göre ek yollar
        int additionalPaths = Mathf.RoundToInt(Mathf.Lerp(0, 2, (difficultyRating - 1) / 9f));
        
        for(int i = 0; i < additionalPaths; i++)
        {
            int randomIndex = Random.Range(1, mainPath.Count - 1);
            Vector2Int branchStart = mainPath[randomIndex];
            
            Vector2Int fakeEnd;
            do
            {
                fakeEnd = new Vector2Int(
                    Random.Range(0, width),
                    Random.Range(0, height)
                );
            } while (Vector2Int.Distance(fakeEnd, branchStart) < width/3);

            var fakePath = BuildPath(branchStart, fakeEnd);
            if(fakePath.Count > 0)
            {
                allPaths.Add(fakePath);
            }
        }

        return allPaths;
    }
private void PlacePaths(List<List<Vector2Int>> allPaths)
{
    foreach (var path in allPaths)
    {
        for (int i = 0; i < path.Count; i++)
        {
            var pos = path[i];
            if (pos == startPos || pos == endPos)
                continue;

            List<int> directions = new List<int>();

            // Determine required directions based on path connections
            if (i > 0)
            {
                var prev = path[i - 1];
                if (prev.y < pos.y) directions.Add(1); // North
                else if (prev.x > pos.x) directions.Add(2); // East
                else if (prev.y > pos.y) directions.Add(3); // South
                else if (prev.x < pos.x) directions.Add(4); // West
            }
            if (i < path.Count - 1)
            {
                var next = path[i + 1];
                if (next.y > pos.y) directions.Add(1); // North
                else if (next.x < pos.x) directions.Add(2); // East
                else if (next.y < pos.y) directions.Add(3); // South
                else if (next.x > pos.x) directions.Add(4); // West
            }

            // Select appropriate prefab based on directions
            GameObject prefab;
            if (directions.Count == 2 && Mathf.Abs(directions[0] - directions[1]) == 2)
            {
                prefab = straightPrefab; // Straight tile
            }
            else if (directions.Count == 2)
            {
                prefab = cornerPrefab; // Corner tile
            }
            else if (directions.Count == 3)
            {
                prefab = tPrefab; // T tile
            }
            else
            {
                prefab = crossPrefab; // Cross tile (fallback)
            }

            bool isKeyTile = keyTilePositions != null && keyTilePositions.Contains(pos);
            bool isOnCriticalPath = criticalPath != null && criticalPath.Contains(pos);
            PlaceOrientedTile(pos, prefab, directions, isKeyTile, isOnCriticalPath);
        }
    }
}
private void PlaceOrientedTile(Vector2Int pos, GameObject prefab, List<int> desiredDirections, bool isKeyTile = false, bool isOnCriticalPath = false)
{
    if (!InBounds(pos) || pos == startPos || pos == endPos)
        return;

    Quaternion rotation = CalculateTileRotation(prefab, desiredDirections);
    GameObject go;

#if UNITY_EDITOR
    go = UnityEditor.PrefabUtility.InstantiatePrefab(prefab, container) as GameObject;
    go.transform.position = new Vector3(pos.x * spacing, 0, pos.y * spacing);
    go.transform.rotation = rotation; // Always apply rotation
#else
    go = Instantiate(prefab, new Vector3(pos.x * spacing, 0, pos.y * spacing), rotation, container);
#endif

    go.name = $"{prefab.name}_{pos.x}_{pos.y}";
    var tile = go.GetComponent<Tile>();
    if (tile == null)
    {
        Debug.LogError($"Tile component missing on prefab {prefab.name} at {pos}");
        DestroyImmediate(go);
        return;
    }

    // IMPORTANT: Update the tile's openDirections based on rotation
    if (tile.openDirections != null && tile.openDirections.Count > 0)
    {
        int rotationAngle = Mathf.RoundToInt(rotation.eulerAngles.y / 90) % 4;
        if (rotationAngle > 0)
        {
            List<int> rotatedDirs = new List<int>();
            foreach (int dir in tile.openDirections)
            {
                // Apply rotation to each direction
                int rotatedDir = ((dir - 1 + rotationAngle) % 4) + 1;
                rotatedDirs.Add(rotatedDir);
            }
            tile.openDirections = rotatedDirs;
        }
    }

    // Apply materials
    var renderer = tile.GetComponentInChildren<Renderer>();
    if (isKeyTile && keyTileMaterial != null && renderer != null)
    {
        renderer.material = keyTileMaterial;
    }
    else if (isOnCriticalPath && criticalPathMaterial != null && difficultyRating <= 3 && renderer != null)
    {
        renderer.material = criticalPathMaterial;
    }

    // Verify tile directions match desired directions
    List<int> actualDirections = tile.openDirections;
    if (desiredDirections.Any(d => !actualDirections.Contains(d)))
    {
        Debug.LogWarning($"Tile at {pos} has mismatched directions. Desired: {string.Join(",", desiredDirections)}, Actual: {string.Join(",", actualDirections)}");
    }

    grid[pos.y, pos.x] = tile;
    if (!tiles.Contains(tile))
        tiles.Add(tile);
}

private bool ValidateLevelSolvability()
{
    if (grid == null || grid.GetLength(0) != height || grid.GetLength(1) != width)
    {
        Debug.LogError("Grid is null or has invalid dimensions");
        return false;
    }

    if (!InBounds(startPos) || !InBounds(endPos))
    {
        Debug.LogError($"Invalid start ({startPos}) or end ({endPos}) position");
        return false;
    }

    var startTileComponent = grid[startPos.y, startPos.x];
    var endTileComponent = grid[endPos.y, endPos.x];
    if (startTileComponent == null || endTileComponent == null)
    {
        Debug.LogError("Start or end tile is null on the grid");
        return false;
    }

    if (startTileComponent.openDirections == null || startTileComponent.openDirections.Count == 0)
    {
        Debug.LogError("Start tile has no openings!");
        return false;
    }
    
    if (endTileComponent.openDirections == null || endTileComponent.openDirections.Count == 0)
    {
        Debug.LogError("End tile has no openings!");
        return false;
    }

    if (!HasConnectedPathNonEmptyTiles(grid, startPos, endPos))
    {
        Debug.LogError("Level is NOT solvable - no physical path of non-empty tiles exists between start and end!");
        return false;
    }

    Debug.Log("Basic path of non-empty tiles found. Performing exhaustive rotation check for solvability...");
    bool isExhaustivelySolvable = ExhaustiveRotationSolvabilityCheck(startPos, endPos);
    
    if (isExhaustivelySolvable)
    {
        Debug.Log("Level IS solvable with proper tile rotations (exhaustive check passed)!");
            return true;
    }
    else
    {
        Debug.LogError("Level is NOT solvable even with all possible rotations (exhaustive check failed)!");
        return false;
    }
}

private bool HasConnectedPathNonEmptyTiles(Tile[,] currentGrid, Vector2Int start, Vector2Int end)
{
    bool[,][] potentialConnections = new bool[height, width][];
    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            potentialConnections[y, x] = new bool[4];
            Tile tile = currentGrid[y, x];
            if (tile == null || tile.openDirections == null || tile.openDirections.Count == 0)
            continue;
            potentialConnections[y, x][0] = true;
            potentialConnections[y, x][1] = true;
            potentialConnections[y, x][2] = true;
            potentialConnections[y, x][3] = true;
        }
    }
    return HasPathWithPotentialConnections(potentialConnections, start, end);
}

private bool HasPathWithPotentialConnections(bool[,][] potentialConnections, Vector2Int start, Vector2Int end)
{
    HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
    Queue<Vector2Int> queue = new Queue<Vector2Int>();
    queue.Enqueue(start);
    visited.Add(start);
    Vector2Int[] directions = { new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0) };
    
    while (queue.Count > 0)
    {
        Vector2Int current = queue.Dequeue();
        if (current == end) return true;
        for (int i = 0; i < 4; i++)
        {
            if (!potentialConnections[current.y, current.x][i]) continue;
            Vector2Int neighbor = current + directions[i];
            if (!InBounds(neighbor) || visited.Contains(neighbor)) continue;
            Tile neighborTile = grid[neighbor.y, neighbor.x]; 
            if (neighborTile == null || neighborTile.openDirections == null || neighborTile.openDirections.Count == 0)
                continue;
            int oppositeDirIndex = (i + 2) % 4;
            if (!potentialConnections[neighbor.y, neighbor.x][oppositeDirIndex]) continue;
            queue.Enqueue(neighbor);
            visited.Add(neighbor);
        }
    }
    return false;
}

// New helper method to check if a tile instance likely came from a specific prefab by name
private bool IsTileFromPrefabName(Tile instance, GameObject prefabReference)
{
    if (instance == null || prefabReference == null || string.IsNullOrEmpty(prefabReference.name)) return false;
    // Assumes instance names are like "PrefabName_x_y", e.g., "StraightPrefab_1_2"
    // Also handles names like "PrefabName(Clone)"
    string instanceName = instance.gameObject.name;
    string prefabName = prefabReference.name;
    return instanceName.StartsWith(prefabName) || instanceName.Contains(prefabName + "(Clone)");
}

// New helper method to get the original open directions from a tile's source prefab
private List<int> GetPrefabOpenDirections(Tile tileInstance)
{
    if (tileInstance == null) return new List<int>();

    GameObject sourcePrefab = null;
    string foundPrefabName = "None";

    if (IsTileFromPrefabName(tileInstance, startPrefab)) { sourcePrefab = startPrefab; foundPrefabName = "StartPrefab"; }
    else if (IsTileFromPrefabName(tileInstance, endPrefab)) { sourcePrefab = endPrefab; foundPrefabName = "EndPrefab"; }
    else if (IsTileFromPrefabName(tileInstance, straightPrefab)) { sourcePrefab = straightPrefab; foundPrefabName = "StraightPrefab"; }
    else if (IsTileFromPrefabName(tileInstance, cornerPrefab)) { sourcePrefab = cornerPrefab; foundPrefabName = "CornerPrefab"; }
    else if (IsTileFromPrefabName(tileInstance, tPrefab)) { sourcePrefab = tPrefab; foundPrefabName = "TPrefab"; }
    else if (IsTileFromPrefabName(tileInstance, crossPrefab)) { sourcePrefab = crossPrefab; foundPrefabName = "CrossPrefab"; }
    else if (IsTileFromPrefabName(tileInstance, emptyPrefab)) { sourcePrefab = emptyPrefab; foundPrefabName = "EmptyPrefab"; }

    if (sourcePrefab != null)
    {
        Tile prefabTileComponent = sourcePrefab.GetComponent<Tile>();
        if (prefabTileComponent != null && prefabTileComponent.openDirections != null)
        {
            // This should be the list of open directions as defined on the prefab asset.
            // Debug.Log($"GetPrefabOpenDirections: Found prefab '{foundPrefabName}' for instance '{tileInstance.gameObject.name}'. Prefab dirs: [{string.Join(",", prefabTileComponent.openDirections)}]");
            return new List<int>(prefabTileComponent.openDirections);
        }
        else if (prefabTileComponent != null && prefabTileComponent.openDirections == null)
        {
            // Prefab's Tile component explicitly has null openDirections (treat as empty)
            // Debug.Log($"GetPrefabOpenDirections: Found prefab '{foundPrefabName}' for instance '{tileInstance.gameObject.name}'. Prefab has NULL openDirections. Returning empty list.");
            return new List<int>();
        }
        else if (prefabTileComponent == null)
        {
            Debug.LogWarning($"GetPrefabOpenDirections: Instance '{tileInstance.gameObject.name}' matched to prefab '{foundPrefabName}', but the prefab asset is missing its Tile component. Returning empty list.");
            return new List<int>();
        }
    }
    
    // Fallback specifically for emptyPrefab if it was identified but had issues, or if no other prefab matched.
    if (foundPrefabName == "EmptyPrefab" || (sourcePrefab == null && emptyPrefab != null && IsTileFromPrefabName(tileInstance, emptyPrefab)))
    {
        // Debug.Log($"GetPrefabOpenDirections: Instance '{tileInstance.gameObject.name}' identified as EmptyPrefab or matched to it. Returning empty list.");
        return new List<int>();
    }

    Debug.LogWarning($"GetPrefabOpenDirections: Could NOT determine original prefab for tile instance '{tileInstance.gameObject.name}' at {tileInstance.transform.position}. Looked for matches with assigned prefabs. Defaulting to NO openings. Check prefab assignments and instance naming. Is '{tileInstance.gameObject.name}' based on one of the known prefabs (Start, End, Straight, Corner, T, Cross, Empty)?");
    return new List<int>(); 
}

private bool ExhaustiveRotationSolvabilityCheck(Vector2Int start, Vector2Int end)
{
    Dictionary<Vector2Int, List<int>> originalPrefabOpenings = new Dictionary<Vector2Int, List<int>>();
    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            Tile currentTileOnGrid = grid[y, x];
            if (currentTileOnGrid != null)
            {
                // Get the base open directions from the tile's source prefab
                List<int> prefabDirs = GetPrefabOpenDirections(currentTileOnGrid);
                originalPrefabOpenings[new Vector2Int(x, y)] = prefabDirs; // Store even if empty (e.g. for empty tiles)
            }
            else
            {
                // If there's no tile on the grid, treat as an empty space for pathfinding purposes
                originalPrefabOpenings[new Vector2Int(x, y)] = new List<int>();
            }
        }
    }
    // Pass the true original prefab openings to the recursive function
    // return TryToFindPathWithRotationsRecursive(originalPrefabOpenings, start, end, new HashSet<Vector2Int>(), 0); // Old
    SolutionPathInfo solution = TryToFindOneSolutionPathRecursive(
        originalPrefabOpenings, 
        start, 
        end, 
        new HashSet<Vector2Int>(), 
        0, // requiredIncomingDirectionForCurrentPos (0 for start)
        new List<Vector2Int>(), // currentAccumulatedPath
        new Dictionary<Vector2Int, int>() // currentAccumulatedRotations
    );
    return solution != null; 
}

private bool CanTileConnectInDirection(List<int> originalOpenDirs, int requiredDirection)
{
    if (originalOpenDirs == null || originalOpenDirs.Count == 0) return false; 
    for (int rotation = 0; rotation < 4; rotation++)
    {
        bool canConnectThisRotation = false;
        foreach (int dir in originalOpenDirs)
        {
            int rotatedDir = ((dir - 1 + rotation) % 4) + 1;
            if (rotatedDir == requiredDirection)
            {
                canConnectThisRotation = true;
                        break;
                    }
                }
        if (canConnectThisRotation) return true;
    }
    return false;
}

// Revised TryToFindPathWithRotationsRecursive -> TryToFindOneSolutionPathRecursive
private SolutionPathInfo TryToFindOneSolutionPathRecursive(
    Dictionary<Vector2Int, List<int>> originalTileOpenings, 
    Vector2Int currentPos, 
    Vector2Int endPos, 
    HashSet<Vector2Int> visited,
    int requiredIncomingDirectionForCurrentPos, // 0 for start tile, 1-4 for others
    List<Vector2Int> currentAccumulatedPath,    // Path built so far
    Dictionary<Vector2Int, int> currentAccumulatedRotations) // Rotations for the path
{
    // Add current position to the path being built for this branch
    currentAccumulatedPath.Add(currentPos);

    if (currentPos == endPos) {
        // Path found! Return the accumulated path and rotations.
        // Rotation for the end tile itself is not typically stored as part of path-making.
        return new SolutionPathInfo(new List<Vector2Int>(currentAccumulatedPath), new Dictionary<Vector2Int, int>(currentAccumulatedRotations));
    }

    visited.Add(currentPos);

    if (!originalTileOpenings.ContainsKey(currentPos)) 
    {
        visited.Remove(currentPos);
        currentAccumulatedPath.RemoveAt(currentAccumulatedPath.Count - 1); // Backtrack path
        return null; 
    }
    List<int> currentTileOriginalOpenDirs = originalTileOpenings[currentPos];
    if (currentTileOriginalOpenDirs.Count == 0) 
    {
        visited.Remove(currentPos);
        currentAccumulatedPath.RemoveAt(currentAccumulatedPath.Count - 1); // Backtrack path
        return null;
    }

    Vector2Int[] worldDirections = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left }; 
    int[] actualDirectionValues = { 1, 2, 3, 4 }; 

    for (int currentRotationVal = 0; currentRotationVal < 4; currentRotationVal++)
    {
        List<int> currentTileRotatedOpenings = new List<int>();
        foreach (int dir in currentTileOriginalOpenDirs)
        {
            currentTileRotatedOpenings.Add(((dir - 1 + currentRotationVal) % 4) + 1);
        }

        if (requiredIncomingDirectionForCurrentPos != 0) 
        {
            if (!currentTileRotatedOpenings.Contains(requiredIncomingDirectionForCurrentPos))
            {
                continue; 
            }
        }
        
        // Record this tile's chosen rotation for this path branch
        // (But only if it's not the end tile, which is handled by the success condition above)
        currentAccumulatedRotations[currentPos] = currentRotationVal;

        for (int i = 0; i < 4; i++) 
        {
            int directionToNeighbor = actualDirectionValues[i]; 

            if (currentTileRotatedOpenings.Contains(directionToNeighbor))
            {
                Vector2Int neighborPos = currentPos + worldDirections[i];

                if (!InBounds(neighborPos) || visited.Contains(neighborPos))
                    continue; 

                if (!originalTileOpenings.ContainsKey(neighborPos)) 
                    continue;
                
                List<int> neighborTileOriginalOpenDirs = originalTileOpenings[neighborPos];
                 // Allow connection to an end tile even if its original openings are "empty" by prefab, 
                 // as its role is to receive, not necessarily to have pre-defined openings for pathfinding.
                 // However, for non-end-tiles, they must have openings.
                if (neighborPos != endPos && (neighborTileOriginalOpenDirs == null || neighborTileOriginalOpenDirs.Count == 0))
                    continue; 

                int requiredDirFromNeighborToAccept = GetOppositeDirection(directionToNeighbor);
                
                // For the end tile, we don't check CanTileConnectInDirection, we just need to reach it.
                // For other tiles, they must be able to rotate to accept the connection.
                if (neighborPos == endPos || CanTileConnectInDirection(neighborTileOriginalOpenDirs, requiredDirFromNeighborToAccept))
                {
                    SolutionPathInfo result = TryToFindOneSolutionPathRecursive(
                        originalTileOpenings, 
                        neighborPos, 
                        endPos, 
                        visited, 
                        requiredDirFromNeighborToAccept,
                        currentAccumulatedPath, // Pass a copy if we don't want side effects from deeper failed branches on this list
                        currentAccumulatedRotations);

                    if (result != null) {
                        return result; // Path found!
                    }
                }
            }
        }
        // Backtrack rotation for currentPos if no path found from it with this rotation
        currentAccumulatedRotations.Remove(currentPos);
    }

    visited.Remove(currentPos);
    currentAccumulatedPath.RemoveAt(currentAccumulatedPath.Count - 1); // Backtrack path
    return null; // No path found from this currentPos
}

private void FillRemainingTiles()
{
    // Reduced cross tile chance to prevent excessive connectivity
    float emptyChance = Mathf.Lerp(0.2f, 0.4f, (difficultyRating - 1) / 9f);
    float straightChance = 0.4f;
    float cornerChance = 0.3f;
    float tChance = 0.15f;
    float crossChance = 0.05f;

    Debug.Log($"Tile distribution - Empty: {emptyChance:F2}, Straight: {straightChance:F2}, " +
              $"Corner: {cornerChance:F2}, T: {tChance:F2}, Cross: {crossChance:F2}");

    // Build reserved positions
    HashSet<Vector2Int> reservedPositions = new HashSet<Vector2Int>();
    if (criticalPath != null)
        reservedPositions.UnionWith(criticalPath);
    if (keyTilePositions != null)
        reservedPositions.UnionWith(keyTilePositions);
    reservedPositions.Add(startPos);
    reservedPositions.Add(endPos);

    // Fill empty grid positions
    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            var pos = new Vector2Int(x, y);
            if (reservedPositions.Contains(pos) || grid[y, x] != null)
                continue;

            float adjustedEmptyChance = emptyChance;
            if (criticalPath != null)
            {
                float minDistance = criticalPath.Min(p => Vector2Int.Distance(pos, p));
                if (minDistance < 2)
                    adjustedEmptyChance *= 0.2f; // Strongly favor connections near critical path
            }

            float r = Random.value;
            GameObject selectedPrefab = null;
            List<int> directions = new List<int>();

            if (r < adjustedEmptyChance)
            {
                selectedPrefab = emptyPrefab;
            }
            else if (r < adjustedEmptyChance + straightChance)
            {
                selectedPrefab = straightPrefab;
                directions = Random.value < 0.5f ? new List<int> { 1, 3 } : new List<int> { 2, 4 };
            }
            else if (r < adjustedEmptyChance + straightChance + cornerChance)
            {
                selectedPrefab = cornerPrefab;
                int firstDir = Random.Range(1, 5);
                int secondDir;
                do
                {
                    secondDir = Random.Range(1, 5);
                } while (secondDir == firstDir || Mathf.Abs(firstDir - secondDir) == 2);
                directions.Add(firstDir);
                directions.Add(secondDir);
            }
            else if (r < adjustedEmptyChance + straightChance + cornerChance + tChance)
            {
                selectedPrefab = tPrefab;
                int excludeDir = Random.Range(1, 5);
                for (int i = 1; i <= 4; i++)
                    if (i != excludeDir)
                        directions.Add(i);
            }
            else
            {
                selectedPrefab = crossPrefab;
                directions = new List<int> { 1, 2, 3, 4 };
            }

            PlaceTile(pos, selectedPrefab, directions);
        }
    }

    // Fill any remaining gaps
    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            var pos = new Vector2Int(x, y);
            if (reservedPositions.Contains(pos) || grid[y, x] != null)
                continue;

            Debug.LogWarning($"Missing tile at {pos}, placing empty tile");
            PlaceTile(pos, emptyPrefab, new List<int>());
        }
    }
}
private List<Vector2Int> BuildPath(Vector2Int start, Vector2Int end)
{
    List<Vector2Int> path = new List<Vector2Int> { start };
    Vector2Int current = start;
    int maxIterations = width * height * 2;

    float directPathBias = Mathf.Lerp(0.95f, 0.75f, (difficultyRating - 1) / 9f);

    while (current != end && maxIterations > 0)
    {
        bool directPath = Random.value < directPathBias;
        Vector2Int next;

        if (directPath)
        {
            Vector2Int dir = new Vector2Int(
                Mathf.Clamp(end.x - current.x, -1, 1),
                Mathf.Clamp(end.y - current.y, -1, 1)
            );
            if (dir.x != 0 && dir.y != 0)
                dir.y = Random.value < 0.5f ? 0 : dir.y;
            next = current + dir;
        }
        else
        {
            Vector2Int[] directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
            next = current + directions[Random.Range(0, directions.Length)];
        }

        if (InBounds(next) && !path.Contains(next))
        {
            path.Add(next);
            current = next;
        }

        maxIterations--;
    }

    if (current != end)
    {
        Debug.LogWarning("Pathfinding failed, using direct path as fallback");
        // Fallback: Create a simple L-shaped path
        path = new List<Vector2Int> { start };
        current = start;

        // Move horizontally to align with end.x
        while (current.x != end.x)
        {
            int step = current.x < end.x ? 1 : -1;
            current = new Vector2Int(current.x + step, current.y);
            if (InBounds(current) && !path.Contains(current))
                path.Add(current);
        }

        // Move vertically to reach end.y
        while (current.y != end.y)
        {
            int step = current.y < end.y ? 1 : -1;
            current = new Vector2Int(current.x, current.y + step);
            if (InBounds(current) && !path.Contains(current))
                path.Add(current);
        }

        if (current != end)
        {
            Debug.LogError("Fallback path failed");
            return new List<Vector2Int> { start, end }; // Minimal path
        }
    }

    Debug.Log($"Path length: {path.Count}");
    return path;
}
    private List<Vector2Int> FindSubPath(Vector2Int subStart, Vector2Int subEnd, float windingFactor)
    {
        var openSet = new PriorityQueue<Vector2Int>(Comparer<Vector2Int>.Create((a, b) =>
        {
            float costA = Vector2Int.Distance(a, subEnd) + Random.value * windingFactor * 5;
            float costB = Vector2Int.Distance(b, subEnd) + Random.value * windingFactor * 5;
            return costA.CompareTo(costB);
        }));
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        var gScore = new Dictionary<Vector2Int, float> { [subStart] = 0 };
        var visited = new HashSet<Vector2Int>();

        openSet.Enqueue(subStart);
        cameFrom[subStart] = subStart;

        Vector2Int[] directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

        while (openSet.Count > 0)
        {
            var current = openSet.Dequeue();
            if (current == subEnd)
                break;

            visited.Add(current);

            // Shuffle directions for randomness
            var shuffledDirections = directions.OrderBy(d => Random.value).ToArray();
            foreach (var dir in shuffledDirections)
            {
                var neighbor = current + dir;
                if (!InBounds(neighbor) || visited.Contains(neighbor))
                    continue;

                float tentativeGScore = gScore[current] + 1;
                if (!gScore.ContainsKey(neighbor) || tentativeGScore < gScore[neighbor])
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeGScore;
                    openSet.Enqueue(neighbor);
                }
            }
        }

        // Reconstruct path
        var path = new List<Vector2Int>();
        if (cameFrom.ContainsKey(subEnd))
        {
            var current = subEnd;
            while (current != subStart)
            {
                path.Add(current);
                current = cameFrom[current];
            }
            path.Add(subStart);
            path.Reverse();
        }
        else
        {
            Debug.LogWarning($"No path found from {subStart} to {subEnd}. Using direct path.");
            path.Add(subStart);
            path.Add(subEnd);
        }

        return path;
    }
    
    List<int> GatherDirections(Vector2Int prev, Vector2Int cur, Vector2Int next)
    {
        var dirs = new HashSet<int>();
        
        // Yön hesaplama öncesi debug
        Debug.Log($"Direction hesaplanıyor - Prev: {prev}, Cur: {cur}, Next: {next}");
        
        // Kuzey (1)
        if (prev.y > cur.y || next.y > cur.y) {
            dirs.Add(1);
            Debug.Log($"Tile {cur}: Kuzey (1) eklendi");
        }
        
        // Doğu (2)
        if (next.x > cur.x || prev.x > cur.x) {
            dirs.Add(2);
            Debug.Log($"Tile {cur}: Doğu (2) eklendi");
        }
        
        // Güney (3)
        if (next.y < cur.y || prev.y < cur.y) {
            dirs.Add(3);
            Debug.Log($"Tile {cur}: Güney (3) eklendi");
        }
        
        // Batı (4)
        if (prev.x < cur.x || next.x < cur.x) {
            dirs.Add(4);
            Debug.Log($"Tile {cur}: Batı (4) eklendi");
        }

        // Straight tile için özel kontrol
        var dirList = new List<int>(dirs);
        if (dirList.Count == 2)
        {
            int d1 = dirList[0], d2 = dirList[1];
            if (Mathf.Abs(d1 - d2) == 2)
            {
                Debug.Log($"Straight tile tespit edildi: {cur} - Yönler: {d1}, {d2}");
            }
        }
        else if (dirList.Count == 1)
        {
            Debug.Log($"Tek yönlü straight tile tespit edildi: {cur} - Yön: {dirList[0]}");
            // Tek yönlü straight tile için karşı yönü ekle
            int oppositeDir = dirList[0] <= 2 ? dirList[0] + 2 : dirList[0] - 2;
            dirs.Add(oppositeDir);
            Debug.Log($"Karşı yön eklendi: {oppositeDir}");
        }

        var finalDirs = new List<int>(dirs);
        Debug.Log($"Final directions for tile at {cur}: {string.Join(", ", finalDirs)}");
        return finalDirs;
    }

    GameObject ChoosePrefab(List<int> dirs)
    {
        // IMPORTANT: This method just selects a prefab, it doesn't modify openDirections
        switch (dirs.Count)
        {
            case 0: return emptyPrefab;
            case 1: return straightPrefab;
            case 2:
                int d1 = dirs[0], d2 = dirs[1];
                return (Mathf.Abs(d1 - d2) == 2)
                    ? straightPrefab
                    : cornerPrefab;
            case 3: return tPrefab;
            case 4: 
                // Zorluğa bağlı olarak bazen cross yerine T şekli kullan
                if (difficultyRating > 5 && Random.value < 0.7f)
                {
                    return tPrefab;
                }
                return crossPrefab;
            default: return emptyPrefab;
        }
    }

    void PlaceTile(Vector2Int pos, GameObject prefab, List<int> dirs)
    {
        if (!InBounds(pos))
        {
            Debug.LogError($"Invalid position: {pos}");
            return;
        }

        // Skip start and end positions
        if (pos == startPos || pos == endPos)
        {
            return;
        }

        try
        {
            // Clean up existing tile if present
            if (grid[pos.y, pos.x] != null && grid[pos.y, pos.x] != startTile && grid[pos.y, pos.x] != endTile)
            {
                // Remove from tiles list if it exists
                if (tiles.Contains(grid[pos.y, pos.x]))
                {
                    tiles.Remove(grid[pos.y, pos.x]);
                }
                
                SafeDestroy(grid[pos.y, pos.x].gameObject);
                grid[pos.y, pos.x] = null;
            }

            // If this position already has a tile (start or end), don't create a new one
            if (grid[pos.y, pos.x] != null)
            {
                return;
            }

            // Ensure we have a prefab to instantiate
            if (prefab == null)
            {
                Debug.LogError($"Null prefab provided for position {pos}. Using empty prefab as fallback.");
                prefab = emptyPrefab;
                
                // If empty prefab is also null, we can't continue
                if (prefab == null)
                {
                    Debug.LogError("Empty prefab is null. Cannot create tile.");
                    return;
                }
            }

            // Create the container if needed
            if (container == null)
            {
                container = GetOrCreateContainer();
            }

            // Create new tile - preserve prefab connection
            GameObject go;
#if UNITY_EDITOR
            go = UnityEditor.PrefabUtility.InstantiatePrefab(prefab, container) as GameObject;
            go.transform.position = new Vector3(pos.x * spacing, 0, pos.y * spacing);
            //go.transform.rotation = prefab.transform.rotation; // Use the prefab's original rotation
#else
            go = Instantiate(prefab,
                new Vector3(pos.x * spacing, 0, pos.y * spacing),
                prefab.transform.rotation, // Use the prefab's original rotation
                container);
#endif

            go.name = $"{prefab.name}_{pos.x}_{pos.y}";
            var tile = go.GetComponent<Tile>();

            if (tile == null)
            {
                Debug.LogError($"Created object does not have a Tile component at position {pos}");
                return;
            }

            // Add to grid and tiles list
            grid[pos.y, pos.x] = tile;
            if (!tiles.Contains(tile))
            {
                tiles.Add(tile);
            }

            // Verify the tile was added to the grid
            if (grid[pos.y, pos.x] == null)
            {
                Debug.LogError($"Failed to add tile to grid at position {pos}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Tile placement error at {pos}: {e.Message}");
        }
    }

    void AssignNeighbors()
    {
        // Safety check - if grid is null, initialize it
        if (grid == null)
        {
            Debug.LogWarning("Grid was null in AssignNeighbors. Initializing a new grid.");
            grid = new Tile[height, width];
            return;
        }
        
        // Reset the tiles list
        tiles.Clear();
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var tile = grid[y, x];
                if (tile == null) continue;

                // Add to tiles list
                if (!tiles.Contains(tile))
                {
                    tiles.Add(tile);
                }

                // Make sure neighbors array is initialized
                if (tile.neighbors == null || tile.neighbors.Length != 4)
                {
                    tile.neighbors = new Tile[4];
                }

                // 1 = Kuzey (+Z) -> y+1
                if (y < height - 1) tile.neighbors[0] = grid[y + 1, x];

                // 2 = Doğu (+X) -> x+1
                if (x < width - 1) tile.neighbors[1] = grid[y, x + 1];

                // 3 = Güney (-Z) -> y-1
                if (y > 0) tile.neighbors[2] = grid[y - 1, x];

                // 4 = Batı (-X) -> x-1
                if (x > 0) tile.neighbors[3] = grid[y, x - 1];
            }
        }
        
        Debug.Log($"AssignNeighbors completed. Total tiles collected: {tiles.Count}");
    }

    bool InBounds(Vector2Int p) =>
        p.x >= 0 && p.x < width && p.y >= 0 && p.y < height;

    public Tile[] GetAllTiles()
    {
        // If tiles list is populated, use that
        if (tiles != null && tiles.Count > 0)
        {
            return tiles.ToArray();
        }
        
        // Otherwise, collect from grid if possible
        List<Tile> allTiles = new List<Tile>();
        if (grid == null) 
        {
            Debug.LogWarning("Grid is null in GetAllTiles. Trying to collect from container...");
            
            // Try to collect from container as a fallback
            if (container != null)
            {
                var tileComponents = container.GetComponentsInChildren<Tile>();
                if (tileComponents.Length > 0)
                {
                    Debug.Log($"Found {tileComponents.Length} tiles in container");
                    return tileComponents;
                }
            }
            
            Debug.LogWarning("Could not find any tiles. Returning empty array.");
            return allTiles.ToArray();
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (grid[y, x] != null)
                {
                    allTiles.Add(grid[y, x]);
                }
            }
        }
        return allTiles.ToArray();
    }

    // Special method for editor-only level creation
    #if UNITY_EDITOR
    public void CreateLevelInEditor()
    {
        // Add a static attempt counter to track recursive calls
        CreateLevelInEditor(0);
    }

    // Helper method with attempt counter
    private void CreateLevelInEditor(int attemptNumber)
    {
        // Safety check to prevent infinite recursion
        if (attemptNumber >= 5)
        {
            Debug.LogError("Failed to create a solvable level after 5 attempts. Please try manually adjusting parameters or try again.");
            EditorUtility.DisplayDialog("Level Creation Failed", 
                "Failed to create a solvable level after 5 attempts.\n\nPlease try manually adjusting parameters (width, height, difficulty) or try again.", 
                "OK");
            return;
        }

        // Clean up any existing level and references
        if (transform.childCount > 0)
        {
            // First clean up the references
            startTile = null;
            endTile = null;
            
            // Then destroy all children
            while (transform.childCount > 0)
            {
                DestroyImmediate(transform.GetChild(0).gameObject);
            }
            container = null;
        }
        
        // Create the level
        CreateLevel();
        
        // Mark the scene as dirty so it can be saved
        if (container != null)
        {
            // Ensure level is solvable
            if (!ValidateLevelSolvability())
            {
                Debug.LogWarning($"Level was not solvable after creation (attempt {attemptNumber + 1}/5)! Trying again...");
                // Recursive call with increased attempt counter
                CreateLevelInEditor(attemptNumber + 1);
                return;
            }

            // Calculate max fillable tiles for the valid level
            SolutionPathInfo mainSolutionPath;
            int maxFillCount = CalculateMaxFillableTiles(out mainSolutionPath);
            
            string pathDetails = "No S-E path found.";
            if (mainSolutionPath != null && mainSolutionPath.PathPositions != null && mainSolutionPath.PathPositions.Count > 0)
            {
                pathDetails = $"Main S-E path has {mainSolutionPath.PathPositions.Count} tiles.";
            }

            string message = $"Max Fillable Tiles: {maxFillCount}\n(Based on one S-E path: {pathDetails})";
            maxFillableTiles = maxFillCount;

            // Find LevelManager and update if it exists
            var levelManager = FindObjectOfType<LevelManager>();
            if (levelManager != null)
            {
                levelManager.maxFillableTiles = maxFillCount;
            }

            // Only show dialog on successful final attempt
            if (attemptNumber == 0 || attemptNumber >= 4)
            {
                EditorUtility.DisplayDialog("Max Fill Calculation", message, "OK");
            }
            
            // Mark all objects in the hierarchy as dirty
            EditorUtility.SetDirty(gameObject);
            EditorUtility.SetDirty(container.gameObject);
            
            // Mark all tiles as dirty
            foreach (var tile in tiles)
            {
                if (tile != null)
                {
                    EditorUtility.SetDirty(tile.gameObject);
                }
            }
            
            // Mark the scene as dirty
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            
            Debug.Log($"Level has been created (attempt {attemptNumber + 1}) and marked for saving. Please save the scene to preserve it.");
        }
    }
    #endif

    // Instantiate the planned level
    private void InstantiatePlannedLevel(List<TilePlan> plan)
    {
        try
        {
            Debug.Log("Creating planned level...");
            
            // Get or create the container
            container = GetOrCreateContainer();
            
            // Initialize the grid
            grid = new Tile[height, width];
            tiles.Clear();
            
            // Instantiate all planned tiles
            foreach (var tilePlan in plan)
            {
                // Calculate the correct rotation based on the tile's directions
                Quaternion tileRotation = CalculateTileRotation(tilePlan.prefab, tilePlan.directions);
                
                // Create the tile with the calculated rotation
                GameObject go;
#if UNITY_EDITOR
                go = UnityEditor.PrefabUtility.InstantiatePrefab(tilePlan.prefab, container) as GameObject;
                go.transform.position = new Vector3(tilePlan.position.x * spacing, 0, tilePlan.position.y * spacing);
                go.transform.rotation = tileRotation;  // Always apply rotation
#else
                go = Instantiate(
                    tilePlan.prefab,
                    new Vector3(tilePlan.position.x * spacing, 0, tilePlan.position.y * spacing),
                    tileRotation,
                    container
                );
#endif
                
                go.name = $"{tilePlan.prefab.name}_{tilePlan.position.x}_{tilePlan.position.y}";
                var tile = go.GetComponent<Tile>();
                
                if (tile == null)
                {
                    Debug.LogError($"Tile component missing on prefab: {tilePlan.prefab.name}");
                    continue;
                }
                
                // IMPORTANT: Update the tile's openDirections based on rotation
                if (tile.openDirections != null && tile.openDirections.Count > 0)
                {
                    int rotationAngle = Mathf.RoundToInt(tileRotation.eulerAngles.y / 90) % 4;
                    if (rotationAngle > 0)
                    {
                        List<int> rotatedDirs = new List<int>();
                        foreach (int dir in tile.openDirections)
                        {
                            // Apply rotation to each direction
                            int rotatedDir = ((dir - 1 + rotationAngle) % 4) + 1;
                            rotatedDirs.Add(rotatedDir);
                        }
                        tile.openDirections = rotatedDirs;
                    }
                }
                
                // Add to grid and list
                grid[tilePlan.position.y, tilePlan.position.x] = tile;
                tiles.Add(tile);
                
                // Apply special materials for critical path and key tiles
                if (tilePlan.isKeyTile && keyTileMaterial != null)
                {
                    var renderer = tile.GetComponentInChildren<Renderer>();
                    if (renderer != null)
                    {
                        renderer.material = keyTileMaterial;
                    }
                }
                else if (tilePlan.isOnCriticalPath && criticalPathMaterial != null && difficultyRating <= 3)
                {
                    // Only show critical path hints at low difficulties
                    var renderer = tile.GetComponentInChildren<Renderer>();
                    if (renderer != null)
                    {
                        renderer.material = criticalPathMaterial;
                    }
                }
                
                // Store references to start and end tiles
                if (tilePlan.position == startPos)
                {
                    startTile = tile;
                }
                else if (tilePlan.position == endPos)
                {
                    endTile = tile;
                }
            }
            
            // Assign neighbors
            AssignNeighbors();
            
            // Ensure level is solvable
            if (!ValidateLevelSolvability())
            {
                Debug.LogWarning("Level was not solvable after creation! You may need to verify the level design.");
                CreateLevelInEditor();
                return;
            }
            
            // Notify LevelManager about start and end tiles
            var levelManager = FindObjectOfType<LevelManager>();
            if (levelManager != null && startTile != null && endTile != null)
            {
                levelManager.SetStartAndEndTiles(startTile, endTile);
                Debug.Log($"Set start tile {startTile.name} and end tile {endTile.name} on LevelManager");
            }
            
            Debug.Log($"Level successfully created! {tiles.Count} tiles placed.");
            SolutionPathInfo mainSolutionPath;
            int maxFillCount = CalculateMaxFillableTiles(out mainSolutionPath);
            
            string pathDetails = "No S-E path found.";
            if (mainSolutionPath != null && mainSolutionPath.PathPositions != null && mainSolutionPath.PathPositions.Count > 0)
            {
                pathDetails = $"Main S-E path has {mainSolutionPath.PathPositions.Count} tiles.";
                // Optional: Log more details about the path to console
                // Debug.Log($"Main S-E Path Details: {string.Join(", ", mainSolutionPath.PathPositions)}");
                // foreach(var entry in mainSolutionPath.Rotations)
                // {
                //    Debug.Log($"  Tile {entry.Key} uses rotation {entry.Value}");
                // }
            }

            string message = $"Max Fillable Tiles: {maxFillCount}\n(Based on one S-E path: {pathDetails})";
            maxFillableTiles = maxFillCount;
            levelManager.maxFillableTiles = maxFillCount;
            EditorUtility.DisplayDialog("Max Fill Calculation", message, "OK");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Level creation error: {e.Message}\n{e.StackTrace}");
        }
    }
    
    // Plan paths without instantiating
    private void PlanPaths(List<List<Vector2Int>> paths, Dictionary<Vector2Int, List<int>> planningGrid)
    {
        foreach(var path in paths)
        {
            for (int i = 1; i < path.Count - 1; i++)
            {
                var prev = path[i - 1];
                var cur = path[i];
                var next = path[i + 1];

                // Gather directions for this tile
                var dirs = GatherDirections(prev, cur, next);
                
                // Add to planning grid, merging with existing directions if needed
                if (planningGrid.ContainsKey(cur))
                {
                    // Merge directions
                    foreach (int dir in dirs)
                    {
                        if (!planningGrid[cur].Contains(dir))
                        {
                            planningGrid[cur].Add(dir);
                        }
                    }
                }
                else
                {
                    planningGrid[cur] = dirs;
                }
            }
        }
    }
    
    // Plan dead ends without instantiating
    private void PlanDeadEnds(Dictionary<Vector2Int, List<int>> planningGrid)
    {
        Debug.Log("Planning dead ends...");
        // int deadEndCount = Mathf.RoundToInt(Mathf.Lerp(5, 20, (difficultyRating - 1) / 9f)); // Old
        int deadEndCount = Mathf.RoundToInt(Mathf.Lerp(5, 30, (difficultyRating - 1) / 9f)); // New: up to 30
        int placedDeadEnds = 0;
        int maxAttempts = deadEndCount * 5;

        // Collect potential anchor points (tiles on paths)
        List<Vector2Int> anchorPoints = planningGrid.Keys
            .Where(p => p != startPos && p != endPos && planningGrid[p].Count >= 2)
            .ToList();

        while (placedDeadEnds < deadEndCount && maxAttempts > 0)
        {
            maxAttempts--;
            Vector2Int pos = anchorPoints[Random.Range(0, anchorPoints.Count)];
            List<int> directions = planningGrid[pos];

            int deadEndDir = directions[Random.Range(0, directions.Count)];
            Vector2Int neighborPos = GetNeighborPosition(pos, deadEndDir);

            if (InBounds(neighborPos) && !planningGrid.ContainsKey(neighborPos))
            {
                planningGrid[neighborPos] = new List<int> { GetOppositeDirection(deadEndDir) };
                placedDeadEnds++;
                // Add the neighbor to anchor points for potential chaining
                if (difficultyRating > 7 && Random.value < 0.3f)
                    anchorPoints.Add(neighborPos);
            }
        }

        Debug.Log($"Total of {placedDeadEnds} dead ends planned.");
    }
        // Plan remaining tiles without instantiating
    
    private void PlanRemainingTiles(Dictionary<Vector2Int, List<int>> planningGrid)
    {
        // Adjust tile distribution based on difficulty
        float baseEmptyChance;
        if (difficultyRating == 10) {
            baseEmptyChance = 0.05f; // Very few empty tiles
        } else if (difficultyRating == 9) {
            baseEmptyChance = 0.10f; // Few empty tiles
        } else {
            baseEmptyChance = Mathf.Lerp(0.6f, 0.15f, (difficultyRating - 1) / 8f); // Adjusted to prevent 0.1 at diff 8 matching diff 9 if not careful with Lerp range
        }
        
        // Proportional factors for other tiles (should sum to 1.0)
        float straightChanceFactor;
        float cornerChanceFactor;
        float tChanceFactor;
        float crossChanceFactor;

        if (difficultyRating >= 9) { // For difficulty 9 and 10, prioritize complex tiles
            straightChanceFactor = 0.20f; 
            cornerChanceFactor = 0.25f;
            tChanceFactor = 0.30f;
            crossChanceFactor = 0.25f; // High chance for T and Cross
        } else if (difficultyRating >= 7) { // For difficulty 7 and 8
            straightChanceFactor = 0.30f; 
            cornerChanceFactor = 0.30f;
            tChanceFactor = 0.25f;
            crossChanceFactor = 0.15f;
        } else { // For difficulties 1-6
            straightChanceFactor = 0.45f; 
            cornerChanceFactor = 0.35f;
            tChanceFactor = 0.15f;
            crossChanceFactor = 0.05f; 
        }

        float remainingSpaceChance = 1.0f - baseEmptyChance;
        float actualStraightChance = remainingSpaceChance * straightChanceFactor;
        float actualCornerChance = remainingSpaceChance * cornerChanceFactor;
        float actualTChance = remainingSpaceChance * tChanceFactor;
        float actualCrossChance = remainingSpaceChance * crossChanceFactor; // or remainingSpaceChance * (1 - (straight + corner + t)) for precision
        
        Debug.Log($"Tile distribution (Difficulty: {difficultyRating}) - BaseEmpty: {baseEmptyChance:F2}, " +
                  $"ActualStraight: {actualStraightChance:F2}, ActualCorner: {actualCornerChance:F2}, " +
                  $"ActualT: {actualTChance:F2}, ActualCross: {actualCrossChance:F2}");
        
        // Check for reserved positions (key tiles)
        HashSet<Vector2Int> reservedPositions = new HashSet<Vector2Int>();
        if (keyTilePositions != null)
        {
            foreach (var pos in keyTilePositions)
            {
                reservedPositions.Add(pos);
            }
        }
        
        // Track positions still needing tiles
        List<Vector2Int> positionsToFill = new List<Vector2Int>();
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var pos = new Vector2Int(x, y);
                
                // Skip already planned positions
                if (planningGrid.ContainsKey(pos) || reservedPositions.Contains(pos))
                    continue;
                
                // Add to positions needing tiles
                positionsToFill.Add(pos);
            }
        }
        
        // First pass: random distribution based on probabilities
        foreach (var pos in positionsToFill)
        {
            float r = Random.value;
            List<int> dirs = new List<int>();
            
            // On higher difficulties, also consider the position's distance to the main path
            if (difficultyRating > 5 && criticalPath != null)
            {
                // Check if this position is far from the critical path
                bool farFromCriticalPath = true;
                float minDistance = float.MaxValue;
                
                foreach (var critPos in criticalPath)
                {
                    float distance = Vector2Int.Distance(pos, critPos);
                    minDistance = Mathf.Min(minDistance, distance);
                    if (distance < 3)
                    {
                        farFromCriticalPath = false;
                        break;
                    }
                }
                
                // If far from critical path, increase empty tile probability
                if (farFromCriticalPath)
                {
                    // Cap maximum empty chance at 0.7 to ensure some tiles still appear
                    baseEmptyChance = Mathf.Min(baseEmptyChance + 0.2f, 0.7f);
                }
                
                // At the highest difficulties, ensure areas very far from path still have some connections
                if (difficultyRating >= 9 && minDistance > 5)
                {
                    // If very far from path, reduce empty chance to ensure some connections
                    baseEmptyChance = Mathf.Max(baseEmptyChance - 0.2f, 0.3f);
                }
            }
            
            // Determine directions based on random value
            if (r < baseEmptyChance)
            {
                // Empty tile - no directions
            }
            else if (r < baseEmptyChance + actualStraightChance)
            {
                // Straight tile - random orientation
                if (Random.value < 0.5f)
                {
                    dirs.Add(1); // North
                    dirs.Add(3); // South
                }
                else
                {
                    dirs.Add(2); // East
                    dirs.Add(4); // West
                }
            }
            else if (r < baseEmptyChance + actualStraightChance + actualCornerChance)
            {
                // Corner tile - random orientation
                int firstDir = Random.Range(1, 5);
                int secondDir;
                do {
                    secondDir = Random.Range(1, 5);
                } while (secondDir == firstDir || Mathf.Abs(firstDir - secondDir) == 2);
                
                dirs.Add(firstDir);
                dirs.Add(secondDir);
            }
            else if (r < baseEmptyChance + actualStraightChance + actualCornerChance + actualTChance)
            {
                // T tile - random orientation
                int excludeDir = Random.Range(1, 5);
                for (int i = 1; i <= 4; i++)
                {
                    if (i != excludeDir)
                        dirs.Add(i);
                }
            }
            else
            {
                // Cross tile - all directions
                dirs.Add(1);
                dirs.Add(2);
                dirs.Add(3);
                dirs.Add(4);
            }
            
            // Add to planning grid
            planningGrid[pos] = dirs;
        }
        
        // Second pass: ensure all positions have an entry in the planning grid
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var pos = new Vector2Int(x, y);
                
                // Skip start and end positions
                if (pos == startPos || pos == endPos)
                    continue;
                    
                // If this position doesn't have an entry, add an empty tile
                if (!planningGrid.ContainsKey(pos))
                {
                    planningGrid[pos] = new List<int>();
                    Debug.LogWarning($"Added missing empty tile at position {pos}");
                }
            }
        }
    }

private Quaternion CalculateTileRotation(GameObject prefab, List<int> desiredDirections)
{
    if (desiredDirections == null || desiredDirections.Count == 0)
    {
        Debug.LogWarning($"No desired directions for {prefab.name}, using default rotation");
        return Quaternion.identity;
    }

    var tile = prefab.GetComponent<Tile>();
    if (tile == null || tile.openDirections == null)
    {
        Debug.LogWarning($"Prefab {prefab.name} has no Tile component or openDirections");
        return Quaternion.identity;
    }

    // Try all rotations
    for (int rot = 0; rot < 4; rot++)
    {
        List<int> rotatedDirections = tile.openDirections
            .Select(d => ((d - 1 + rot) % 4) + 1)
            .ToList();
        if (desiredDirections.All(d => rotatedDirections.Contains(d)) &&
            rotatedDirections.All(d => desiredDirections.Contains(d)))
        {
            return Quaternion.Euler(0, rot * 90, 0);
        }
    }

    Debug.LogWarning($"No valid rotation for {prefab.name} with desired directions: {string.Join(",", desiredDirections)}");
    // Fallback: Use a rotation that partially matches
    for (int rot = 0; rot < 4; rot++)
    {
        List<int> rotatedDirections = tile.openDirections
            .Select(d => ((d - 1 + rot) % 4) + 1)
            .ToList();
        if (desiredDirections.Any(d => rotatedDirections.Contains(d)))
        {
            Debug.LogWarning($"Using partial rotation match for {prefab.name}");
            return Quaternion.Euler(0, rot * 90, 0);
        }
    }

    return Quaternion.identity;
}

    // New public method to calculate max fillable tiles
    public int CalculateMaxFillableTiles(out SolutionPathInfo mainSolutionPath)
    {
        mainSolutionPath = null; 
        Dictionary<Vector2Int, List<int>> originalAllTileOpenings = GetOriginalPrefabOpenings();

        // 1. Find ALL valid Start-to-End paths.
        List<SolutionPathInfo> allSEPaths = FindAllSolutionPaths(startPos, endPos, originalAllTileOpenings);

        if (allSEPaths == null || allSEPaths.Count == 0)
        {
            Debug.LogError("[CalculateMaxFillableTiles] No S-E solution paths found. Cannot calculate fill.");
            mainSolutionPath = null;
            return 0;
        }

        Debug.Log($"[CalculateMaxFillableTiles] Found {allSEPaths.Count} potential S-E paths. Evaluating fill for each...");

        int maxOverallFillCount = 0;
        SolutionPathInfo bestMainPathForMaxFill = null;

        int pathCounter = 0;
        foreach (SolutionPathInfo pathOption in allSEPaths)
        {
            pathCounter++;
            // Debug.Log($"[CalculateMaxFillableTiles] Evaluating S-E path option {pathCounter}/{allSEPaths.Count}...");
            int currentPathFillCount = ExploreFillFromPath(pathOption, originalAllTileOpenings);
            // Debug.Log($"[CalculateMaxFillableTiles] S-E path option {pathCounter} resulted in a fill count of {currentPathFillCount}.");

            if (currentPathFillCount > maxOverallFillCount)
            {
                maxOverallFillCount = currentPathFillCount;
                bestMainPathForMaxFill = pathOption;
            }
        }
        
        mainSolutionPath = bestMainPathForMaxFill; // Output the S-E path that led to the max fill

        if (mainSolutionPath != null)
        {
            Debug.Log($"[CalculateMaxFillableTiles] Max fill count: {maxOverallFillCount} (achieved with S-E path of {mainSolutionPath.PathPositions.Count} tiles).");
        }
        else
        {
            // Should not happen if allSEPaths was not empty, but as a fallback.
            Debug.LogWarning($"[CalculateMaxFillableTiles] Max fill count: {maxOverallFillCount}, but no specific S-E path was determined as best (this might indicate an issue or only one S-E path found).");
        }
        
        return maxOverallFillCount;
    }

    // Helper to get original prefab openings for all tiles on the grid
    private Dictionary<Vector2Int, List<int>> GetOriginalPrefabOpenings()
    {
        Dictionary<Vector2Int, List<int>> originalPrefabOpenings = new Dictionary<Vector2Int, List<int>>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Tile currentTileOnGrid = grid[y, x];
                if (currentTileOnGrid != null)
                {
                    List<int> prefabDirs = GetPrefabOpenDirections(currentTileOnGrid);
                    originalPrefabOpenings[new Vector2Int(x, y)] = prefabDirs;
                }
                else
                {
                    originalPrefabOpenings[new Vector2Int(x, y)] = new List<int>(); // Empty space
                }
            }
        }
        return originalPrefabOpenings;
    }

    // Placeholder for finding ALL solution paths (more complex)
    private List<SolutionPathInfo> FindAllSolutionPaths(Vector2Int startNode, Vector2Int endNode, Dictionary<Vector2Int, List<int>> originalAllTileOpenings)
    {
        List<SolutionPathInfo> allFoundPaths = new List<SolutionPathInfo>();
        FindAllSolutionPathsRecursive(
            originalAllTileOpenings,
            startNode,
            endNode,
            new HashSet<Vector2Int>(), // visitedInCurrentPath
            allFoundPaths,             // list to populate
            0,                         // requiredIncomingDirectionForCurrentPos (0 for start)
            new List<Vector2Int>(),    // currentAccumulatedPath
            new Dictionary<Vector2Int, int>() // currentAccumulatedRotations
        );

        Debug.Log($"[FindAllSolutionPaths] Found {allFoundPaths.Count} potential S-E solution paths between {startNode} and {endNode}.");
        return allFoundPaths;
    }

    private void FindAllSolutionPathsRecursive(
        Dictionary<Vector2Int, List<int>> originalTileOpenings,
        Vector2Int currentPos,
        Vector2Int endPos,
        HashSet<Vector2Int> visitedInCurrentPath, 
        List<SolutionPathInfo> allFoundPaths,
        int requiredIncomingDirectionForCurrentPos, 
        List<Vector2Int> currentAccumulatedPath,
        Dictionary<Vector2Int, int> currentAccumulatedRotations)
    {
        // Add current position to the path and mark as visited for this specific path attempt
        currentAccumulatedPath.Add(currentPos);
        visitedInCurrentPath.Add(currentPos);

        if (currentPos == endPos)
        {
            // Found a complete path to the end
            // Create new instances for the path and rotations to store them independently
            allFoundPaths.Add(new SolutionPathInfo(
                new List<Vector2Int>(currentAccumulatedPath),
                new Dictionary<Vector2Int, int>(currentAccumulatedRotations)
            ));
            // Backtrack to allow exploring other branches
            visitedInCurrentPath.Remove(currentPos);
            currentAccumulatedPath.RemoveAt(currentAccumulatedPath.Count - 1);
            return; // Stop this branch of recursion
        }

        // Get original openings for the current tile
        if (!originalTileOpenings.TryGetValue(currentPos, out List<int> currentTileOriginalOpenDirs) || currentTileOriginalOpenDirs == null || currentTileOriginalOpenDirs.Count == 0)
        {
            // Current tile is empty or has no openings, cannot continue path from here
            visitedInCurrentPath.Remove(currentPos);
            currentAccumulatedPath.RemoveAt(currentAccumulatedPath.Count - 1);
            return;
        }

        Vector2Int[] worldDirections = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        int[] actualDirectionValues = { 1, 2, 3, 4 }; // N, E, S, W

        // Try all 4 possible rotations for the current tile
        for (int currentRotationVal = 0; currentRotationVal < 4; currentRotationVal++)
        {
            List<int> currentTileRotatedOpenings = new List<int>();
            foreach (int dir in currentTileOriginalOpenDirs)
            {
                currentTileRotatedOpenings.Add(((dir - 1 + currentRotationVal) % 4) + 1);
            }

            // Check if this rotation is compatible with the required incoming direction (if not start tile)
            if (requiredIncomingDirectionForCurrentPos != 0)
            {
                if (!currentTileRotatedOpenings.Contains(requiredIncomingDirectionForCurrentPos))
                {
                    continue; // This rotation is not valid for the incoming path, try next rotation
                }
            }

            // Temporarily fix this rotation for the current tile for this path branch
            currentAccumulatedRotations[currentPos] = currentRotationVal;

            // Explore neighbors
            for (int i = 0; i < 4; i++) // Iterate N, E, S, W
            {
                int directionToNeighbor = actualDirectionValues[i];
                if (currentTileRotatedOpenings.Contains(directionToNeighbor))
                {
                    Vector2Int neighborPos = currentPos + worldDirections[i];

                    if (InBounds(neighborPos) && !visitedInCurrentPath.Contains(neighborPos))
                    {
                        if (!originalTileOpenings.TryGetValue(neighborPos, out List<int> neighborTileOriginalOpenDirs) || neighborTileOriginalOpenDirs == null)
                        {
                            continue; // Should not happen if grid fully mapped, but safety check
                        }

                        // Allow connection to end tile even if it has no "openings" by its prefab definition (it just needs to be reached)
                        // For other tiles, they must have openings to be part of a path.
                        if (neighborPos != endPos && neighborTileOriginalOpenDirs.Count == 0)
                        {
                            continue; // Neighbor is an empty non-end tile
                        }

                        int requiredDirFromNeighborToAccept = GetOppositeDirection(directionToNeighbor);

                        // Check if neighbor can connect (end tile always can, others need CanTileConnectInDirection)
                        if (neighborPos == endPos || CanTileConnectInDirection(neighborTileOriginalOpenDirs, requiredDirFromNeighborToAccept))
                        {
                            FindAllSolutionPathsRecursive(
                                originalTileOpenings,
                                neighborPos,
                                endPos,
                                visitedInCurrentPath,
                                allFoundPaths,
                                requiredDirFromNeighborToAccept,
                                currentAccumulatedPath,
                                currentAccumulatedRotations
                            );
                        }
                    }
                }
            }
            // Backtrack the rotation for currentPos before trying the next rotation for currentPos, or finishing currentPos exploration
            currentAccumulatedRotations.Remove(currentPos);
        }

        // Backtrack from currentPos after all its rotations and subsequent paths have been explored
        visitedInCurrentPath.Remove(currentPos);
        currentAccumulatedPath.RemoveAt(currentAccumulatedPath.Count - 1);
    }

    // This is the old ExploreFillFromPath, which will be refactored to use the iterative lookahead approach.
    private int ExploreFillFromPath(SolutionPathInfo currentPath, Dictionary<Vector2Int, List<int>> originalAllTileOpenings)
    {
        if (currentPath == null || currentPath.PathPositions == null || currentPath.PathPositions.Count == 0)
        {
            Debug.LogWarning("[ExploreFillPath] Received null or empty currentPath.");
            return 0;
        }

        Debug.Log($"[ExploreFillPath] Exploring fill for S-E path with {currentPath.PathPositions.Count} tiles. Path Rotations: {currentPath.Rotations.Count}");
        foreach(var tilePosInPath in currentPath.PathPositions) 
        {
            string rotInfo = currentPath.Rotations.TryGetValue(tilePosInPath, out int r) ? r.ToString() : "N/A (end or unrecorded start)";
            //Debug.Log($"[ExploreFillPath] Main S-E Path Tile: {tilePosInPath}, Initial Rotation: {rotInfo}"); // Optional: can be noisy
        }

        HashSet<Vector2Int> filledTiles = new HashSet<Vector2Int>(currentPath.PathPositions);
        Dictionary<Vector2Int, int> currentFixedRotations = new Dictionary<Vector2Int, int>(currentPath.Rotations);

        if (!currentFixedRotations.ContainsKey(startPos) && 
            originalAllTileOpenings.TryGetValue(startPos, out List<int> startTileOpenDirs) && 
            startTileOpenDirs != null && startTileOpenDirs.Count > 0)
        {
            Debug.LogWarning($"[ExploreFillPath] Rotation for startPos {startPos} was not in mainSolutionPath.Rotations. Defaulting its rotation to 0 for fill exploration as it has openings.");
            currentFixedRotations[startPos] = 0;
        }
        
        Queue<Vector2Int> frontier = new Queue<Vector2Int>();
        foreach (var pos in currentPath.PathPositions) // Prime frontier with all tiles from the given S-E path
        {
            if (currentFixedRotations.ContainsKey(pos) || pos == startPos) // Ensure tile has a rotation or is startPos (which will get one)
            {
                 frontier.Enqueue(pos);
            }
        }

        Vector2Int[] worldDirections = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left }; 
        int[] actualDirectionValues = { 1, 2, 3, 4 }; 

        int iterations = 0; 
        while (frontier.Count > 0 && iterations < (width * height * 5)) 
        {
            iterations++;
            Vector2Int activeTilePos = frontier.Dequeue();
            
            if (!originalAllTileOpenings.TryGetValue(activeTilePos, out List<int> activeTileOriginalOpenDirs) || activeTileOriginalOpenDirs == null)
            {
                Debug.LogError($"[ExploreFillPath] Missing original open directions for active tile {activeTilePos}. Skipping.");
                continue;
            }
            
            if (!currentFixedRotations.TryGetValue(activeTilePos, out int activeTileCurrentRotation))
            {
                Debug.LogError($"[ExploreFillPath] CRITICAL: Could not find rotation for active tile {activeTilePos}. Skipping tile.");
                continue;
            }

            List<int> activeTileRotatedOpenings = new List<int>();
            foreach (int dir in activeTileOriginalOpenDirs)
            {
                activeTileRotatedOpenings.Add(((dir - 1 + activeTileCurrentRotation) % 4) + 1);
            }

            for (int i = 0; i < 4; i++) 
            {
                int outgoingDirectionFromActive = actualDirectionValues[i]; 
                
                if (activeTileRotatedOpenings.Contains(outgoingDirectionFromActive))
                {
                    Vector2Int neighborPos = activeTilePos + worldDirections[i];

                    if (!InBounds(neighborPos) || filledTiles.Contains(neighborPos))
                        continue;

                    if (!originalAllTileOpenings.TryGetValue(neighborPos, out List<int> neighborOriginalOpenDirs) || neighborOriginalOpenDirs == null || neighborOriginalOpenDirs.Count == 0) 
                        continue; // Neighbor is empty or invalid

                    int requiredConnectionFromNeighbor = GetOppositeDirection(outgoingDirectionFromActive);

                    for (int neighborRotationVal = 0; neighborRotationVal < 4; neighborRotationVal++)
                    {
                        List<int> neighborRotatedOpenings = new List<int>();
                        foreach (int dir in neighborOriginalOpenDirs)
                        {
                            neighborRotatedOpenings.Add(((dir - 1 + neighborRotationVal) % 4) + 1);
                        }

                        if (neighborRotatedOpenings.Contains(requiredConnectionFromNeighbor))
                        {
                            filledTiles.Add(neighborPos);
                            frontier.Enqueue(neighborPos);
                            currentFixedRotations[neighborPos] = neighborRotationVal;
                            // Debug.Log($"[ExploreFillPath] ADDED Branch: {neighborPos} (rot: {neighborRotationVal}) TO: {activeTilePos} (rot: {activeTileCurrentRotation}) VIA: {outgoingDirectionFromActive}");
                            break; 
                        }
                    }
                }
            }
        }
        if (iterations >= (width*height*5)) {
            Debug.LogWarning("[ExploreFillPath] Exceeded iteration limit.");
        }
        // Debug.Log($"[ExploreFillPath] Fill count for this S-E path: {filledTiles.Count}. Iterations: {iterations}");
        return filledTiles.Count;
    }

}



