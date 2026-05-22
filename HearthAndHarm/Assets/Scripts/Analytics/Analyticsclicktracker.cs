using UnityEngine;

public class AnalyticsClickTracker : MonoBehaviour
{
    public enum TrackType { Tracked, Unclickable }

    [Tooltip("Label that appears in your UGS Analytics dashboard.")]
    [SerializeField] private string objectLabel = "unlabelled";

    [Tooltip("Tracked = general click event.\nUnclickable = player clicked something they shouldn't.")]
    [SerializeField] private TrackType trackType = TrackType.Tracked;

    private void OnMouseDown()
    {
        Fire();
    }

    // Also handle 2D physics clicks
    private void OnMouseUpAsButton()
    {
        // OnMouseDown already fired no double-count needed
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
        }
    }
}