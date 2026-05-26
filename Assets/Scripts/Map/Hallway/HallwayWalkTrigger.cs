using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class HallwayWalkTrigger : MonoBehaviour
{
    private HallwayGrid hallway;
    private bool        locked;
    private bool        cooling;
    private bool        applied;

    public GameObject DoorStripObject { get; set; }

    public void Initialize(HallwayGrid hg)
    {
        hallway = hg;
        GetComponent<Collider2D>().isTrigger = true;
    }

    public void SetLocked(bool isLocked)
    {
        locked = isLocked;
        if (DoorStripObject != null) DoorStripObject.SetActive(isLocked);
    }

    public void DisableTemporarily(float seconds) =>
        StartCoroutine(TemporaryDisableRoutine(seconds));

    private IEnumerator TemporaryDisableRoutine(float seconds)
    {
        cooling = true;
        applied = false;
        yield return new WaitForSeconds(seconds);
        cooling = false;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        applied = false;
        TryApplyCamera(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (applied && RoomManager.Instance != null && RoomManager.Instance.IsInHallway())
            return;

        TryApplyCamera(other);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
            applied = false;
    }

    private void TryApplyCamera(Collider2D other)
    {
        if (cooling || locked) return;
        if (!other.CompareTag("Player")) return;
        if (hallway == null || !hallway.IsReady) return;

        if (!IsLocalPlayerCollider(other)) return;

        ApplyHallwayCameraBounds();
        RoomManager.Instance?.SetInHallway();
        applied = true;
    }

    private static bool IsLocalPlayerCollider(Collider2D col)
    {
        if (!GameManager.IsMultiplayer)
            return col.GetComponent<Unit>() != null
                || col.GetComponentInParent<Unit>() != null;

        var netObj = col.GetComponent<Unity.Netcode.NetworkObject>()
                  ?? col.GetComponentInParent<Unity.Netcode.NetworkObject>();

        return netObj != null && netObj.IsOwner;
    }

    private void ApplyHallwayCameraBounds()
    {
        var cam = CameraController2D.Instance;
        if (cam == null || hallway == null) return;

        var floor = hallway.FloorTilemap;
        if (floor == null) return;

        var     cb       = floor.cellBounds;
        Vector3 worldMin = floor.GetCellCenterWorld(
            new Vector3Int(cb.xMin,     cb.yMin,     0));
        Vector3 worldMax = floor.GetCellCenterWorld(
            new Vector3Int(cb.xMax - 1, cb.yMax - 1, 0));

        Vector3 center = (worldMin + worldMax) * 0.5f;
        float   width  = Mathf.Abs(worldMax.x - worldMin.x);
        float   height = Mathf.Abs(worldMax.y - worldMin.y);

        float finalWidth  = Mathf.Max(width  + 64f, 32f);
        float finalHeight = Mathf.Max(height + 64f, 10f);

        cam.SetRoomBounds(new Bounds(center,
            new Vector3(finalWidth, finalHeight, 10f)));
    }
}