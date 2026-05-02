using UnityEngine;
using UnityEngine.SceneManagement;

public class LevelCheats : MonoBehaviour
{
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            RestartWithCharacter(0);
        }

        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            RestartWithCharacter(1);
        }
    }

    private void RestartWithCharacter(int index)
    {
        Debug.Log($"[Cheat] Restarting scene with Character Index: {index}");

        CharacterSelection.Index = index;

        string currentSceneName = SceneManager.GetActiveScene().name;
        SceneManager.LoadScene(currentSceneName);
    }
}