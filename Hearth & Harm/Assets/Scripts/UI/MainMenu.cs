using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class MainMenuController : MonoBehaviour
{
    [Header("Scenes")]
    [SerializeField] private string singlePlayerScene = "SinglePlayerScene";
    [SerializeField] private string multiplayerScene  = "MultiplayerScene";

    [Header("Panels")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject charSelectPanel;

    [Header("Character Selection UI")]
    [SerializeField] private List<Button> characterButtons;
    [SerializeField] private TextMeshProUGUI selectedNameText;
    [SerializeField] private string[] characterNames = { "Warrior", "Mage", "Rogue" };

    private void Start()
    {
        // Ensure we start on the main panel
        mainPanel.SetActive(true);
        charSelectPanel.SetActive(false);

        // Setup character buttons for single player
        for (int i = 0; i < characterButtons.Count; i++)
        {
            int index = i;
            characterButtons[i].onClick.AddListener(() => SelectCharacter(index));
        }
    }

    // --- Navigation ---

    public void OnSinglePlayerClicked()
    {
        // Instead of loading the scene, show the character select panel
        mainPanel.SetActive(false);
        charSelectPanel.SetActive(true);
        UpdateSelectionVisuals(0); // Default to first character
    }

    public void OnMultiplayerClicked()
    {
        SceneManager.LoadScene(multiplayerScene);
    }

    public void OnBackToMainClicked()
    {
        charSelectPanel.SetActive(false);
        mainPanel.SetActive(true);
    }

    // --- Character Selection ---

    private void SelectCharacter(int index)
    {
        // Set the static index that your Spawner script looks for
        CharacterSelection.Index = index;
        
        UpdateSelectionVisuals(index);
    }

    private void UpdateSelectionVisuals(int index)
    {
        if (selectedNameText != null && index < characterNames.Length)
        {
            selectedNameText.text = characterNames[index];
        }
        
        // You could add code here to highlight the button or show a preview model
    }

    public void OnStartGameClicked()
    {
        // Set mode to offline and go!
        GameManager.SetMode(GameMode.Offline);
        SceneManager.LoadScene(singlePlayerScene);
    }
}