// using UnityEngine;
// using UnityEngine.UI;
// using TMPro;

// /// <summary>
// /// Lose screen UI. Call LoseScreen.Show() to display it.
// /// Wired to GameStateManager for restart / quit.
// ///
// /// Setup:
// ///   1. Create a Canvas child called "LoseScreen" and attach this component.
// ///   2. Assign the panel and button references in the Inspector.
// ///   3. The panel should default to inactive (disabled) in the scene.
// /// </summary>
// public class LoseScreen : MonoBehaviour
// {
//     // ── Singleton-lite (scene-scoped) ──────────────────────────────────────
//     private static LoseScreen _instance;

//     [Header("UI References")]
//     [SerializeField] private GameObject panel;          // root panel to show/hide
//     [SerializeField] private TextMeshProUGUI levelText; // "You reached Level X"
//     [SerializeField] private Button restartButton;
//     [SerializeField] private Button quitButton;

//     private void Awake()
//     {
//         _instance = this;
//         if (panel != null) panel.SetActive(false);
//     }

//     private void OnEnable()
//     {
//         if (restartButton != null) restartButton.onClick.AddListener(OnRestart);
//         if (quitButton    != null) quitButton.onClick.AddListener(OnQuit);
//     }

//     private void OnDisable()
//     {
//         if (restartButton != null) restartButton.onClick.RemoveListener(OnRestart);
//         if (quitButton    != null) quitButton.onClick.RemoveListener(OnQuit);
//     }

//     // ── Static entry point (called by GameStateManager) ────────────────────
//     public static void Show()
//     {
//         if (_instance == null)
//         {
//             Debug.LogWarning("[LoseScreen] No LoseScreen instance in scene.");
//             return;
//         }
//         _instance.Display();
//     }

//     private void Display()
//     {
//         // Update flavour text
//         if (levelText != null && WaveManager.Instance != null)
//             levelText.text = $"You reached Level {WaveManager.Instance.CurrentLevel}\n" +
//                              $"({WaveManager.Instance.StagesCleared} stages cleared)";

//         Time.timeScale = 0f;
//         if (panel != null) panel.SetActive(true);
//     }

//     private void OnRestart()
//     {
//         if (panel != null) panel.SetActive(false);
//         // resetProgress = true resets WaveManager back to Level 1
//         GameStateManager.Instance?.RestartGame(resetProgress: true);
//     }

//     private void OnQuit()
//     {
// #if UNITY_EDITOR
//         UnityEditor.EditorApplication.isPlaying = false;
// #else
//         Application.Quit();
// #endif
//     }
// }