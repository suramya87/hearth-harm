using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns a short sprite animation at world positions when an attack lands.
/// Works in 2D — no rotation needed, sprite faces camera automatically.
/// </summary>
public class AttackSpritePopup : MonoBehaviour
{
    [SerializeField] private float fps = 12f;

    // ── Static entry points ────────────────────────────────────────────────

    public static void Show(CombatActionData data, Vector3 worldPos,
                            Vector3 offset = default, float heightOffset = 0f,
                            float fps = 12f, float scale = 1f)
    {
        if (!HasVisuals(data)) return;
        Spawn(data, worldPos + offset + Vector3.up * heightOffset, fps, scale);
    }

    public static void ShowOnTiles(CombatActionData data,
                                   List<GridPosition> tiles,
                                   float heightOffset = 0f,
                                   float fps = 12f, float scale = 1f)
    {
        if (!HasVisuals(data) || tiles == null || tiles.Count == 0) return;
        if (RoomManager.Instance?.GetCurrentRoomGrid() == null) return;

        var room = RoomManager.Instance.GetCurrentRoomGrid();

        if (data.showSpritePerTile)
        {
            foreach (var t in tiles)
                Spawn(data, room.GetWorldPosition(t), fps, scale);
        }
        else
        {
            Spawn(data, room.GetWorldPosition(tiles[0]), fps, scale);
        }
    }

    // ── Internal ───────────────────────────────────────────────────────────

    private static bool HasVisuals(CombatActionData d) =>
        d != null && ((d.animationFrames != null && d.animationFrames.Length > 0) || d.icon != null);

    private static void Spawn(CombatActionData data, Vector3 pos, float fps, float scale)
    {
        bool hasAnim = data.animationFrames != null && data.animationFrames.Length > 0;
        var go = new GameObject("AttackSpritePopup");
        go.transform.position   = pos;
        go.transform.localScale = Vector3.one * scale;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = hasAnim ? data.animationFrames[0] : data.icon;
        sr.sortingOrder = 20;

        var popup = go.AddComponent<AttackSpritePopup>();
        popup.fps    = fps;
        popup._frames = hasAnim ? data.animationFrames : null;
        popup._sr     = sr;
    }

    // ── Instance ───────────────────────────────────────────────────────────

    private Sprite[]       _frames;
    private SpriteRenderer _sr;

    private void Start()
    {
        if (_frames != null && _frames.Length > 1) StartCoroutine(PlayAnim());
        else StartCoroutine(StaticFlash());
    }

    private IEnumerator PlayAnim()
    {
        float d = 1f / fps;
        foreach (var f in _frames)
        { if (f != null) _sr.sprite = f; yield return new WaitForSeconds(d); }
        Destroy(gameObject);
    }

    private IEnumerator StaticFlash()
    {
        yield return new WaitForSeconds(3f / fps);
        Destroy(gameObject);
    }
}