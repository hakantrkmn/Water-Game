using UnityEngine;
using Unity.Cinemachine;
using System.Threading.Tasks;
using System;
using System.Collections;

[RequireComponent(typeof(CinemachineCamera))]
public class GameCamera : MonoBehaviour
{
    [Tooltip("CinemachineCamera bileşeni")]
    public CinemachineCamera cineCamera;
    public CinemachineTargetGroup targetGroup;
    void Awake()
    {
        if (cineCamera == null)
            cineCamera = GetComponent<CinemachineCamera>();
    }

    private void OnEnable() {
        EventManager.LevelGenerated += OnLevelGenerated;
    }

    private void OnLevelGenerated()
    {
        UpdateCamera();
    }

    private void OnDisable() {
        EventManager.LevelGenerated -= OnLevelGenerated;
    }
    
    

    [ContextMenu("Update Camera")]
    public void UpdateCamera()
    {
        targetGroup = GameObject.FindAnyObjectByType<CinemachineTargetGroup>();

        targetGroup.Targets.Clear();

        // 1) Tile pozisyonlarından min/max X,Z bul
        Tile[] tiles = EventManager.GetAllTiles();
        if (targetGroup == null)
        {
            Debug.LogError("TargetGroup component not found on this GameObject.");
            return;
        }   
        // 2) TargetGroup'a tüm tile'ları ekleyin
        foreach (var tile in tiles)
        {
            targetGroup.AddMember(tile.transform, 1f, 0f);
        }
        
        

        
    }
}
