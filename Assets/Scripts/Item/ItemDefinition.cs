using UnityEngine;

/// <summary>
/// ItemType declares the type of item this is and is used for checks.
/// </summary>
public enum ItemType
{
    None,
    Food,
    Material,
    Placeable,
    Collectible
}

/// <summary>
/// ScriptableObject that defines an item, including its identity and
/// all values for storefront, inventory, and placement UI.
/// </summary>
[CreateAssetMenu(menuName = "SuperFarm/Item Definition", fileName = "Item_")]
public class ItemDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField, Tooltip("Name for the item (e.g. 'Carrot').")]
    private string itemName = "Carrot";
    [SerializeField, Tooltip("Sprite icon for the item.")]
    private Sprite icon;
    [SerializeField, Tooltip("The type of the item.")]
    private ItemType itemType;

    [Header("Store Front Values")]
    [SerializeField, Tooltip("If the item is available for the store front it can be bought or sold.")]
    private bool isAvailableForStorefront;
    [SerializeField, Tooltip("Base sell value of the item.")]
    private float baseSellValue;
    [SerializeField, Tooltip("Base buy value of the item.")]
    private float baseBuyValue;

    [Header("Feeding Values")]
    [SerializeField, Tooltip("If the item is available for feeding it can be fed to an animal.")]
    private bool isAvailableForFeeding;
    [SerializeField, Tooltip("Base feed value of the item.")]
    private float baseFeedValue;

    [Header("Placement Values")]
    [SerializeField, Tooltip("Prefab to spawn in the world when this item is placed into a slot. " +
                             "Only required for items with ItemType.Placeable.")]
    private GameObject placeablePrefab;

    #region Accessors
    public string ItemName               => itemName;
    public Sprite Icon                   => icon;
    public ItemType ItemType             => itemType;

    public bool IsAvailableForStorefront => isAvailableForStorefront;
    public float BaseSellValue           => baseSellValue;
    public float BaseBuyValue            => baseBuyValue;

    public bool IsAvailableForFeeding    => isAvailableForFeeding;
    public float BaseFeedValue           => baseFeedValue;

    public GameObject PlaceablePrefab    => placeablePrefab;
    #endregion
}