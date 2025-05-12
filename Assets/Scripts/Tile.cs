// Tile.cs
using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

// Yön numaralandırması:
// 1 = Kuzey (+Z)
// 2 = Doğu  (+X)
// 3 = Güney (-Z)
// 4 = Batı  (-X)
public class Tile : MonoBehaviour, IPointerDownHandler {

    public TileSettingScriptable tileSetting;
    [Header("Açık yönler (1=N(+Z), 2=E(+X), 3=S(-Z), 4=W(-X))")]
    public List<int> openDirections = new List<int>();
    public Transform waterTransform;
    public Tile[] neighbors = new Tile[4]; // index 0=N, 1=E, 2=S, 3=W

    [Header("Görsel Ayarlar")]
    public Material normalMaterial;  // Normal renk
    public Material flowMaterial;    // Su akışı rengi
    private MeshRenderer meshRenderer;
    public bool lastState = false;
    public bool hasWater = false;
    public List<MeshRenderer> wallRenderers = new List<MeshRenderer>();

/// <summary>
/// Start is called on the frame when a script is enabled just before
/// any of the Update methods is called the first time.
    /// </summary>
    void Start()
    {
        RotateRandomOnStart();
    }
    private void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();


        // Collider kontrolü
        var collider = GetComponent<Collider>();
        if (collider == null)
        {
            Debug.LogWarning($"Tile {gameObject.name} üzerinde Collider yok! BoxCollider ekleniyor.");
            gameObject.AddComponent<BoxCollider>();
        }
    }

    /// <summary>Tile'ın açık yönlerini ayarla.</summary>
    public void SetOpenings(IEnumerable<int> dirs) {
        openDirections.Clear();
        openDirections.AddRange(dirs);
        // Görsel veya collider ayarlarını da burada güncelleyebilirsin.
    }

    public Vector3 GetDirectionVector(int dir)
    {
        switch (dir)
        {
            case 1: return Vector3.forward;  // Kuzey (+Z)
            case 2: return Vector3.right;    // Doğu  (+X)
            case 3: return Vector3.back;     // Güney (-Z)
            case 4: return Vector3.left;     // Batı  (-X)
            default: return Vector3.zero;
        }
    }

    /// <summary>Bu yönden su alabilir mi?</summary>
    public bool CanFlowFrom(int dir) {
        // dir: 1..4
        return openDirections.Contains(dir);
    }

    /// <summary>Bu yönde su verebilir mi?</summary>
    public bool CanFlowTo(int dir) {
        return openDirections.Contains(dir);
    }

    /// <summary>Karşıt yönü verir (1<->3,2<->4).</summary>
    public static int Opposite(int dir) {
        switch (dir) {
            case 1: return 3; // Kuzey -> Güney
            case 2: return 4; // Doğu -> Batı
            case 3: return 1; // Güney -> Kuzey
            case 4: return 2; // Batı -> Doğu
            default: return 0;
        }
    }

    public void WaterAnimation(bool flowing)
    {
        if(DOTween.IsTweening(this))
        {
            DOTween.Complete(this);
        }
        if(flowing)
        {
            Debug.Log(waterTransform.GetComponent<MeshRenderer>().material.GetTextureOffset("_MainTex"));
        }
    }

    public void RotateRandomOnStart()
    {
        //rotate random 90 degrees or multiple of 90 degrees
        int random = UnityEngine.Random.Range(0, 4);
        for(int i = 0; i < random; i++)
        {
        transform.Rotate(0, 90, 0);
        List<int> newDirections = new List<int>();
        foreach (int dir in openDirections)
        {
            // 1(N)->2(E)->3(S)->4(W)->1(N)
            int newDir = dir + 1;
            if (newDir > 4) newDir = 1;
            newDirections.Add(newDir);
        }
        openDirections = newDirections;
        }

    }
    public void Rotate90Degrees(Action callback)
    {
        if (DOTween.IsTweening(this))
        {
            DOTween.Complete(this);
        }
        transform.DOScale(new Vector3(0.9f, 0.9f, 0.9f), 0.25f).SetLoops(2, LoopType.Yoyo).SetId(this);
        // Objeyi 90 derece döndür (saat yönünde)
        transform.DORotate(new Vector3(0, 90, 0), 0.5f, RotateMode.LocalAxisAdd).SetEase(tileSetting.rotateEase).SetId(this).OnComplete(() => 
        {
        //transform.Rotate(0, -90, 0); // Saat yönünde dönüş için -90

        // Açık yönleri güncelle (saat yönünde)
        List<int> newDirections = new List<int>();
        foreach (int dir in openDirections)
        {
            // 1(N)->2(E)->3(S)->4(W)->1(N)
            int newDir = dir + 1;
            if (newDir > 4) newDir = 1;
            newDirections.Add(newDir);
        }
        openDirections = newDirections;

        // Debug için yön bilgilerini göster
        Debug.Log($"Tile {gameObject.name} döndürüldü. Yeni yönler: {string.Join(",", openDirections)}");
        callback?.Invoke();
        });

    }

    public void SetWaterFlow(bool flowing, float delay = 0f)
    {
        hasWater = flowing;
        if(lastState == flowing) return;
        lastState = flowing;
        WaterAnimation(flowing);
        //set walls color to if flowing is false, F691D5, otherwise white
        waterTransform.DOLocalMoveY(flowing ? 0.4f : 0.2f, tileSetting.waterSpeed).SetEase(tileSetting.waterEase).SetDelay(delay);
        foreach(MeshRenderer wallRenderer in wallRenderers)
        {
            wallRenderer.material.DOColor(flowing ? new Color(1, 1, 1, 0.8f) : new Color(0.96f, 0.57f, 0.83f, 0.8f), 0.3f).SetEase(Ease.InOutSine);
        }
    }
    public void OnPointerDown(PointerEventData eventData)
    {
        Debug.Log($"Tile {gameObject.name} tıklandı!");
        Rotate90Degrees(() => 
        {
            // LevelManager'a rotasyon olduğunu bildir
            if (LevelManager.Instance != null)
            {
                LevelManager.Instance.OnTileRotated();
            }
        });
    }
 

    private void OnMouseEnter()
    {
        Debug.Log($"Mouse Tile {gameObject.name} üzerine geldi");
    }

    private void OnMouseExit()
    {
        Debug.Log($"Mouse Tile {gameObject.name} üzerinden ayrıldı");
    }

    private void OnDrawGizmos()
    {
        // Eğer boş tile ise (openDirections.Count == 0) gizmo çizme
        if (openDirections.Count == 0) return;

        // Ok başlangıç noktası (tile'ın ortası, biraz yukarıda)
        Vector3 center = transform.position + Vector3.up * 0.5f;
        
        // Ok rengi
        if (hasWater)
            Gizmos.color = new Color(0, 1, 0, 0.8f);  // Yarı saydam koyu yeşil
        else
            Gizmos.color = new Color(0, 0.8f, 1f, 0.8f);   // Yarı saydam mavi

        float arrowLength = 0.8f;        // Ok uzunluğu arttırıldı
        float arrowHeadLength = 0.3f;    // Ok başı uzunluğu arttırıldı
        float arrowHeadAngle = 30f;      // Ok başı açısı arttırıldı
        float arrowWidth = 0.1f;         // Ok kalınlığı

        foreach (int dir in openDirections)
        {
            Vector3 direction = GetDirectionVector(dir);
            
            // Ok gövdesini çiz
            Vector3 arrowEnd = center + direction * arrowLength;
            
            // Kalın ok gövdesi için paralel çizgiler
            Vector3 perpendicular = Vector3.Cross(direction, Vector3.up).normalized * arrowWidth;
            
            // Ok gövdesinin kenarları
            Gizmos.DrawLine(center + perpendicular, arrowEnd + perpendicular);
            Gizmos.DrawLine(center - perpendicular, arrowEnd - perpendicular);
            // Ok gövdesinin üst çizgisi
            Gizmos.DrawLine(center + perpendicular, center - perpendicular);
            // Ok gövdesinin uç çizgisi
            Gizmos.DrawLine(arrowEnd + perpendicular, arrowEnd - perpendicular);

            // Ok başlarını çiz (daha kalın)
            Vector3 right = Quaternion.LookRotation(direction) * Quaternion.Euler(0, arrowHeadAngle, 0) * Vector3.forward;
            Vector3 left = Quaternion.LookRotation(direction) * Quaternion.Euler(0, -arrowHeadAngle, 0) * Vector3.forward;

            // Ok başı için üçgen
            Gizmos.DrawLine(arrowEnd, arrowEnd - right * arrowHeadLength + perpendicular);
            Gizmos.DrawLine(arrowEnd, arrowEnd - left * arrowHeadLength - perpendicular);
            Gizmos.DrawLine(arrowEnd - right * arrowHeadLength + perpendicular, 
                          arrowEnd - left * arrowHeadLength - perpendicular);
        }

#if UNITY_EDITOR
        // Tile tipini göster (boş değilse)
        if (gameObject.name.Contains("Start") || gameObject.name.Contains("End"))
        {
            UnityEditor.Handles.Label(center + Vector3.up * 0.5f, 
                gameObject.name, 
                new GUIStyle() { fontSize = 14, fontStyle = FontStyle.Bold });
        }
#endif
    }

    private void OnDrawGizmosSelected()
    {
        // Seçili olduğunda komşu bağlantılarını göster
        Gizmos.color = Color.yellow;
        
        for (int i = 0; i < neighbors.Length; i++)
        {
            if (neighbors[i] != null)
            {
                Vector3 start = transform.position + Vector3.up * 0.5f;
                Vector3 end = neighbors[i].transform.position + Vector3.up * 0.5f;
                
                // Kalın bağlantı çizgisi
                float lineWidth = 0.05f;
                Vector3 perpendicular = Vector3.Cross((end - start).normalized, Vector3.up).normalized * lineWidth;
                
                Gizmos.DrawLine(start + perpendicular, end + perpendicular);
                Gizmos.DrawLine(start - perpendicular, end - perpendicular);

#if UNITY_EDITOR
                // Yön etiketini daha büyük göster
                string[] directions = { "N", "E", "S", "W" };
                Vector3 midPoint = (start + end) / 2f + Vector3.up * 0.2f;
                var style = new GUIStyle() { fontSize = 12, fontStyle = FontStyle.Bold };
                UnityEditor.Handles.Label(midPoint, directions[i], style);
#endif
            }
        }
    }
}