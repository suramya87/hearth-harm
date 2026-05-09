using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class GridCostVisualizer : MonoBehaviour
{
    public static GridCostVisualizer Instance;

    [SerializeField] private GameObject costTextPrefab;

    private Dictionary<GridPosition, TextMeshPro> activeTexts =
        new Dictionary<GridPosition, TextMeshPro>();

    void Awake()
    {
        Instance = this;
    }

    public void ShowCost(GridPosition pos, int cost)
    {
        if (activeTexts.ContainsKey(pos))
            return;

        RoomGrid room = RoomManager.Instance.GetCurrentRoomGrid();
        Vector3 world = room.GetWorldPosition(pos);

        GameObject obj = Instantiate(
            costTextPrefab,
            world + Vector3.up * 0.1f,
            costTextPrefab.transform.rotation
        );

        TextMeshPro tmp = obj.GetComponent<TextMeshPro>();
        tmp.text = cost.ToString();

        activeTexts[pos] = tmp;
    }

    public void ClearAll()
    {
        foreach (var text in activeTexts.Values)
            Destroy(text.gameObject);

        activeTexts.Clear();
    }
}