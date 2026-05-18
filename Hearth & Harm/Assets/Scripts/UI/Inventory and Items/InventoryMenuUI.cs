using UnityEngine;

public class InventoryMenuUI : MonoBehaviour
{
    public static InventoryMenuUI Instance { get; private set; }

    [Header("Root")]
    [SerializeField] private GameObject menuRoot;

    [Header("Panels")]
    [SerializeField] private InventoryPanelUI playerInventoryPanel;
    [SerializeField] private InventoryPanelUI chestInventoryPanel;

    [Header("Player Inventory")]
    [SerializeField] private Inventory playerInventory = new Inventory();

    private ChestInventory currentChest;

    public Inventory PlayerInventory => playerInventory;

    private void Awake()
    {
        Instance = this;

        if (menuRoot != null)
            menuRoot.SetActive(false);
    }

    public void TogglePlayerInventory()
    {
        if (menuRoot.activeSelf)
            Close();
        else
            OpenPlayerInventoryOnly();
    }

    public void OpenPlayerInventoryOnly()
    {
        currentChest = null;

        menuRoot.SetActive(true);

        playerInventoryPanel.BindInventory(playerInventory);

        if (chestInventoryPanel != null)
            chestInventoryPanel.gameObject.SetActive(false);
    }

    public void OpenChest(ChestInventory chest)
    {
        currentChest = chest;

        menuRoot.SetActive(true);

        playerInventoryPanel.BindInventory(playerInventory);

        chestInventoryPanel.gameObject.SetActive(true);
        chestInventoryPanel.BindInventory(chest.Inventory);
    }

    public void Close()
    {
        currentChest = null;

        if (menuRoot != null)
            menuRoot.SetActive(false);
    }
}