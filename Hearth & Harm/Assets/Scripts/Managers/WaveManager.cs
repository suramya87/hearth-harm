using UnityEngine;

/// <summary>
/// Tracks current level and enemy budget scaling.
/// Persists across scene loads.
/// </summary>
public class WaveManager : MonoBehaviour
{
    public static WaveManager Instance { get; private set; }

    [Header("Enemy Budget")]
    [SerializeField] private int baseEnemyCount  = 5;
    [SerializeField] private int enemiesPerLevel = 3;
    [SerializeField] private int maxEnemies      = 40;

    [Header("Room Count")]
    [SerializeField] private int baseMinRooms  = 6;
    [SerializeField] private int baseMaxRooms  = 10;
    [SerializeField] private int roomsPerLevel = 1;
    [SerializeField] private int maxRooms      = 20;

    public int CurrentLevel  { get; private set; } = 1;
    public int StagesCleared { get; private set; } = 0;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void AdvanceLevel()
    {
        StagesCleared++;
        CurrentLevel++;
        Debug.Log($"[WaveManager] Level {CurrentLevel}. Stages cleared: {StagesCleared}.");
    }

    public void ResetToLevel1()
    {
        CurrentLevel  = 1;
        StagesCleared = 0;
    }

    public int GetTotalEnemyBudget() =>
        Mathf.Min(baseEnemyCount + (CurrentLevel - 1) * enemiesPerLevel, maxEnemies);

    public int GetMinRooms() =>
        Mathf.Min(baseMinRooms + (CurrentLevel - 1) * roomsPerLevel, maxRooms - 1);

    public int GetMaxRooms() =>
        Mathf.Min(baseMaxRooms + (CurrentLevel - 1) * roomsPerLevel, maxRooms);
}