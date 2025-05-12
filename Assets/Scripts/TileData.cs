
using System.Collections.Generic;
using System.Diagnostics;

public class TileData
{
    public int type; // 0:empty, 1:straight, 2:corner, 3:t-shape, 4:cross, 5:start, 6:end
    public int rotation; // 0, 90, 180, 270 derece
    public List<int> openDirections = new List<int>(); // 1:kuzey, 2:doğu, 3:güney, 4:batı
    public bool isOnMainPath = false;
    public bool isDeadEnd = false;
    
    public TileData()
    {
        openDirections = new List<int>();
    }
    
    public TileData(int tileType, List<int> directions, int rot = 0)
    {
        type = tileType;
        openDirections = new List<int>(directions);
        rotation = rot;
    }
    
    // Tile yönlerini döndür
    public void Rotate(int degrees)
    {
        if (degrees == 0) return;
        
        rotation = (rotation + degrees) % 360;
        List<int> newDirections = new List<int>();
        
        foreach (int dir in openDirections)
        {
            // Her 90 derece için yönü bir adım kaydır (1->2->3->4->1)
            int steps = degrees / 90;
            int newDir = ((dir - 1 + steps) % 4) + 1;
            newDirections.Add(newDir);
        }
        
        openDirections = newDirections;
    }
    
    // Tile tipi ve yönlerine göre prefab seçimi
    public static TileData CreateFromType(int tileType)
    {
        TileData data = new TileData();
        data.type = tileType;
        
        switch (tileType)
        {
            case 0: // Empty
                data.openDirections = new List<int>();
                break;
                
            case 1: // Straight
                data.openDirections = new List<int> { 1, 3 }; // North-South
                break;
                
            case 2: // Corner
                data.openDirections = new List<int> { 1, 2 }; // North-East
                break;
                
            case 3: // T-shape
                data.openDirections = new List<int> { 1, 2, 3 }; // North-East-South
                break;
                
            case 4: // Cross
                data.openDirections = new List<int> { 1, 2, 3, 4 }; // All directions
                break;
                
            case 5: // Start
                data.openDirections = new List<int> { 1 }; // Default North
                break;
                
            case 6: // End
                data.openDirections = new List<int> { 3 }; // Default South
                break;
                
            default:
                break;
        }
        
        return data;
    }
}
