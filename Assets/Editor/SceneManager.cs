using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
using System.Collections;
using UnityEngine.SceneManagement;

// Remove the incorrect import and add the correct one
// using UnityEngine.SceneManagement;

public class CustomSceneManager : EditorWindow
{
    private string templateScenePath = "";
    private string outputFolderPath = "Assets/Scenes/Generated";
    private int sceneCounter = 1;
    private bool showSettings = true;
    
    // Level generator settings
    private bool showLevelSettings = true;
    private int levelWidth = 10;
    private int levelHeight = 10;
    private int levelDifficulty = 5;

    [MenuItem("Tools/Custom Scene Manager")]
    public static void ShowWindow()
    {
        GetWindow<CustomSceneManager>("Scene Manager");
    }

    private void OnGUI()
    {
        GUILayout.Label("Scene Creation Tool", EditorStyles.boldLabel);
        
        showSettings = EditorGUILayout.Foldout(showSettings, "Scene Settings");
        
        if (showSettings)
        {
            EditorGUILayout.BeginVertical("box");
            
            // Template scene selection
            EditorGUILayout.LabelField("Template Scene");
            EditorGUILayout.BeginHorizontal();
            templateScenePath = EditorGUILayout.TextField(templateScenePath);
            
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string path = EditorUtility.OpenFilePanel("Select Template Scene", "Assets", "unity");
                if (!string.IsNullOrEmpty(path))
                {
                    // Convert from absolute path to project relative path
                    templateScenePath = "Assets" + path.Substring(Application.dataPath.Length);
                }
            }
            EditorGUILayout.EndHorizontal();
            
            // Output folder selection
            EditorGUILayout.LabelField("Output Folder");
            EditorGUILayout.BeginHorizontal();
            outputFolderPath = EditorGUILayout.TextField(outputFolderPath);
            
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string path = EditorUtility.OpenFolderPanel("Select Output Folder", "Assets", "");
                if (!string.IsNullOrEmpty(path))
                {
                    // Convert from absolute path to project relative path
                    outputFolderPath = "Assets" + path.Substring(Application.dataPath.Length);
                }
            }
            EditorGUILayout.EndHorizontal();
            
            // Scene counter
            sceneCounter = EditorGUILayout.IntField("Next Scene Number", sceneCounter);
            
