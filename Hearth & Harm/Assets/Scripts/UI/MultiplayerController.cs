using System.Collections.Generic; // <--- Add this line
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MultiplayerMenuController : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject connectionPanel; // The panel with Host/Join buttons
    [SerializeField] private GameObject charSelectPanel; // The panel with character buttons

    [Header("UI Elements")]
    [SerializeField] private TMP_InputField joinCodeInput;
    [SerializeField] private TextMeshProUGUI displayJoinCodeText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Button startButton;

    [Header("Character Selection")]
    [SerializeField] private List<Button> characterButtons;
    [SerializeField] private string gameSceneName = "GameScene";

    private void Start()
    {
        charSelectPanel.SetActive(false);
        connectionPanel.SetActive(true);

        if (startButton != null) {
            startButton.onClick.AddListener(OnStartClicked);
            startButton.interactable = false;
        }

        // Setup Character Buttons
        for (int i = 0; i < characterButtons.Count; i++) {
            int index = i; 
            characterButtons[i].onClick.AddListener(() => SelectCharacter(index));
        }

        if (NetworkBootstrapper.Instance != null) {
            NetworkBootstrapper.Instance.OnJoinCodeReady += (code) => {
                displayJoinCodeText.text = code;
                TransitionToCharSelect();
            };
            NetworkBootstrapper.Instance.OnConnected += TransitionToCharSelect;
        }
    }

    private void TransitionToCharSelect()
    {
        connectionPanel.SetActive(false);
        charSelectPanel.SetActive(true);
        UpdateStatus("Connected! Pick your character.");
        
        // Only host can see/press start
        if (startButton != null) 
            startButton.gameObject.SetActive(NetworkManager.Singleton.IsHost);
    }

    private void SelectCharacter(int index)
    {
        // 1. Save to the static class so we remember it locally
        CharacterSelection.Index = index;

        // 2. Sync it to the server immediately
        if (CharacterSelectionSync.Instance != null) {
            CharacterSelectionSync.Instance.SubmitCharacterIndex(index);
            UpdateStatus($"Selected Character {index}");
        }

        // Visual feedback: If Host, check if we can start (optional logic)
        if (NetworkManager.Singleton.IsHost) startButton.interactable = true;
    }

    public async void OnHostClicked() {
        GameManager.SetMode(GameMode.Host);
        await NetworkBootstrapper.Instance.HostGame();
    }

    public async void OnJoinClicked() {
        string code = joinCodeInput.text.Trim();
        if (code.Length < 6) return;
        GameManager.SetMode(GameMode.Client);
        await NetworkBootstrapper.Instance.JoinGame(code);
    }

    public void OnStartClicked() {
        if (!NetworkManager.Singleton.IsHost) return;
        NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
    }


    private void UpdateStatus(string msg)
        {
        if (statusText != null) 
        {
            statusText.text = msg;
        }
        
        if (!string.IsNullOrEmpty(msg)) 
        {
            Debug.Log($"[MultiplayerMenu] {msg}");
        }
    }
}
// // ── Helper Method ─────────────────────────────────────────────────────────

// private void UpdateStatus(string msg)
// {
//     if (statusText != null) 
//     {
//         statusText.text = msg;
//     }
    
//     if (!string.IsNullOrEmpty(msg)) 
//     {
//         Debug.Log($"[MultiplayerMenu] {msg}");
//     }
// }