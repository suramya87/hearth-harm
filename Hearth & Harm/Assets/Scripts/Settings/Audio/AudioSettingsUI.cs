using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AudioSettingsUI : MonoBehaviour
{
    [Header("Sliders")]
    [SerializeField] private Slider musicVolumeSlider;

    [Header("Display")]
    [SerializeField] private TMP_Text musicVolumeValueText;

    private void OnEnable()
    {
        if (AudioSettingsManager.Instance == null || musicVolumeSlider == null)
            return;

        float volume = AudioSettingsManager.Instance.MusicVolume;

        musicVolumeSlider.SetValueWithoutNotify(volume);
        UpdateVolumeText(volume);

        musicVolumeSlider.onValueChanged.AddListener(OnMusicSliderChanged);
    }

    private void OnDisable()
    {
        if (musicVolumeSlider != null)
            musicVolumeSlider.onValueChanged.RemoveListener(OnMusicSliderChanged);
    }

    private void OnMusicSliderChanged(float value)
    {
        AudioSettingsManager.Instance.SetMusicVolume(value);
        UpdateVolumeText(value);
    }

    private void UpdateVolumeText(float value)
    {
        if (musicVolumeValueText == null)
            return;

        int percent = Mathf.RoundToInt(value * 100f);
        musicVolumeValueText.text = percent.ToString();
    }
}