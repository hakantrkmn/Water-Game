using System;
using TMPro;
using UnityEngine;
using DG.Tweening;
using UnityEngine.SceneManagement;
public class UIController : MonoBehaviour
{
    public TextMeshProUGUI levelText;
    public CanvasGroup levelCompletedCanvasGroup;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        levelText.text = ES3.Load("level").ToString();
    }
    private void OnEnable()
    {
        EventManager.LevelCompleted += OnLevelCompleted;
    }

    private void OnLevelCompleted()
    {
        DOTween.To(() => levelCompletedCanvasGroup.alpha, x => levelCompletedCanvasGroup.alpha = x, 1, 1f);
        levelCompletedCanvasGroup.blocksRaycasts = true;
        levelCompletedCanvasGroup.interactable = true;

        int nextLevel = (int)ES3.Load("level") + 1;
        if(nextLevel > SceneManager.sceneCountInBuildSettings - 1 )
        {
            nextLevel = 1;
        }
        ES3.Save("level", nextLevel);
    }

    private void OnDisable()
    {
        EventManager.LevelCompleted -= OnLevelCompleted;
    }
    public void OnNextLevelButtonClicked()
    {
        SceneManager.LoadScene("Scene_" + ES3.Load("level").ToString());
    }
}
