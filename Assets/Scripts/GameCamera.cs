using UnityEngine;
using Unity.Cinemachine;
using System.Threading.Tasks;
using System;

[RequireComponent(typeof(CinemachineCamera))]
public class GameCamera : MonoBehaviour
{
    [Tooltip("CinemachineCamera bileşeni")]
    public CinemachineCamera cineCamera;
    [Tooltip("TileGenerator: tile’ların Transform’larını veren sınıf")]
    public LevelGenerator levelGenerator;
    [Tooltip("Izgara etrafındaki ekstra boşluk (world units)")]
    public float padding = 1f;
    [Tooltip("Kameranın minimum yükseklik ofseti (tile’ların geçmemesi için)")]
    public float minHeightOffset = 1f;
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
    
    
    void Start()
    {
        foreach (var tile in targetGroup.Targets)
        {
            targetGroup.RemoveMember(tile.Object);
        }
         
    }

    [ContextMenu("Update Camera")]
    public void UpdateCamera()
    {
        if (levelGenerator == null) return;

        // 1) Tile pozisyonlarından min/max X,Z bul
        var tiles = levelGenerator.GetAllTiles();
        Debug.Log("tiles: " + tiles.Length);
        if (targetGroup == null)
        {
            Debug.LogError("TargetGroup component not found on this GameObject.");
            return;
        }   


        // 2) TargetGroup'a tüm tile'ları ekleyin
        Debug.Log("tiles: " + tiles.Length);
        foreach (var tile in tiles)
        {
            targetGroup.AddMember(tile.transform, 1f, 0f);
        }
        
        

        
    }
}
