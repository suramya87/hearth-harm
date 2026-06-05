// ─────────────────────────────────────────────────────────────────────────────
// AnalyticsClickTracker.cs  (World-space — requires a Collider2D or Collider)
//
// SETUP
//   1. Attach to any world-space GameObject you want to track.
//   2. Make sure the GameObject has a Collider2D (or Collider for 3D).
//   3. Set the label in the Inspector — this is what shows in your dashboard.
//   4. Choose the click type:
//        Tracked    → fires tracked_object_clicked
//        Unclickable → fires unclickable_clicked (for things players shouldn't click)
//
// ZERO game logic is touched. This is observe-only.
// ─────────────────────────────────────────────────────────────────────────────

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
        // OnMouseDown already fired — no double-count needed
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