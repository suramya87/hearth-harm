// // MultiplayerMenuController.cs
// // ─────────────────────────────────────────────────────────────────────────────
// // Handles ONLY networking: host, join, char sync, scene load.
// // All UI transitions are delegated to MenuFlowController.
// // ─────────────────────────────────────────────────────────────────────────────

// using System.Collections;
// using TMPro;
// using Unity.Netcode;
// using UnityEngine;
// using UnityEngine.SceneManagement;
// using UnityEngine.UI;

// public class MultiplayerMenuController : MonoBehaviour
// {
//     public static MultiplayerMenuController Instance { get; private set; }

//     [Header("Connection UI")]
//     [SerializeField] private TMP_InputField  joinCodeInput;

//     [Header("Lobby UI")]
//     [SerializeField] private Button beginCharSelectButton; // host only, in lobby section
//     [SerializeField] private Button startButton;           // host only, in char select

//     [Header("Scene")]
//     [SerializeField] private string gameSceneName = "MultiplayerTest";

//     private void Awake()
//     {
//         if (Instance != null && Instance != this) { Destroy(gameObject); return; }
//         Instance = this;
//     }

//     private void Start()
//     {
//         beginCharSelectButton?.onClick.AddListener(OnBeginCharSelectClicked);
//         beginCharSelectButton?.gameObject.SetActive(false);

//         // startButton is wired in MenuFlowController but we expose it here
//         // so MenuFlowController can call OnStartClicked via Instance
//         startButton?.gameObject.SetActive(false);

//         StartCoroutine(SubscribeWhenReady());
//     }

//     private void OnDestroy()
//     {
//         if (NetworkBootstrapper.Instance == null) return;
//         NetworkBootstrapper.Instance.OnJoinCodeReady    -= OnJoinCodeReady;
//         NetworkBootstrapper.Instance.OnConnected        -= OnNetworkConnected;
//         NetworkBootstrapper.Instance.OnConnectionFailed -= OnConnectionFailed;
//     }

//     private IEnumerator SubscribeWhenReady()
//     {
//         while (NetworkBootstrapper.Instance == null) yield return null;
//         NetworkBootstrapper.Instance.OnJoinCodeReady    += OnJoinCodeReady;
//         NetworkBootstrapper.Instance.OnConnected        += OnNetworkConnected;
//         NetworkBootstrapper.Instance.OnConnectionFailed += OnConnectionFailed;
//     }

//     // ── Host / Join ────────────────────────────────────────────────────────

//     public async void OnHostClicked()
//     {
//         MenuFlowController.Instance?.SetStatus("Creating session...");
//         await NetworkBootstrapper.Instance.HostGame();
//     }

//     public async void OnJoinClicked()
//     {
//         string code = joinCodeInput != null ? joinCodeInput.text.Trim() : "";
//         if (string.IsNullOrEmpty(code) || code.Length < 6)
//         {
//             MenuFlowController.Instance?.SetStatus("Enter a valid 6-character code.");
//             return;
//         }
//         MenuFlowController.Instance?.SetStatus($"Joining {code.ToUpper()}...");
//         await NetworkBootstrapper.Instance.JoinGame(code);
//     }

//     public void OnDisconnectClicked()
//     {
//         NetworkBootstrapper.Instance?.Disconnect();
//         MenuFlowController.Instance?.OnBackClicked();
//     }

//     // ── Bootstrapper callbacks ─────────────────────────────────────────────

//     private void OnJoinCodeReady(string code)
//     {
//         MenuFlowController.Instance?.OnSessionCodeReady(code);
//     }

//     private void OnNetworkConnected()
//     {
//         MenuFlowController.Instance?.OnConnected();
//     }

//     private void OnConnectionFailed(string error)
//     {
//         MenuFlowController.Instance?.OnConnectionFailed(error);
//     }

//     // ── Lobby ──────────────────────────────────────────────────────────────

//     /// Called by MenuFlowController to show/hide the Begin Char Select button
//     public void SetBeginCharSelectVisible(bool show)
//     {
//         beginCharSelectButton?.gameObject.SetActive(show);
//     }

//     private void OnBeginCharSelectClicked()
//     {
//         if (!(NetworkManager.Singleton?.IsHost ?? false)) return;
//         beginCharSelectButton?.gameObject.SetActive(false);
//         // MenuFlowController broadcasts and transitions everyone
//         MenuFlowController.Instance?.TransitionToMPCharSelect();
//     }

//     // ── Character selection ────────────────────────────────────────────────

//     /// Called by MenuFlowController when a character button is pressed in MP flow
//     public void NotifyCharacterSelected(int index)
//     {
//         CharacterSelectionSync.Instance?.SubmitCharacterIndex(index);
//         // Mark this client ready automatically on character selection
//         if (LobbyNetwork.Instance != null && NetworkManager.Singleton != null)
//             LobbyNetwork.Instance.SetReadyServerRpc(
//                 NetworkManager.Singleton.LocalClientId, true);
//     }

//     // ── Start game ─────────────────────────────────────────────────────────

//     /// Called by MenuFlowController's mpStartButton
//     public void OnStartClicked()
//     {
//         if (!(NetworkManager.Singleton?.IsHost ?? false)) return;

//         MenuFlowController.Instance?.TransitionToLoading();

//         var status = NetworkManager.Singleton.SceneManager.LoadScene(
//             gameSceneName, LoadSceneMode.Single);

//         if (status != SceneEventProgressStatus.Started)
//         {
//             Debug.LogError($"[MPMenu] Scene load failed: {status}");
//             // Revert to char select
//             MenuFlowController.Instance?.OnBackClicked();
//         }
//     }
// }