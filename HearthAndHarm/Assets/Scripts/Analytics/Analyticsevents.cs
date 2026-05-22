 
using System.Collections.Generic;
using UnityEngine;
using Unity.Services.Analytics;
 
public static class AnalyticsEvents
{
    // ── Session & Device ───────────────────────────────────────────────────
 
    /// <summary>
    /// Call once at game start (after AnalyticsService is ready).
    /// Logs platform, screen size, and whether this is a WebGL/web build.
    /// </summary>
    public static void SessionStarted()
    {
        if (!GameManager.AnalyticsReady) return;
 
        AnalyticsService.Instance.RecordEvent(new CustomEvent("session_started")
        {
            { "platform",     Application.platform.ToString()                        },
            { "IsWebBuild", Application.platform == RuntimePlatform.WebGLPlayer   },
            { "screen_width", Screen.width                                           },
            { "screen_height",Screen.height                                          },
            { "device_model", SystemInfo.deviceModel                                 },
            { "os",           SystemInfo.operatingSystem                             },
            { "app_version",  Application.version                                    },
        });
 
        Debug.Log("[Analytics] session_started");
    }
 
    // ── Load times ─────────────────────────────────────────────────────────
 
    /// <summary>
    /// Fire after a scene finishes loading.
    /// Call from AnalyticsLoadTracker via SceneManager.sceneLoaded.
    /// </summary>
    public static void SceneLoadTime(string sceneName, float seconds, bool isWebBuild)
    {
        if (!GameManager.AnalyticsReady) return;
 
        float rounded = Mathf.Round(seconds * 100f) / 100f;
 
        AnalyticsService.Instance.RecordEvent(new CustomEvent("scene_load_time")
        {
            { "scene_name",   sceneName   },
            { "seconds",      rounded     },
            { "IsWebBuild", isWebBuild  },
        });
 
        Debug.Log($"[Analytics] scene_load_time | scene:{sceneName} time:{rounded}s web:{isWebBuild}");
    }
 
    /// <summary>
    /// Fire when a room GameObject finishes activating.
    /// Measured from SetActive(true) to the end of the first frame it's alive.
    /// </summary>
    public static void RoomLoadTime(string roomName, float seconds, bool isWebBuild)
    {
        if (!GameManager.AnalyticsReady) return;
 
        float rounded = Mathf.Round(seconds * 100f) / 100f;
 
        AnalyticsService.Instance.RecordEvent(new CustomEvent("room_load_time")
        {
            { "room_name",    roomName    },
            { "seconds",      rounded     },
            { "IsWebBuild", isWebBuild  },
        });
 
        Debug.Log($"[Analytics] room_load_time | room:{roomName} time:{rounded}s web:{isWebBuild}");
    }
 
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
 
    /// <summary>
    /// Fire when the player leaves a room.
    /// Now includes FPS data sampled during the room stay.
    /// avg_fps / min_fps let you correlate lag with room difficulty or clear rate.
    /// </summary>
    public static void RoomTimeSpent(string roomName, float secondsSpent, bool wasCleared,
                                     float avgFps, float minFps)
    {
        if (!GameManager.AnalyticsReady) return;
 
        AnalyticsService.Instance.RecordEvent(new CustomEvent("room_time_spent")
        {
            { "room_name",     roomName                                   },
            { "seconds_spent", Mathf.Round(secondsSpent * 10f) / 10f     },
            { "was_cleared",   wasCleared                                 },
            { "avg_fps",       Mathf.Round(avgFps)                        },
            { "min_fps",       Mathf.Round(minFps)                        },
        });
 
        Debug.Log($"[Analytics] room_time_spent | room:{roomName} time:{secondsSpent:F1}s " +
                  $"cleared:{wasCleared} avgFPS:{avgFps:F0} minFPS:{minFps:F0}");
    }
 
    // ── Character selection ────────────────────────────────────────────────
 

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
 
    public static void UnclickableClicked(string objectLabel)
    {
        if (!GameManager.AnalyticsReady) return;
 
        AnalyticsService.Instance.RecordEvent(new CustomEvent("unclickable_clicked")
        {
            { "object_label", objectLabel }
        });
 
        Debug.Log($"[Analytics] unclickable_clicked | label:{objectLabel}");
    }
 
    // ── Generic labelled click ─────────────────────────────────────────────
 
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
