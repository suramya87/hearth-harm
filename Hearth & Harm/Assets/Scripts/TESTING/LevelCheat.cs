using UnityEngine;
using UnityEngine.SceneManagement;

public class LevelCheats : MonoBehaviour
{
    private void Update()
    {
        // Press 1 to restart scene as Character Index 0
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            RestartWithCharacter(0);
        }

        // Press 2 to restart scene as Character Index 1
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            RestartWithCharacter(1);
        }
    }

    private void RestartWithCharacter(int index)
    {
        Debug.Log($"[Cheat] Restarting scene with Character Index: {index}");

        // 1. Update the static variable that LevelGenerator.SpawnPlayer reads
        CharacterSelection.Index = index;

        // 2. Reload the currently active scene
        // This completely wipes the slate, resets all Managers, and re-runs Start()
        string currentSceneName = SceneManager.GetActiveScene().name;
        SceneManager.LoadScene(currentSceneName);
    }
}