using System.Collections.Generic;
using UnityEngine;

public class TileAnimationController : MonoBehaviour
{
    public List<Animator> animators;
    public List<SkinnedMeshRenderer> meshRenderers;
    public string[] waterAnimationNames = new string[] { "Floor", "Snake", "HipHop" ,"Victory"};
    public string[] idleAnimationNames = new string[] { "Angry", "Bored", "Dizzy", "Praying","Sad"};
    public void HaveWater()
    {
        foreach (Animator animator in animators)
        {
            animator.CrossFade(waterAnimationNames[Random.Range(0, waterAnimationNames.Length)], 0.2f);
        }
    }

    public void DontHaveWater()
    {
        foreach (Animator animator in animators)
        {
            animator.CrossFade(idleAnimationNames[Random.Range(0, idleAnimationNames.Length)], 0.2f);
        }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