            EditorGUILayout.EndVertical();
        }
        
        // Level Generator Settings
        showLevelSettings = EditorGUILayout.Foldout(showLevelSettings, "Level Generator Settings");
        
        if (showLevelSettings)
        {
            EditorGUILayout.BeginVertical("box");
            
            // Width setting
            levelWidth = EditorGUILayout.IntSlider("Width", levelWidth, 5, 20);
            
            // Height setting
            levelHeight = EditorGUILayout.IntSlider("Height", levelHeight, 5, 20);
            
            // Difficulty setting
            levelDifficulty = EditorGUILayout.IntSlider("Difficulty", levelDifficulty, 1, 10);
            
            EditorGUILayout.EndVertical();
        }
        
        EditorGUILayout.Space(10);
        
        // Create scene button
        GUI.enabled = !string.IsNullOrEmpty(templateScenePath) && File.Exists(templateScenePath);
        if (GUILayout.Button("Create New Scene", GUILayout.Height(30)))
        {
            CreateNewScene();
        }
        
        if (!GUI.enabled)
        {
            EditorGUILayout.HelpBox("Please select a valid template scene file.", MessageType.Warning);
        }
        GUI.enabled = true;
        
        EditorGUILayout.Space(10);
        
        // Generate Level button
        if (GUILayout.Button("Generate Level", GUILayout.Height(30)))
        {
            GenerateLevel();
        }
    }
    
    private void CreateNewScene()
    {
        // Make sure the directory exists
        if (!Directory.Exists(outputFolderPath))
        {
            Directory.CreateDirectory(outputFolderPath);
        }
        
        // Generate the new scene path
        string newScenePath = $"{outputFolderPath}/Scene_{sceneCounter}.unity";
        
        // Make sure we don't overwrite an existing scene
        while (File.Exists(newScenePath))
        {
            sceneCounter++;
            newScenePath = $"{outputFolderPath}/Scene_{sceneCounter}.unity";
        }
        
        // Create the new scene by copying the template
        AssetDatabase.CopyAsset(templateScenePath, newScenePath);
        AssetDatabase.Refresh();
        
        Debug.Log($"Created new scene at: {newScenePath}");
        
        // Increment the counter for next time
        sceneCounter++;
        
        // Open the new scene
        EditorSceneManager.OpenScene(newScenePath);
    }
    
    private void GenerateLevel()
    {
        // Find objects with the LevelGenerator component by name
        GameObject levelGeneratorObject = GameObject.Find("LevelGenerator");
        if (levelGeneratorObject == null)
        {
            // Try to find any object with a LevelGenerator component
            MonoBehaviour[] allMonoBehaviours = FindObjectsOfType<MonoBehaviour>();
            foreach (MonoBehaviour mb in allMonoBehaviours)
            {
                if (mb.GetType().Name == "LevelGenerator")
                {
                    levelGeneratorObject = mb.gameObject;
                    break;
                }
            }
        }
        
        if (levelGeneratorObject != null)
        {
            // Get the component using reflection to avoid direct type reference
            Component levelGen = levelGeneratorObject.GetComponent("LevelGenerator");
            
            if (levelGen != null)
            {
                // Set the properties using reflection
                levelGen.GetType().GetField("width").SetValue(levelGen, levelWidth);
                levelGen.GetType().GetField("height").SetValue(levelGen, levelHeight);
                levelGen.GetType().GetField("difficultyRating").SetValue(levelGen, levelDifficulty);
                
                // Call the method to create a level
                Debug.Log($"Generating level with Width: {levelWidth}, Height: {levelHeight}, Difficulty: {levelDifficulty}");
                levelGen.SendMessage("CreateLevelInEditor", null, SendMessageOptions.DontRequireReceiver);
                
                // Mark the scene as dirty to ensure changes are saved
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                
                // Save the scene
                SaveCurrentScene();
            }
            else
            {
                // No LevelGenerator component found
                Debug.LogError("No LevelGenerator component found on the object.");
                EditorUtility.DisplayDialog("Error", "No LevelGenerator component found on the object.", "OK");
            }
        }
        else
        {
            // No LevelGenerator object found in the scene
            Debug.LogError("No LevelGenerator component found in the current scene. Make sure your template scene includes a LevelGenerator component.");
            EditorUtility.DisplayDialog("Error", "No LevelGenerator component found in the current scene. Make sure your template scene includes a LevelGenerator component.", "OK");
        }
    }
    
    // Method to save the current scene
    private void SaveCurrentScene()
    {
        Scene currentScene = EditorSceneManager.GetActiveScene();
        
        if (currentScene.isDirty)
        {
            // Save the scene
            string scenePath = currentScene.path;
            if (!string.IsNullOrEmpty(scenePath))
            {
                // Save the existing scene
                bool saved = EditorSceneManager.SaveScene(currentScene);
                if (saved)
                {
                    Debug.Log($"Scene saved successfully: {scenePath}");
                    EditorUtility.DisplayDialog("Success", $"Scene saved successfully: {scenePath}", "OK");
                }
                else
                {
                    Debug.LogError($"Failed to save scene: {scenePath}");
                    EditorUtility.DisplayDialog("Error", $"Failed to save scene: {scenePath}", "OK");
                }
            }
            else
            {
                // Scene hasn't been saved before, use SaveAs
                string suggestedPath = outputFolderPath + $"/Scene_{sceneCounter - 1}.unity";
                string newPath = EditorUtility.SaveFilePanel("Save Scene", Path.GetDirectoryName(suggestedPath), Path.GetFileName(suggestedPath), "unity");
                
                if (!string.IsNullOrEmpty(newPath))
                {
                    // Convert to project relative path if it's within the project
                    if (newPath.StartsWith(Application.dataPath))
                    {
                        newPath = "Assets" + newPath.Substring(Application.dataPath.Length);
                    }
                    
                    bool saved = EditorSceneManager.SaveScene(currentScene, newPath);
                    if (saved)
                    {
                        Debug.Log($"Scene saved successfully: {newPath}");
                        EditorUtility.DisplayDialog("Success", $"Scene saved successfully: {newPath}", "OK");
                    }
                    else
                    {
                        Debug.LogError($"Failed to save scene: {newPath}");
                        EditorUtility.DisplayDialog("Error", $"Failed to save scene: {newPath}", "OK");
                    }
                }
            }
        }
        else
        {
            Debug.Log("No changes to save in the current scene.");
        }
    }
}
