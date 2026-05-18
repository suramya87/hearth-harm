using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InventorySlotUI : MonoBehaviour, IDropHandler
{
    [SerializeField] private Image iconImage;

    private InventoryPanelUI panel;
    private int slotIndex;

    public Inventory Inventory => panel.Inventory;
    public int SlotIndex => slotIndex;

    public void Initialize(InventoryPanelUI ownerPanel, int index)
    {
        panel = ownerPanel;
        slotIndex = index;
        Refresh();
    }

    public void Refresh()
    {
        ItemData item = Inventory.GetItem(slotIndex);

        if (iconImage != null)
        {
            iconImage.sprite = item != null ? item.icon : null;
            iconImage.enabled = item != null;
        }
    }

    public void OnDrop(PointerEventData eventData)
    {
        InventoryItemDragUI draggedItem = eventData.pointerDrag?.GetComponent<InventoryItemDragUI>();

        if (draggedItem == null) return;

        Inventory sourceInventory = draggedItem.SourceSlot.Inventory;
        int sourceIndex = draggedItem.SourceSlot.SlotIndex;

        sourceInventory.SwapItems(sourceIndex, Inventory, slotIndex);

        draggedItem.SourceSlot.Refresh();
        Refresh();

        panel.RefreshAll();
        draggedItem.SourceSlot.GetComponentInParent<InventoryPanelUI>()?.RefreshAll();
    }
}