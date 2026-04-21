// using UnityEngine;
// using UnityEngine.UI;
// using TMPro;

// /// <summary>
// /// Win screen shown when the player clears a stage (reaches the End room / beats the boss).
// /// Call WinScreen.Show() from wherever you detect the win condition
// /// (e.g. EndRoomTrigger, BossDeathHandler, etc.).
// ///
// /// "Next Level" advances WaveManager, regenerates the level with MORE rooms.
// /// "Restart"   resets everything back to Level 1.
// /// "Quit"      exits the application.
// ///
// /// Setup:
// ///   1. Create a Canvas child called "WinScreen" and attach this component.
// ///   2. Assign panel + button references in the Inspector.
// ///   3. Panel should default to inactive in the scene.
// /// </summary>
// public class WinScreen : MonoBehaviour
// {
//     private static WinScreen _instance;

//     [Header("UI References")]
//     [SerializeField] private GameObject      panel;
//     [SerializeField] private TextMeshProUGUI titleText;      // "Stage Clear!" / "You Win!"
//     [SerializeField] private TextMeshProUGUI statsText;      // level + rooms info
//     [SerializeField] private TextMeshProUGUI nextLevelText;  // "Next: Level X (Y–Z rooms)"
//     [SerializeField] private Button          nextLevelButton;
//     [SerializeField] private Button          restartButton;
//     [SerializeField] private Button          quitButton;

//     private void Awake()
//     {
//         _instance = this;
//         if (panel != null) panel.SetActive(false);
//     }

//     private void OnEnable()
//     {
//         if (nextLevelButton != null) nextLevelButton.onClick.AddListener(OnNextLevel);
//         if (restartButton   != null) restartButton.onClick.AddListener(OnRestart);
//         if (quitButton      != null) quitButton.onClick.AddListener(OnQuit);
//     }

//     private void OnDisable()
//     {
//         if (nextLevelButton != null) nextLevelButton.onClick.RemoveListener(OnNextLevel);
//         if (restartButton   != null) restartButton.onClick.RemoveListener(OnRestart);
//         if (quitButton      != null) quitButton.onClick.RemoveListener(OnQuit);
//     }

//     // ── Static entry point ─────────────────────────────────────────────────
//     public static void Show()
//     {
//         if (_instance == null)
//         {
//             Debug.LogWarning("[WinScreen] No WinScreen instance in scene.");
//             return;
//         }
//         _instance.Display();
//     }

//     private void Display()
//     {
//         var wm = WaveManager.Instance;

//         if (titleText != null)
//             titleText.text = wm != null && wm.CurrentLevel >= 10
//                 ? "YOU WIN!" : "Stage Clear!";

//         if (statsText != null && wm != null)
//             statsText.text = $"Level {wm.CurrentLevel} complete\n" +
//                              $"Total stages cleared: {wm.StagesCleared}";

//         // Preview what comes next (WaveManager hasn't advanced yet here)
//         if (nextLevelText != null && wm != null)
//         {
//             int nextMin = Mathf.Min(wm.GetMinRooms() + 1, 20);   // +1 preview for next level
//             int nextMax = Mathf.Min(wm.GetMaxRooms() + 1, 20);
//             nextLevelText.text = $"Next: Level {wm.CurrentLevel + 1}  ({nextMin}–{nextMax} rooms)";
//         }

//         Time.timeScale = 0f;
//         if (panel != null) panel.SetActive(true);
//     }

//     // ── Buttons ────────────────────────────────────────────────────────────

//     private void OnNextLevel()
//     {
//         if (panel != null) panel.SetActive(false);

//         // Advance WaveManager FIRST so LevelGenerator picks up new room counts
//         WaveManager.Instance?.AdvanceLevel();

//         // Tell GameStateManager a level was advanced (resets its state + fires OnGameRestarted)
//         GameStateManager.Instance?.NotifyLevelAdvanced();

//         // Regenerate the level (more rooms, more enemies)
//         var gen = FindAnyObjectByType<LevelGenerator>();
//         if (gen != null) gen.GenerateLevel();
//     }

//     private void OnRestart()
//     {
//         if (panel != null) panel.SetActive(false);
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