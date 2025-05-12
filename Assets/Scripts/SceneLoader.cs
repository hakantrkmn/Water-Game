using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if(ES3.KeyExists("level"))
        {
            object level = ES3.Load("level");
            SceneManager.LoadScene(level.ToString());
        }
        else
        {
            ES3.Save("level", 1);
            SceneManager.LoadScene("1");
        }
    }
    // Update is called once per frame
    void Update()
    {
        
    }
}
