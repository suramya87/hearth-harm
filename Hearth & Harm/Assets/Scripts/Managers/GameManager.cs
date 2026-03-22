using UnityEngine;

/// <summary>
/// Central entry point. Holds the game-mode flag so the same scene and
/// code can run as single-player or multiplayer without duplicate scripts.
///
/// HOW TO USE
///   Single-player scene  → attach this, leave isMultiplayer = false
///   Multiplayer scene    → attach this, set isMultiplayer = true
///
/// Other systems read GameManager.IsMultiplayer to decide whether to
/// activate networked behaviour (e.g. MultiplayerTurnSystem vs TurnSystem).
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Mode")]
    [Tooltip("False = single-player. True = multiplayer (NGO required).")]
    [SerializeField] private bool isMultiplayer = false;

    public static bool IsMultiplayer => Instance != null && Instance.isMultiplayer;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }
}