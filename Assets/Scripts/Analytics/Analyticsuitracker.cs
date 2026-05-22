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