using UnityEngine;

public class AudioSettingsManager : MonoBehaviour
{
    public static AudioSettingsManager Instance { get; private set; }

    private const string MUSIC_VOLUME_KEY = "MusicVolume";

    [Header("Music")]
    [SerializeField] private AudioSource musicSource;

    [Range(0f, 1f)]
    [SerializeField] private float musicVolume = 0.8f;

    public float MusicVolume => musicVolume;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        
        transform.SetParent(null); 
        DontDestroyOnLoad(gameObject);

        LoadSettings();
        ApplyMusicVolume();
    }

    public void SetMusicVolume(float value)
    {
        musicVolume = Mathf.Clamp01(value);
        ApplyMusicVolume();

        PlayerPrefs.SetFloat(MUSIC_VOLUME_KEY, musicVolume);
        PlayerPrefs.Save();
    }

    private void LoadSettings()
    {
        musicVolume = PlayerPrefs.GetFloat(MUSIC_VOLUME_KEY, musicVolume);
    }

    private void ApplyMusicVolume()
    {
        if (musicSource != null)
            musicSource.volume = musicVolume;
    }

    public void SetMusicSource(AudioSource source)
    {
        musicSource = source;
        ApplyMusicVolume();
    }
}