using System;
using System.Collections;
using UnityEngine;

public class Unit : MonoBehaviour
{
    [Header("Visual Settings")]
    [Tooltip("Adjust this to center the sprite in the tile (usually 0.5, 0.5)")]
    [SerializeField] private Vector2 visualOffset = new Vector2(0.5f, 0.5f);

    private GridPosition gridPosition;
    private RoomGrid     currentRoomGrid;
    private bool         isInitialized;

    private MoveAction   moveAction;
    private BaseAction[] allActions;
    private PlayerStats  playerStats;

    internal bool IsSyncingFromNetwork { get; set; }

    private void Awake()
    {
        moveAction  = GetComponent<MoveAction>();
        allActions  = GetComponents<BaseAction>();
        playerStats = GetComponent<PlayerStats>();
    }

    private void Start()
    {
        if (GameManager.IsMultiplayer)
            StartCoroutine(SubscribeToNetworkedTurnSystem());
        else if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged += OnTurnChanged;
    }

    private IEnumerator SubscribeToNetworkedTurnSystem()
    {
        while (NetworkedTurnSystem.Instance == null) yield return null;
        NetworkedTurnSystem.Instance.OnTurnChanged += OnTurnChanged;
    }

    private void OnDestroy()
    {
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged -= OnTurnChanged;
        if (NetworkedTurnSystem.Instance != null)
            NetworkedTurnSystem.Instance.OnTurnChanged -= OnTurnChanged;
    }

    // ── Grid placement ─────────────────────────────────────────────────────

    public void PlaceInRoom(RoomGrid room, GridPosition newPos)
    {
        // Always update grid state (remove from old cell, register in new cell)
        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, this);

        currentRoomGrid = room;
        gridPosition    = newPos;
        isInitialized   = true;

        room.AddUnitAtGridPosition(newPos, this);

        if (!IsSyncingFromNetwork)
        {
            Vector3 world = room.GetWorldPosition(newPos);
            transform.position = new Vector3(
                world.x + visualOffset.x,
                world.y + visualOffset.y,
                transform.position.z
            );
        }

        if (GameManager.IsMultiplayer && !IsSyncingFromNetwork)
        {
            var bridge = GetComponent<NetworkedPlayerBridge>();
            if (bridge != null && bridge.IsSpawned && bridge.IsOwner)
                bridge.SyncGridPosition(room, newPos);
        }
    }

    // ── Turn events ────────────────────────────────────────────────────────

    private void OnTurnChanged(object sender, EventArgs e)
    {
        if (playerStats != null)
            playerStats.SetCurrentStaminaPoints(playerStats.GetMaxStaminaPoints());
    }

    // ── Accessors ──────────────────────────────────────────────────────────

    public GridPosition  GetGridPosition()   => isInitialized ? gridPosition : default;
    public RoomGrid      GetCurrentRoomGrid() => currentRoomGrid;
    public bool          IsInitialized()      => isInitialized;
    public MoveAction    GetMoveAction()      => moveAction;
    public BaseAction[]  GetBaseActionArray() => allActions;
}