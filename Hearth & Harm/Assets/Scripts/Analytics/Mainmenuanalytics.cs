
using System.Collections;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using Unity.Services.Analytics;

public class MainMenuAnalytics : MonoBehaviour
{
    private IEnumerator Start()
    {
        // Don't init if GameManager already did it (shouldn't happen but safety net)
        if (GameManager.AnalyticsReady) yield break;

        var options = new InitializationOptions();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        options.SetEnvironmentName("development");
#else
        options.SetEnvironmentName("production");
#endif

        var task = UnityServices.InitializeAsync(options);
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.IsFaulted)
        {
            Debug.LogWarning($"[MainMenuAnalytics] UGS init failed: {task.Exception?.Message}");
            yield break;
        }

        AnalyticsService.Instance.StartDataCollection();

        // Fire session started with hardware info
        AnalyticsService.Instance.RecordEvent(new CustomEvent("session_started")
        {
            { "platform",        Application.platform.ToString()          },
            { "screen_width",    Screen.width                             },
            { "screen_height",   Screen.height                            },
            { "device_model",    SystemInfo.deviceModel                   },
            { "os",              SystemInfo.operatingSystem               },
            { "app_version",     Application.version                      },
            { "processor",       SystemInfo.processorType                 },
            { "processor_cores", SystemInfo.processorCount                },
            { "ram_mb",          SystemInfo.systemMemorySize              },
            { "gpu",             SystemInfo.graphicsDeviceName            },
            { "vram_mb",         SystemInfo.graphicsMemorySize            },
            { "graphics_api",    SystemInfo.graphicsDeviceType.ToString() },
        });

        AnalyticsService.Instance.Flush();
        Debug.Log("[MainMenuAnalytics] UGS ready — session_started fired.");
    }
}