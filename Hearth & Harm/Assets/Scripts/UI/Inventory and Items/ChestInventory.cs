using UnityEngine;

public class ChestInventory : MonoBehaviour
{
    [SerializeField] private Inventory inventory = new Inventory();

    public Inventory Inventory => inventory;

    public void OpenChest()
    {
        InventoryMenuUI.Instance.OpenChest(this);
    }

    private void OnMouseDown()
    {
        OpenChest();
    }
}