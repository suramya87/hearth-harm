using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class GraphicsSettingsUI : MonoBehaviour
{
    [Header("Resolution")]
    [SerializeField] private TMP_Dropdown resolutionDropdown;

    private Resolution[] resolutions;

    private void OnEnable()
    {
        PopulateResolutions();
    }

    private void PopulateResolutions()
    {
        if (resolutionDropdown == null)
            return;

        resolutions = Screen.resolutions;

        resolutionDropdown.ClearOptions();

        List<string> options = new();
        int currentResolutionIndex = 0;

        for (int i = 0; i < resolutions.Length; i++)
        {
            Resolution res = resolutions[i];
            string option = $"{res.width} x {res.height} @ {res.refreshRateRatio.value:F0}Hz";
            options.Add(option);

            if (res.width == Screen.currentResolution.width &&
                res.height == Screen.currentResolution.height)
            {
                currentResolutionIndex = i;
            }
        }

        resolutionDropdown.AddOptions(options);
        resolutionDropdown.SetValueWithoutNotify(currentResolutionIndex);
        resolutionDropdown.RefreshShownValue();

        resolutionDropdown.onValueChanged.RemoveListener(SetResolution);
        resolutionDropdown.onValueChanged.AddListener(SetResolution);
    }

    private void OnDisable()
    {
        if (resolutionDropdown != null)
            resolutionDropdown.onValueChanged.RemoveListener(SetResolution);
    }

    private void SetResolution(int index)
    {
        if (resolutions == null || index < 0 || index >= resolutions.Length)
            return;

        Resolution res = resolutions[index];

        Screen.SetResolution(
            res.width,
            res.height,
            Screen.fullScreenMode,
            res.refreshRateRatio
        );

        PlayerPrefs.SetInt("ResolutionIndex", index);
        PlayerPrefs.Save();
    }
}