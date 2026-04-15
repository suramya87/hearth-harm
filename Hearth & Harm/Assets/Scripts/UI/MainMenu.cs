// using System.Collections;
// using System.Collections.Generic;
// using TMPro;
// using UnityEngine;
// using UnityEngine.UI;

// public class MainMenuController : MonoBehaviour
// {
//     [Header("Top Level Panels")]
//     [SerializeField] private GameObject mainMenuPanel;
//     [SerializeField] private GameObject modeSelectPanel; 
//     [SerializeField] private GameObject multiplayerJoinPanel; // Panel with the Join Code input
//     [SerializeField] private GameObject lobbyPanel;          // Panel where you see players/code
//     [SerializeField] private GameObject loadingPanel;

//     [Header("Multiplayer UI Elements")]
//     [SerializeField] private TMP_InputField joinCodeInput;
//     [SerializeField] private TextMeshProUGUI displayJoinCodeText; // Shows the code to the host
//     [SerializeField] private TextMeshProUGUI statusText;         // "Connecting...", "Failed", etc.
//     [SerializeField] private Button startAdventureButton;       // The final "Start Game" button

//     private bool isSinglePlayer = false;

//     private void Awake()
//     {
//         // Bind UI buttons to logic
//         // (Assuming you have these buttons on your panels)
//     }

//     private void Start()
//     {
//         ShowPanel(mainMenuPanel);

//         // Subscribe to Bootstrapper events so we know when the room is ready
//         if (NetworkBootstrapper.Instance != null)
//         {
//             NetworkBootstrapper.Instance.OnJoinCodeReady += HandleJoinCodeGenerated;
//             NetworkBootstrapper.Instance.OnConnectionFailed += HandleConnectionError;
//         }
//     }

//     private void OnDestroy()
//     {
//         if (NetworkBootstrapper.Instance != null)
//         {
//             NetworkBootstrapper.Instance.OnJoinCodeReady -= HandleJoinCodeGenerated;
//             NetworkBootstrapper.Instance.OnConnectionFailed -= HandleConnectionError;
//         }
//     }

//     // ─── Flow Logic ────────────────────────────────────────────────────────

//     public void OnPlayButtonClicked() => ShowPanel(modeSelectPanel);

//     public void StartSinglePlayer()
//     {
//         isSinglePlayer = true;
//         statusText.text = "Singleplayer Mode";
//         ShowPanel(lobbyPanel); 
        
//         // In SP, start button is always ready
//         if (startAdventureButton != null) startAdventureButton.interactable = true;
//         if (displayJoinCodeText != null) displayJoinCodeText.text = "OFFLINE";
//     }

//     public void OpenMultiplayerJoinMenu()
//     {
//         isSinglePlayer = false;
//         ShowPanel(multiplayerJoinPanel);
//     }

//     public async void OnHostGameClicked()
//     {
//         UpdateStatus("Creating Relay Session...");
//         await NetworkBootstrapper.Instance.HostGame();
//         // The HandleJoinCodeGenerated callback will trigger showing the lobbyPanel
//     }

//     public async void OnJoinGameClicked()
//     {
//         string code = joinCodeInput.text;
//         if (string.IsNullOrEmpty(code) || code.Length < 6)
//         {
//             UpdateStatus("Invalid Join Code");
//             return;
//         }

//         UpdateStatus("Joining " + code + "...");
//         await NetworkBootstrapper.Instance.JoinGame(code);
        
//         // If successful, show the lobby
//         ShowPanel(lobbyPanel);
//         if (displayJoinCodeText != null) displayJoinCodeText.text = code.ToUpper();
//     }

//     // ─── Event Handlers ────────────────────────────────────────────────────

//     private void HandleJoinCodeGenerated(string code)
//     {
//         ShowPanel(lobbyPanel);
//         if (displayJoinCodeText != null) displayJoinCodeText.text = code;
//         UpdateStatus("Room Ready");
//     }

//     private void HandleConnectionError(string error)
//     {
//         UpdateStatus("Error: " + error);
//         ShowPanel(multiplayerJoinPanel);
//     }

//     // ─── Helper Methods ────────────────────────────────────────────────────

//     private void ShowPanel(GameObject target)
//     {
//         mainMenuPanel.SetActive(false);
//         modeSelectPanel.SetActive(false);
//         multiplayerJoinPanel.SetActive(false);
//         lobbyPanel.SetActive(false);
//         loadingPanel.SetActive(false);

//         target.SetActive(true);
//     }

//     private void UpdateStatus(string msg)
//     {
//         if (statusText != null) statusText.text = msg;
//         Debug.Log($"[UI Status] {msg}");
//     }
// }