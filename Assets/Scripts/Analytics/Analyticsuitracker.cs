// ─────────────────────────────────────────────────────────────────────────────
// AnalyticsUITracker.cs  (UI / Canvas — implements IPointerClickHandler)
//
// SETUP
//   1. Attach to any UI GameObject (Button, Panel, Image, etc.).
//   2. Your Canvas must have a GraphicRaycaster and EventSystem in the scene
//      (they're already there if you have any UI working).
//   3. The GameObject needs a Graphic component (Image, Text, etc.) with
//      Raycast Target = true so clicks register.
//   4. Set the label and track type in the Inspector.
//
//   For CHARACTER BUTTONS:
//     - Set trackType = CharacterSelect
//     - Set characterName to match the character (e.g. "Warrior", "Mage")
//     - Set source to "menu" or "cheat" depending on which button it is
//
//   For UNCLICKABLE UI:
//     - Set trackType = Unclickable
//     - Add this alongside (not instead of) any existing button component
//
// ZERO game logic is touched. This is observe-only.
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;
using UnityEngine.EventSystems;

public class AnalyticsUITracker : MonoBehaviour, IPointerClickHandler
{
    public enum TrackType { Tracked, Unclickable, CharacterSelect }

    [Tooltip("Label that appears in your UGS Analytics dashboard.\n" +
             "For CharacterSelect this can be left blank — characterName is used instead.")]
    [SerializeField] private string objectLabel = "unlabelled";

    [Tooltip("Tracked       = general UI click.\n" +
             "Unclickable   = player clicked something they shouldn't.\n" +
             "CharacterSelect = character pick from menu or cheat button.")]
    [SerializeField] private TrackType trackType = TrackType.Tracked;

    [Header("Character Select only")]
    [Tooltip("The character's name as it should appear in analytics.\nE.g. 'Warrior', 'Mage', 'Rogue'.")]
    [SerializeField] private string characterName = "";

    [Tooltip("'menu' if this is a normal character select button.\n" +
             "'cheat' if this is a cheat/debug button.")]
    [SerializeField] private string source = "menu";

    // IPointerClickHandler — fires on any pointer click, regardless of whether
    // the button is interactable. This lets us catch clicks on disabled buttons too.
    public void OnPointerClick(PointerEventData eventData)
    {
        Fire();
    }

    private void Fire()
    {
        switch (trackType)
        {
            case TrackType.Tracked:
                AnalyticsEvents.TrackedObjectClicked(objectLabel);
                break;

            case TrackType.Unclickable:
                AnalyticsEvents.UnclickableClicked(objectLabel);
                break;

            case TrackType.CharacterSelect:
                string name = string.IsNullOrEmpty(characterName) ? objectLabel : characterName;
                AnalyticsEvents.CharacterSelected(name, source);
                break;
        }
    }
}