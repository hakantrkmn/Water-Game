using DG.Tweening;
using UnityEngine;

[CreateAssetMenu(fileName = "TileSetting", menuName = "ScriptableObjects/TileSetting", order = 1)]
public class TileSettingScriptable : ScriptableObject
{
    public float waterSpeed = 0.8f;
    public Ease waterEase = Ease.OutFlash;
    public Ease rotateEase = Ease.OutCubic;
}
