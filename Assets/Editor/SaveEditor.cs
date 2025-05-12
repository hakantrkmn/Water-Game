using UnityEngine;
using UnityEditor;
using System.IO;

public class SaveEditor : EditorWindow
{
    [MenuItem("Save Settings/Open Window")]
    public static void ShowWindow()
    {
        GetWindow<SaveEditor>("Save Settings");
    }

    void OnGUI()
    {
        GUILayout.Label("Save Settings", EditorStyles.boldLabel);
        
        if (GUILayout.Button("Clear Save (Delete Keys)"))
        {
            if (EditorUtility.DisplayDialog("Clear Save Data", 
                "Are you sure you want to clear all save keys? This action cannot be undone.", 
                "Yes", "No"))
            {
                ClearES3SaveDataByKeys();
            }
        }
        
        if (GUILayout.Button("Clear Save (Delete File)"))
        {
            if (EditorUtility.DisplayDialog("Clear Save Data", 
                "Are you sure you want to delete the save file? This action cannot be undone.", 
                "Yes", "No"))
            {
                ClearES3SaveDataByFile();
            }
        }
    }
    
    private void ClearES3SaveDataByKeys()
    {
        try
        {
            // Get all keys and delete them one by one
            string[] keys = ES3.GetKeys();
            if (keys != null && keys.Length > 0)
            {
                foreach (string key in keys)
                {
                    ES3.DeleteKey(key);
                }
                Debug.Log("All ES3 save keys have been cleared successfully.");
            }
            else
            {
                Debug.Log("No ES3 save data found to clear.");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Error clearing ES3 save keys: " + e.Message);
        }
    }
    
    private void ClearES3SaveDataByFile()
    {
        try
        {
            // Delete the entire save file
            ES3.DeleteFile();
            Debug.Log("ES3 save file has been deleted successfully.");
        }
        catch (System.Exception e)
        {
            Debug.LogError("Error deleting ES3 save file: " + e.Message);
        }
    }
}
