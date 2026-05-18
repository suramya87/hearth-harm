using System;
using UnityEngine;

[Serializable]
public class Inventory
{
    [SerializeField] private ItemData[] slots = new ItemData[10];

    public int Size => slots.Length;

    public ItemData GetItem(int index)
    {
        if (!IsValidIndex(index)) return null;
        return slots[index];
    }

    public void SetItem(int index, ItemData item)
    {
        if (!IsValidIndex(index)) return;
        slots[index] = item;
    }

    public bool AddItem(ItemData item)
    {
        if (item == null) return false;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null)
            {
                slots[i] = item;
                return true;
            }
        }

        return false;
    }

    public void SwapItems(int indexA, Inventory otherInventory, int indexB)
    {
        if (otherInventory == null) return;
        if (!IsValidIndex(indexA) || !otherInventory.IsValidIndex(indexB)) return;

        ItemData temp = slots[indexA];
        slots[indexA] = otherInventory.slots[indexB];
        otherInventory.slots[indexB] = temp;
    }

    private bool IsValidIndex(int index)
    {
        return index >= 0 && index < slots.Length;
    }
}