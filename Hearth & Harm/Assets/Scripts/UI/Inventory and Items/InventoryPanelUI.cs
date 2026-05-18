using UnityEngine;

public class InventoryPanelUI : MonoBehaviour
{
    [SerializeField] private InventorySlotUI slotPrefab;
    [SerializeField] private Transform slotRoot;

    public Inventory Inventory { get; private set; }

    private InventorySlotUI[] spawnedSlots;

    public void BindInventory(Inventory inventory)
    {
        Inventory = inventory;

        foreach (Transform child in slotRoot)
            Destroy(child.gameObject);

        spawnedSlots = new InventorySlotUI[inventory.Size];

        for (int i = 0; i < inventory.Size; i++)
        {
            InventorySlotUI slot = Instantiate(slotPrefab, slotRoot);
            slot.Initialize(this, i);
            spawnedSlots[i] = slot;
        }
    }

    public void RefreshAll()
    {
        if (spawnedSlots == null) return;

        foreach (InventorySlotUI slot in spawnedSlots)
        {
            if (slot != null)
                slot.Refresh();
        }
    }
}