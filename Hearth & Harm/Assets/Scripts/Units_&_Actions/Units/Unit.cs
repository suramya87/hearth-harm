using System;
using UnityEngine;

public class Unit : MonoBehaviour
{
    [Header("Visual Settings")]
    [Tooltip("Adjust this to center the sprite in the tile (usually 0.5, 0.5)")]
    [SerializeField] private Vector2 visualOffset = new Vector2(0.5f, 0.5f);

    private GridPosition  gridPosition;
    private RoomGrid      currentRoomGrid;
    private bool          isInitialized;

    private MoveAction    moveAction;
    private BaseAction[]  allActions;
    private PlayerStats   playerStats;

    private void Awake()
    {
        moveAction  = GetComponent<MoveAction>();
        allActions  = GetComponents<BaseAction>();
        playerStats = GetComponent<PlayerStats>();
    }

    private void Start()
    {
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged += OnTurnChanged;
    }

    private void OnDestroy()
    {
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged -= OnTurnChanged;
    }

    // ── Grid placement ─────────────────────────────────────────────────────

    public void PlaceInRoom(RoomGrid room, GridPosition newPos)
    {
        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, this);

        currentRoomGrid = room;
        gridPosition    = newPos;

        // 1. Get the base world position (the corner of the tile)
        Vector3 world = room.GetWorldPosition(newPos);

        // 2. Apply the visual offset to the world position
        // We add visualOffset.x to X and visualOffset.y to Y
        transform.position = new Vector3(
            world.x + visualOffset.x, 
            world.y + visualOffset.y, 
            transform.position.z
        );

        room.AddUnitAtGridPosition(newPos, this);
        isInitialized = true;
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