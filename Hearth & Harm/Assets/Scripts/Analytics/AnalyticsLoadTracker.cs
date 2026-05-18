using UnityEngine;
using UnityEngine.SceneManagement;
 
public class AnalyticsLoadTracker : MonoBehaviour
{
    private float sceneLoadStartTime;
 
    private bool IsWeb => Application.platform == RuntimePlatform.WebGLPlayer;
 
    private void OnEnable()
    {
        sceneLoadStartTime = Time.realtimeSinceStartup;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
 
    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
 
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        float seconds = Time.realtimeSinceStartup - sceneLoadStartTime;
 
        AnalyticsEvents.SceneLoadTime(scene.name, seconds, IsWeb);
 
        sceneLoadStartTime = Time.realtimeSinceStartup;
    }
}
