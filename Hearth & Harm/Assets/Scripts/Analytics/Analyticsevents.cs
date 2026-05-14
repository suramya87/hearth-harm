// ─────────────────────────────────────────────────────────────────────────────
// AnalyticsEvents.cs
//
// Single static class that owns every custom event fired in the game.
// Nothing in here touches game logic — it only reads data passed to it
// and sends it to UGS Analytics.
//
// ADDING A NEW EVENT:
//   1. Add a static method here.
//   2. Call it from the observer component that detects the thing.
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using UnityEngine;
using Unity.Services.Analytics;

public static class AnalyticsEvents
{
    // ── Rooms ──────────────────────────────────────────────────────────────

    /// <summary>Fire when the player enters a room for the first time.</summary>
    public static void RoomVisited(string roomName)
    {
        if (!GameManager.AnalyticsReady) return;

        AnalyticsService.Instance.RecordEvent(new CustomEvent("room_visited")
        {
            { "room_name", roomName }
        });

        Debug.Log($"[Analytics] room_visited | room:{roomName}");
    }

    /// <summary>Fire when all enemies in a room are defeated.</summary>
    public static void RoomCleared(string roomName)
    {
        if (!GameManager.AnalyticsReady) return;

        AnalyticsService.Instance.RecordEvent(new CustomEvent("room_cleared")
        {
            { "room_name", roomName }
        });

        Debug.Log($"[Analytics] room_cleared | room:{roomName}");
    }

    // ── Character selection ────────────────────────────────────────────────

    /// <summary>
    /// Fire when a character is selected — from the main menu OR a cheat button.
    /// source: "menu" | "cheat"
    /// </summary>
    public static void CharacterSelected(string characterName, string source = "menu")
    {
        if (!GameManager.AnalyticsReady) return;

        AnalyticsService.Instance.RecordEvent(new CustomEvent("character_selected")
        {
            { "character_name", characterName },
            { "source",         source        }
        });

        Debug.Log($"[Analytics] character_selected | char:{characterName} source:{source}");
    }

    // ── Unclickable / invalid clicks ───────────────────────────────────────

    /// <summary>
    /// Fire when a player clicks something tagged as unclickable.
    /// objectLabel: the label you set on the AnalyticsClickTracker component.
    /// </summary>
    public static void UnclickableClicked(string objectLabel)
    {
        if (!GameManager.AnalyticsReady) return;

        AnalyticsService.Instance.RecordEvent(new CustomEvent("unclickable_clicked")
        {
            { "object_label", objectLabel }
        });

        Debug.Log($"[Analytics] unclickable_clicked | label:{objectLabel}");
    }

    // ── Generic labelled click (for any tracked object) ────────────────────

    /// <summary>
    /// Fire when a player clicks any object that has a tracker on it.
    /// Use this for things you want to count but aren't specifically
    /// rooms, characters, or invalid targets.
    /// </summary>
    public static void TrackedObjectClicked(string objectLabel)
    {
        if (!GameManager.AnalyticsReady) return;

        AnalyticsService.Instance.RecordEvent(new CustomEvent("tracked_object_clicked")
        {
            { "object_label", objectLabel }
        });

        Debug.Log($"[Analytics] tracked_object_clicked | label:{objectLabel}");
    }
}