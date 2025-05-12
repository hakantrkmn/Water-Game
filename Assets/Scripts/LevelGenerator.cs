// TileGenerator.cs
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
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

    private Tile[,] grid;
    public List<Tile> tiles = new List<Tile>();
    private Transform container;
    private Vector2Int startPos;
    private Vector2Int endPos;
    
    // Kalıcı start ve end tile referansları
    private Tile startTile;
    private Tile endTile;
    
    // Zorluk seviyesine göre ayarlanabilir değerler
    private float deadEndProbability;
    private float misleadingPathProbability;
    private int maxPathLength;
    private float complexityFactor;

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

        public TilePlan(Vector2Int pos, GameObject prefab, List<int> dirs)
        {
            this.position = pos;
            this.prefab = prefab;
            this.directions = new List<int>(dirs);
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
            Debug.Log("Level planlaması başlıyor...");
            
            // Initialize the grid for planning
            grid = new Tile[height, width];
            
            // Clear the tiles list
            tiles.Clear();
            
            // Start ve End pozisyonlarını belirle
            startPos = new Vector2Int(
                Mathf.Clamp(width / 4, 1, width - 2),
                Mathf.Clamp(1, 1, height / 3)
            );

            endPos = new Vector2Int(
                Mathf.Clamp((3 * width) / 4, 2, width - 2),
                Mathf.Clamp(height - 2, height * 2/3, height - 2)
            );

            // Pozisyonların çakışmadığından emin ol
            if (Vector2Int.Distance(startPos, endPos) < 3)
            {
                Debug.LogWarning("Start ve End pozisyonları çok yakın! Ayarlanıyor...");
                endPos.x = Mathf.Clamp(startPos.x + 3, 0, width - 1);
                endPos.y = Mathf.Clamp(startPos.y + 3, 0, height - 1);
            }

            Debug.Log($"Start pozisyonu: {startPos}, End pozisyonu: {endPos}");
            
            // Create the plan list and add start/end tiles
            List<TilePlan> plan = new List<TilePlan>();
            
            // Add start and end tiles to the plan
            plan.Add(new TilePlan(startPos, startPrefab, new List<int>()));
            plan.Add(new TilePlan(endPos, endPrefab, new List<int>()));
            
            // Create a temporary grid for path planning
            Dictionary<Vector2Int, List<int>> planningGrid = new Dictionary<Vector2Int, List<int>>();
            planningGrid[startPos] = new List<int>();
            planningGrid[endPos] = new List<int>();

            // 4. Ana yolu oluştur
            var mainPath = BuildPath(startPos, endPos);
            if (mainPath == null || mainPath.Count < 2)
            {
                Debug.LogError("Ana yol oluşturulamadı!");
                return null;
            }
            
            // Yanıltıcı ve çıkmaz yollar oluştur (zorluk seviyesine göre)
            var allPaths = new List<List<Vector2Int>> { mainPath };
            GenerateDeceptivePaths(allPaths, mainPath);

            // 5. Plan all paths
            PlanPaths(allPaths, planningGrid);

            // 6. Plan dead ends
            if (difficultyRating > 3)
            {
                PlanDeadEnds(planningGrid);
            }

            // 7. Plan remaining tiles
            PlanRemainingTiles(planningGrid);
            
            // 8. Convert planning grid to tile plans
            foreach (var entry in planningGrid)
            {
                Vector2Int pos = entry.Key;
                List<int> dirs = entry.Value;
                
                // Skip start and end positions - they're already in the plan
                if (pos == startPos || pos == endPos)
                    continue;
                
                // Choose appropriate prefab based on directions
                GameObject prefab = ChoosePrefab(dirs);
                
                // Add to plan
                plan.Add(new TilePlan(pos, prefab, dirs));
            }
            
            Debug.Log($"Level planlaması tamamlandı. {plan.Count} tile planlandı.");
            return plan;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Level planlama hatası: {e.Message}\n{e.StackTrace}");
            return null;
        }
    }
    
    // Instantiate the planned level
    private void InstantiatePlannedLevel(List<TilePlan> plan)
    {
        try
        {
            Debug.Log("Planlanan level oluşturuluyor...");
            
            // Get or create the container
            container = GetOrCreateContainer();
            
            // Initialize the grid
            grid = new Tile[height, width];
            tiles.Clear();
            
            // Instantiate all planned tiles
            foreach (var tilePlan in plan)
            {
                // Create the tile without rotation - use PrefabUtility in editor mode
                GameObject go;
#if UNITY_EDITOR
                go = UnityEditor.PrefabUtility.InstantiatePrefab(tilePlan.prefab, container) as GameObject;
                go.transform.position = new Vector3(tilePlan.position.x * spacing, 0, tilePlan.position.y * spacing);
                go.transform.rotation = Quaternion.identity;
#else
                go = Instantiate(
                    tilePlan.prefab,
                    new Vector3(tilePlan.position.x * spacing, 0, tilePlan.position.y * spacing),
                    Quaternion.identity,
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
                
                // Add to grid and list
                grid[tilePlan.position.y, tilePlan.position.x] = tile;
                tiles.Add(tile);
                
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
            
            // Notify LevelManager about start and end tiles
            var levelManager = FindObjectOfType<LevelManager>();
            if (levelManager != null && startTile != null && endTile != null)
            {
                levelManager.SetStartAndEndTiles(startTile, endTile);
                Debug.Log($"Set start tile {startTile.name} and end tile {endTile.name} on LevelManager");
            }
            
            Debug.Log($"Level başarıyla oluşturuldu! {tiles.Count} tile yerleştirildi.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Level oluşturma hatası: {e.Message}\n{e.StackTrace}");
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
        Debug.Log("Çıkmaz sokaklar planlanıyor...");
        
        int deadEndCount = Mathf.RoundToInt(Mathf.Lerp(2, 15, (difficultyRating - 1) / 9f));
        int placedDeadEnds = 0;
        int maxAttempts = deadEndCount * 4;
        int attempts = 0;
        
        while (placedDeadEnds < deadEndCount && attempts < maxAttempts)
        {
            attempts++;
            
            // Rastgele bir konum seç
            int x = Random.Range(1, width - 1);
            int y = Random.Range(1, height - 1);
            Vector2Int pos = new Vector2Int(x, y);
            
            // Skip start, end, and unplanned positions
            if (pos == startPos || pos == endPos || !planningGrid.ContainsKey(pos))
                continue;
                
            // Check if we can add a dead end
            List<int> directions = planningGrid[pos];
            bool canAddDeadEnd = directions.Count >= 3;
            
            if (difficultyRating > 6 && directions.Count >= 2)
                canAddDeadEnd = true;
                
            if (canAddDeadEnd)
            {
                // Choose a random direction
                int randomDirIndex = Random.Range(0, directions.Count);
                int deadEndDir = directions[randomDirIndex];
                
                // Calculate neighbor position
                Vector2Int neighborPos = GetNeighborPosition(pos, deadEndDir);
                
                // Check if neighbor is valid and unplanned
                if (InBounds(neighborPos) && !planningGrid.ContainsKey(neighborPos))
                {
                    // Add dead end to planning grid
                    int oppositeDir = GetOppositeDirection(deadEndDir);
                    planningGrid[neighborPos] = new List<int> { oppositeDir };
                    placedDeadEnds++;
                }
            }
        }
        
        Debug.Log($"Toplam {placedDeadEnds} çıkmaz sokak planlandı.");
    }
    
    // Plan remaining tiles without instantiating
    private void PlanRemainingTiles(Dictionary<Vector2Int, List<int>> planningGrid)
    {
        // Zorluk seviyesine göre tile dağılımını ayarla
        float emptyChance = Mathf.Lerp(0.4f, 0.1f, (difficultyRating - 1) / 9f);
        float straightChance = Mathf.Lerp(0.3f, 0.3f, (difficultyRating - 1) / 9f);
        float cornerChance = Mathf.Lerp(0.2f, 0.4f, (difficultyRating - 1) / 9f);
        float tChance = Mathf.Lerp(0.1f, 0.18f, (difficultyRating - 1) / 9f);
        float crossChance = Mathf.Lerp(0.0f, 0.02f, (difficultyRating - 1) / 9f);
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var pos = new Vector2Int(x, y);
                
                // Skip already planned positions
                if (planningGrid.ContainsKey(pos))
                    continue;

                float r = Random.value;
                List<int> dirs = new List<int>();
                
                // Determine directions based on random value
                if (r < emptyChance)
                {
                    // Empty tile - no directions
                }
                else if (r < emptyChance + straightChance)
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
                else if (r < emptyChance + straightChance + cornerChance)
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
                else if (r < emptyChance + straightChance + cornerChance + tChance)
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
        }
    }

    private void SetDifficultyParameters()
    {
        // Zorluk seviyesine göre parametreleri ayarla (1-10 ölçeği)
        deadEndProbability = Mathf.Lerp(0.05f, 0.6f, (difficultyRating - 1) / 9f);          // Çıkmaz sokak olasılığını arttır
        misleadingPathProbability = Mathf.Lerp(0.1f, 0.8f, (difficultyRating - 1) / 9f);    // Yanıltıcı yol olasılığını arttır
        maxPathLength = Mathf.RoundToInt(Mathf.Lerp(width + height, (width + height) * 2.5f, (difficultyRating - 1) / 9f));
        complexityFactor = Mathf.Lerp(1f, 3.5f, (difficultyRating - 1) / 9f);
        
        Debug.Log($"Zorluk parametreleri - Çıkmaz sokak: {deadEndProbability:F2}, " +
                  $"Yanıltıcı yollar: {misleadingPathProbability:F2}, " +
                  $"Maksimum yol uzunluğu: {maxPathLength}, " +
                  $"Karmaşıklık faktörü: {complexityFactor:F2}");
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

    public bool GenerateTiles()
    {
        try
        {
            Debug.Log("Level oluşturma başlıyor...");
            
            // Get or create the container
            container = GetOrCreateContainer();
            
            // Initialize the grid
            grid = new Tile[height, width];

            // Start ve End pozisyonlarını belirle (güvenli bir şekilde)
            startPos = new Vector2Int(
                Mathf.Clamp(width / 4, 1, width - 2),
                Mathf.Clamp(1, 1, height / 3)
            );

            endPos = new Vector2Int(
                Mathf.Clamp((3 * width) / 4, 2, width - 2),
                Mathf.Clamp(height - 2, height * 2/3, height - 2)
            );

            // Pozisyonların çakışmadığından emin ol
            if (Vector2Int.Distance(startPos, endPos) < 3)
            {
                Debug.LogWarning("Start ve End pozisyonları çok yakın! Ayarlanıyor...");
                endPos.x = Mathf.Clamp(startPos.x + 3, 0, width - 1);
                endPos.y = Mathf.Clamp(startPos.y + 3, 0, height - 1);
            }

            Debug.Log($"Start pozisyonu: {startPos}, End pozisyonu: {endPos}");

            // Add the start and end tiles to the grid
            if (startTile != null)
            {
                grid[startPos.y, startPos.x] = startTile;
            }
            
            if (endTile != null)
            {
                grid[endPos.y, endPos.x] = endTile;
            }
            
            // 4. Ana yolu oluştur
            var mainPath = BuildPath(startPos, endPos);
            if (mainPath == null || mainPath.Count < 2)
            {
                Debug.LogError("Ana yol oluşturulamadı!");
                return false;
            }
            
            // Yanıltıcı ve çıkmaz yollar oluştur (zorluk seviyesine göre)
            var allPaths = new List<List<Vector2Int>> { mainPath };
            GenerateDeceptivePaths(allPaths, mainPath);

            // 5. Tüm yolları yerleştir
            PlacePaths(allPaths);

            // 6. Çıkmaz sokaklar ekle
            if (difficultyRating > 3)
            {
                AddDeadEnds();
            }

            // 7. Kalan boşlukları doldur
            FillRemainingTiles();

            // 8. Komşulukları ata
            AssignNeighbors();

            Debug.Log("Level başarıyla oluşturuldu!");
            
            // Notify LevelManager in both editor and play mode
            var levelManager = FindObjectOfType<LevelManager>();
            if (levelManager != null && startTile != null && endTile != null)
            {
                levelManager.SetStartAndEndTiles(startTile, endTile);
            }
            else if (Application.isPlaying && EventManager.LevelGenerated != null)
            {
                EventManager.LevelGenerated.Invoke();
            }
            
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Level oluşturma hatası: {e.Message}\n{e.StackTrace}");
            // Temizlik yap
            if (container != null)
            {
                SafeDestroy(container.gameObject);
                container = null;
            }
            return false;
        }
    }

    private void GenerateDeceptivePaths(List<List<Vector2Int>> allPaths, List<Vector2Int> mainPath)
    {
        // Zorluk seviyesine göre yanıltıcı yol sayısını belirle
        int deceptivePathCount = Mathf.RoundToInt(Mathf.Lerp(1, 5, (difficultyRating - 1) / 9f));
        deceptivePathCount = Mathf.Min(deceptivePathCount, mainPath.Count - 2);
        
        Debug.Log($"Yanıltıcı yol sayısı: {deceptivePathCount}");
        
        // Ana yoldan başlangıç noktalardan yanıltıcı yollar türet
        HashSet<int> usedIndices = new HashSet<int>();
        
        for (int i = 0; i < deceptivePathCount; i++)
        {
            // Ana yoldan başlangıç noktası seç (start ve end hariç)
            int startIndex;
            do {
                startIndex = Random.Range(1, mainPath.Count - 1);
            } while (usedIndices.Contains(startIndex));
            
            usedIndices.Add(startIndex);
            Vector2Int branchStart = mainPath[startIndex];
            
            // Yanıltıcı hedef belirle (grid sınırları içinde)
            Vector2Int fakeTarget = GetRandomFakeTarget(branchStart, mainPath);
            
            // Yanıltıcı yol oluştur
            List<Vector2Int> fakePath = BuildDeceptivePath(branchStart, fakeTarget);
            if (fakePath.Count > 3) // Yeterince uzunsa ekle
            {
                allPaths.Add(fakePath);
                
                // Zorluk yükseldikçe, bu yoldan başka çıkmaz yollar da oluştur
                if (difficultyRating > 7 && Random.value < misleadingPathProbability)
                {
                    int subBranchIndex = Random.Range(1, fakePath.Count - 1);
                    Vector2Int subBranchStart = fakePath[subBranchIndex];
                    Vector2Int subFakeTarget = GetRandomFakeTarget(subBranchStart, fakePath);
                    
                    List<Vector2Int> subFakePath = BuildDeceptivePath(subBranchStart, subFakeTarget);
                    if (subFakePath.Count > 2)
                    {
                        allPaths.Add(subFakePath);
                    }
                }
            }
        }
        
        Debug.Log($"Toplam oluşturulan yol sayısı: {allPaths.Count}");
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
        // Yanıltıcı yollar için path builder
        float windingFactor = Mathf.Lerp(0.3f, 0.8f, (difficultyRating - 1) / 9f);
        int minLength = Mathf.RoundToInt(Mathf.Lerp(3, 8, (difficultyRating - 1) / 9f));
        
        List<Vector2Int> path = new List<Vector2Int> { start };
        Vector2Int current = start;
        int maxIterations = maxPathLength;
        int iteration = 0;
        
        while (current != target && iteration < maxIterations)
        {
            // Doğrudan hedefe gitme olasılığı (zorluk arttıkça azalır)
            bool directPath = Random.value > windingFactor;
            Vector2Int next;
            
            if (directPath && path.Count > minLength)
            {
                // Hedefe doğru hareket et
                Vector2Int dir = new Vector2Int(
                    Mathf.Clamp(target.x - current.x, -1, 1),
                    Mathf.Clamp(target.y - current.y, -1, 1)
                );
                
                // Diagonal hareketi engelle (sadece x veya y)
                if (dir.x != 0 && dir.y != 0)
                {
                    if (Random.value < 0.5f)
                        dir.x = 0;
                    else
                        dir.y = 0;
                }
                
                next = current + dir;
            }
            else
            {
                // Rastgele bir yönde git (dolambaçlı yol)
                Vector2Int[] directions = new Vector2Int[] 
                {
                    Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left
                };
                
                // Rastgele yön seç
                Vector2Int dir = directions[Random.Range(0, directions.Length)];
                next = current + dir;
            }
            
            // Sınırları kontrol et
            if (InBounds(next) && !path.Contains(next))
            {
                path.Add(next);
                current = next;
            }
            
            iteration++;
        }
        
        // Hedef noktayı ekle (eğer ulaşılamadıysa)
        if (current != target && path.Count > 2)
        {
            path.Add(target);
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
        go.transform.rotation = rotation;
#else
        go = Instantiate(prefab,
            new Vector3(pos.x * spacing, 0, pos.y * spacing),
            rotation,
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
    
    private int GetOppositeDirection(int direction)
    {
        return direction <= 2 ? direction + 2 : direction - 2;
    }

    private Vector2Int[] GenerateStartEndPositions()
    {
        Vector2Int start, end;
        float cornerPreference = Mathf.Lerp(0.3f, 0.8f, (difficultyRating - 1) / 9f);
        
        // Start pozisyonu (alt kısımda)
        int startY = Mathf.RoundToInt(height * 0.2f);
        if (Random.value < cornerPreference)
        {
            start = new Vector2Int(
                Random.value < 0.5f ? Random.Range(0, width / 3) : Random.Range(2 * width / 3, width),
                Random.Range(0, startY)
            );
        }
        else
        {
            start = new Vector2Int(Random.Range(0, width), Random.Range(0, startY));
        }

        // End pozisyonu (üst kısımda)
        int endY = Mathf.RoundToInt(height * 0.8f);
        float minDist = Mathf.Lerp(width * 0.3f, width * 0.8f, (difficultyRating - 1) / 9f);

        do
        {
            if (Random.value < cornerPreference)
            {
                end = new Vector2Int(
                    Random.value < 0.5f ? Random.Range(0, width / 3) : Random.Range(2 * width / 3, width),
                    Random.Range(endY, height)
                );
            }
            else
            {
                end = new Vector2Int(Random.Range(0, width), Random.Range(endY, height));
            }
        } while (Vector2Int.Distance(start, end) < minDist);

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

    private void PlacePaths(List<List<Vector2Int>> paths)
    {
        HashSet<Vector2Int> usedTiles = new HashSet<Vector2Int>();

        foreach(var path in paths)
        {
            for (int i = 1; i < path.Count - 1; i++)
            {
                var prev = path[i - 1];
                var cur = path[i];
                var next = path[i + 1];

                if(!usedTiles.Contains(cur))
                {
                    // Gather required directions for this tile
                    var dirs = GatherDirections(prev, cur, next);
                    
                    // Choose appropriate prefab based on directions
                    var prefab = ChoosePrefab(dirs);
                    
                    // Place the tile with appropriate rotation
                    PlaceOrientedTile(cur, prefab, dirs);
                    usedTiles.Add(cur);
                }
                else
                {
                    // Tile already exists, we would need to merge connections
                    // This is complex to handle with prefab rotations
                    // For now, we'll skip these tiles to avoid conflicts
                    Debug.Log($"Skipping tile at {cur} as it's already placed");
                }
            }
        }
    }
    
    // New method to place a tile with appropriate rotation without modifying openDirections
    private void PlaceOrientedTile(Vector2Int pos, GameObject prefab, List<int> desiredDirections)
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
        
        // Calculate rotation based on the desired directions
        float rotationAngle = 0f;
        
        if (prefab == straightPrefab)
        {
            // For straight tiles: determine if it's horizontal or vertical
            bool isVertical = desiredDirections.Contains(1) || desiredDirections.Contains(3);
            rotationAngle = isVertical ? 0f : 90f;
        }
        else if (prefab == cornerPrefab)
        {
            // For corner tiles: determine rotation based on which directions are desired
            if (desiredDirections.Contains(3) && desiredDirections.Contains(4))
            {
                rotationAngle = 0f;  // South-West
            }
            else if (desiredDirections.Contains(3) && desiredDirections.Contains(2))
            {
                rotationAngle = 90f; // South-East
            }
            else if (desiredDirections.Contains(1) && desiredDirections.Contains(2))
            {
                rotationAngle = 180f; // North-East
            }
            else if (desiredDirections.Contains(1) && desiredDirections.Contains(4))
            {
                rotationAngle = 270f; // North-West
            }
        }
        else if (prefab == tPrefab)
        {
            // For T tiles: determine which way the T is facing
            if (!desiredDirections.Contains(1))
            {
                rotationAngle = 0f;   // T facing South (open to E,S,W)
            }
            else if (!desiredDirections.Contains(2))
            {
                rotationAngle = 90f;  // T facing West (open to N,S,W)
            }
            else if (!desiredDirections.Contains(3))
            {
                rotationAngle = 180f; // T facing North (open to N,E,W)
            }
            else if (!desiredDirections.Contains(4))
            {
                rotationAngle = 270f; // T facing East (open to N,E,S)
            }
        }
        // Cross tiles don't need rotation
        
        // Create the tile with calculated rotation
        var rotation = Quaternion.Euler(0f, rotationAngle, 0f);
        GameObject go;
        
#if UNITY_EDITOR
        go = UnityEditor.PrefabUtility.InstantiatePrefab(prefab, container) as GameObject;
        go.transform.position = new Vector3(pos.x * spacing, 0, pos.y * spacing);
        go.transform.rotation = rotation;
#else
        go = Instantiate(prefab,
            new Vector3(pos.x * spacing, 0, pos.y * spacing),
            rotation,
            container);
#endif
        
        go.name = $"{prefab.name}_{pos.x}_{pos.y}";
        var tile = go.GetComponent<Tile>();
        
        // IMPORTANT: We don't modify openDirections - they come from the prefab
        
        // Add to grid and tiles list
        grid[pos.y, pos.x] = tile;
        if (!tiles.Contains(tile))
        {
            tiles.Add(tile);
        }
    }

    private void FillRemainingTiles()
    {
        // Zorluk seviyesine göre tile dağılımını ayarla
        float emptyChance = Mathf.Lerp(0.4f, 0.1f, (difficultyRating - 1) / 9f);
        float straightChance = Mathf.Lerp(0.3f, 0.3f, (difficultyRating - 1) / 9f); // Düz tile sabit
        float cornerChance = Mathf.Lerp(0.2f, 0.4f, (difficultyRating - 1) / 9f);  // Köşe tile'ları arttır
        float tChance = Mathf.Lerp(0.1f, 0.18f, (difficultyRating - 1) / 9f);      // T-tile'ları biraz azalt
        float crossChance = Mathf.Lerp(0.0f, 0.02f, (difficultyRating - 1) / 9f);  // Cross tile'ları ciddi şekilde azalt
        
        Debug.Log($"Tile dağılımı - Boş: {emptyChance:F2}, Düz: {straightChance:F2}, " +
                  $"Köşe: {cornerChance:F2}, T: {tChance:F2}, Artı: {crossChance:F2}");

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var pos = new Vector2Int(x, y);
                
                // Start ve End pozisyonlarını atla
                if (pos == startPos || pos == endPos || (grid[y, x] != null && grid[y, x].openDirections.Count > 0))
                    continue;

                float r = Random.value;
                GameObject selectedPrefab = null;
                
                if (r < emptyChance)
                {
                    selectedPrefab = emptyPrefab;
                }
                else if (r < emptyChance + straightChance)
                {
                    selectedPrefab = straightPrefab;
                }
                else if (r < emptyChance + straightChance + cornerChance)
                {
                    selectedPrefab = cornerPrefab;
                }
                else if (r < emptyChance + straightChance + cornerChance + tChance)
                {
                    selectedPrefab = tPrefab;
                }
                else
                {
                    selectedPrefab = crossPrefab;
                }
                
                // Tile yerleştir (openings ayarlamadan)
                PlaceTile(pos, selectedPrefab, new List<int>());
            }
        }
    }

    List<Vector2Int> BuildPath(Vector2Int start, Vector2Int end)
    {
        // Zorluk bazlı parametreler - daha zorlaştırıldı
        float directPathBias = Mathf.Lerp(0.8f, 0.1f, (difficultyRating - 1) / 9f);  // Kolay: 0.8, Zor: 0.1
        int minWaypoints = Mathf.RoundToInt(Mathf.Lerp(1, 8, (difficultyRating - 1) / 9f));  // Kolay: 1, Zor: 8
        int maxWaypoints = Mathf.RoundToInt(Mathf.Lerp(2, 15, (difficultyRating - 1) / 9f));  // Kolay: 2, Zor: 15
        
        List<Vector2Int> waypoints = new List<Vector2Int> { start };
        Vector2Int lastPoint = start;
        int waypointCount = Random.Range(minWaypoints, maxWaypoints + 1);
        
        Debug.Log($"Zorluk: {difficultyRating}, Waypoint Sayısı: {waypointCount}");

        // Zorluğa göre rotayı karmaşıklaştır
        // Zorluk yükseldikçe daha uzak ve zikzaklı yollar
        bool forceZigZag = difficultyRating > 6 && Random.value < 0.7f;
        
        for (int i = 0; i < waypointCount; i++)
        {
            Vector2Int waypoint;
            bool useDirectPath = Random.value < directPathBias;

            if (useDirectPath && !forceZigZag)
            {
                // Hedefe doğru daha direkt bir yol
                waypoint = new Vector2Int(
                    Mathf.RoundToInt(Mathf.Lerp(lastPoint.x, end.x, Random.Range(0.3f, 0.7f))),
                    Mathf.RoundToInt(Mathf.Lerp(lastPoint.y, end.y, Random.Range(0.3f, 0.7f)))
                );
            }
            else
            {
                // Daha karmaşık yol için uzak noktalar
                int maxOffset = Mathf.RoundToInt(Mathf.Lerp(width/4, width/2 * complexityFactor, (difficultyRating - 1) / 9f));
                
                // Zıtlayan yön (zigzag) kontrolü
                int xOffset, yOffset;
                
                if (forceZigZag && i > 0 && i < waypointCount - 1)
                {
                    // Önceki harekete göre ters yönde hareket
                    bool moveHorizontal = i % 2 == 0;
                    
                    if (moveHorizontal)
                    {
                        xOffset = Random.Range(maxOffset/2, maxOffset+1) * (Random.value < 0.5f ? 1 : -1);
                        yOffset = 0;
                    }
                    else
                    {
                        xOffset = 0;
                        yOffset = Random.Range(maxOffset/2, maxOffset+1) * (Random.value < 0.5f ? 1 : -1);
                    }
                }
                else
                {
                    // Normal rastgele hareket
                    xOffset = Random.Range(-maxOffset, maxOffset + 1);
                    yOffset = Random.Range(-maxOffset, maxOffset + 1);
                }
                
                // Zorluğa göre hedefe daha uzak noktalar tercih et
                if (difficultyRating > 5 && Vector2Int.Distance(lastPoint, end) < width / 2 && i < waypointCount - 2)
                {
                    // Hedeften uzaklaşan hareket
                    Vector2Int dirToEnd = new Vector2Int(
                        end.x - lastPoint.x > 0 ? 1 : -1,
                        end.y - lastPoint.y > 0 ? 1 : -1
                    );
                    
                    // Hedefe olan yönün tersine hareket
                    if (Random.value < 0.7f)
                    {
                        xOffset = Mathf.Abs(xOffset) * -dirToEnd.x;
                        yOffset = Mathf.Abs(yOffset) * -dirToEnd.y;
                    }
                }
                
                waypoint = new Vector2Int(
                    Mathf.Clamp(lastPoint.x + xOffset, 1, width - 2),
                    Mathf.Clamp(lastPoint.y + yOffset, 1, height - 2)
                );
            }

            // Yol üzerinde zaten var olan nokta mı kontrol et
            if (!waypoints.Contains(waypoint) && waypoint != end)
            {
                waypoints.Add(waypoint);
                lastPoint = waypoint;
            }
            
            // Zorlaştırma: Zorluk yüksekse ve son waypoint çok yakınsa, biraz daha uzat
            if (difficultyRating > 7 && i == waypointCount - 1 && Vector2Int.Distance(lastPoint, end) < 3)
            {
                // Son noktadan uzaklaş, yolu daha da uzat
                Vector2Int extraPoint = new Vector2Int(
                    Mathf.Clamp(lastPoint.x + Random.Range(-width/3, width/3+1), 1, width - 2),
                    Mathf.Clamp(lastPoint.y + Random.Range(-height/3, height/3+1), 1, height - 2)
                );
                
                if (!waypoints.Contains(extraPoint) && extraPoint != end)
                {
                    waypoints.Add(extraPoint);
                    lastPoint = extraPoint;
                }
            }
        }
        
        waypoints.Add(end);

        // Waypoint'ler arası yol oluşturma
        List<Vector2Int> finalPath = new List<Vector2Int>();
        finalPath.Add(start);

        for (int i = 1; i < waypoints.Count; i++)
        {
            // Zorluğa göre direkt yol olasılığını azalt
            float subpathDirectBias = Mathf.Max(0.1f, directPathBias - (difficultyRating / 20f));
            var subPath = FindSubPath(waypoints[i-1], waypoints[i], subpathDirectBias);
            finalPath.AddRange(subPath.Skip(1));
        }

        return finalPath;
    }
    
    private List<Vector2Int> FindSubPath(Vector2Int subStart, Vector2Int subEnd, float directPathBias)
    {
        // Zorluğa göre rastgele yön tercihi (daha karmaşık yollar için)
        bool preferRandomDirections = difficultyRating > 6 && Random.value > directPathBias;
        
        var queue = new Queue<Vector2Int>();
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        var visited = new HashSet<Vector2Int>();

        queue.Enqueue(subStart);
        visited.Add(subStart);
        cameFrom[subStart] = subStart; // Başlangıç noktasının kendisinden geldiğini işaretle

        Vector2Int[] directions = new Vector2Int[]
        {
            Vector2Int.up,    // Kuzey
            Vector2Int.right, // Doğu
            Vector2Int.down,  // Güney
            Vector2Int.left   // Batı
        };

        bool pathFound = false;
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (current == subEnd)
            {
                pathFound = true;
                break;
            }

            // Eğer zorluk yüksekse ve rastgele yön tercihi aktifse, her seferinde farklı yönleri tercih et
            Vector2Int[] shuffledDirections;
            
            if (preferRandomDirections)
            {
                // Hedeften uzaklaşma eğilimi
                Vector2Int dirToEnd = new Vector2Int(
                    Mathf.Clamp(subEnd.x - current.x, -1, 1),
                    Mathf.Clamp(subEnd.y - current.y, -1, 1)
                );
                
                // En uzak yönleri önce dene
                shuffledDirections = directions.OrderBy(d => 
                {
                    // Hedeften uzak yönlere öncelik ver
                    if (difficultyRating > 8 && Random.value < 0.4f)
                        return Vector2.Dot(new Vector2(d.x, d.y), new Vector2(dirToEnd.x, dirToEnd.y));
                    else
                        return Random.value;
                }).ToArray();
            }
            else
            {
                // Normal rastgele sıralama
                shuffledDirections = directions.OrderBy(d => Random.value).ToArray();
            }
            
            foreach (var direction in shuffledDirections)
            {
                var neighbor = current + direction;

                if (InBounds(neighbor) && !visited.Contains(neighbor))
                {
                    visited.Add(neighbor);
                    cameFrom[neighbor] = current;
                    queue.Enqueue(neighbor);
                }
            }
        }

        var path = new List<Vector2Int>();
        if (pathFound)
        {
            // Yolu geriye doğru oluştur
            var current = subEnd;
            while (current != subStart)
            {
                path.Add(current);
                current = cameFrom[current];
            }
            path.Add(subStart);
            path.Reverse(); // Baştan sona doğru sırala
        }
        else
        {
            Debug.LogError($"FindSubPath: {subStart} ve {subEnd} arasında yol bulunamadı!");
            // Acil durum: Direkt bir çizgi çizmeye çalış
            var emergencyPath = new List<Vector2Int>();
            Vector2Int tempCurrent = subStart;
            emergencyPath.Add(tempCurrent);
            while(tempCurrent != subEnd && emergencyPath.Count < (width + height) * 2)
            {
                if (tempCurrent.x < subEnd.x) tempCurrent.x++;
                else if (tempCurrent.x > subEnd.x) tempCurrent.x--;
                else if (tempCurrent.y < subEnd.y) tempCurrent.y++;
                else if (tempCurrent.y > subEnd.y) tempCurrent.y--;
                emergencyPath.Add(tempCurrent);
            }
            return emergencyPath;
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
            Debug.LogError($"Geçersiz pozisyon: {pos}");
            return;
        }

        // Start ve End pozisyonlarına tile yerleştirmeyi engelle
        if (pos == startPos || pos == endPos)
        {
            return;
        }

        try
        {
            // Mevcut tile'ı temizle (Start ve End hariç)
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

            // Eğer bu pozisyonda Start veya End tile varsa, yeni tile oluşturma
            if (grid[pos.y, pos.x] == startTile || grid[pos.y, pos.x] == endTile)
            {
                return;
            }

            // Yeni tile oluştur - prefab bağlantısını koruyarak
            GameObject go;
#if UNITY_EDITOR
            go = UnityEditor.PrefabUtility.InstantiatePrefab(prefab, container) as GameObject;
            go.transform.position = new Vector3(pos.x * spacing, 0, pos.y * spacing);
            go.transform.rotation = prefab.transform.rotation; // Use the prefab's original rotation
#else
            go = Instantiate(prefab,
                new Vector3(pos.x * spacing, 0, pos.y * spacing),
                prefab.transform.rotation, // Use the prefab's original rotation
                container);
#endif

            go.name = $"{prefab.name}_{pos.x}_{pos.y}";
            var tile = go.GetComponent<Tile>();

            // IMPORTANT: No longer modifying the openings/directions
            // The prefab's original openings will be preserved

            grid[pos.y, pos.x] = tile;
            
            // Add to tiles list
            if (!tiles.Contains(tile))
            {
                tiles.Add(tile);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Tile yerleştirme hatası: {e.Message}");
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
            
            Debug.Log("Level has been created and marked for saving. Please save the scene to preserve it.");
        }
    }
    #endif
}
