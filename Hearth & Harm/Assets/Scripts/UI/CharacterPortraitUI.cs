using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Updates a portrait Image based on whichever character the player selected
/// in the main menu (CharacterSelection.Index).
/// </summary>
public class CharacterPortraitUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The UI Image to update. Auto-assigned if left empty.")]
    [SerializeField] private Image portraitImage;

    [Header("Portraits")]
    [Tooltip("One sprite per character, in the same order as MainMenuController's character buttons.")]
    [SerializeField] private List<Sprite> portraits = new List<Sprite>();

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (portraitImage == null)
            portraitImage = GetComponent<Image>();
    }

    private void Start()
    {
        RefreshPortrait();
    }

    // ── Portrait update ────────────────────────────────────────────────────

    private void RefreshPortrait()
    {
        if (portraitImage == null) return;
        if (portraits == null || portraits.Count == 0) return;

        int index = CharacterSelection.Index;

        if (index < 0 || index >= portraits.Count)
        {
            Debug.LogWarning($"[CharacterPortraitUI] Index {index} out of range ({portraits.Count} portraits assigned).");
            return;
        }

        Sprite portrait = portraits[index];
        if (portrait != null)
            portraitImage.sprite = portrait;
    }
}