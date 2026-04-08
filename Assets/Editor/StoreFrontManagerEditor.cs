using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom editor for StoreFrontManager.
/// Displays live storefront state during Play Mode — coin balance, sell panel,
/// buy panel with catalogue stock, and buyback panel with snapshotted prices.
/// </summary>
[CustomEditor(typeof(StoreFrontManager))]
public class StoreFrontManagerEditor : Editor
{
    private FieldInfo _inventoryField;
    private FieldInfo _buybackField;

    // Column widths
    private const float IconWidth  = 20f;
    private const float ValueWidth = 46f;
    private const float CountWidth = 36f;
    private const float StockWidth = 46f;

    // Cached styles
    private GUIStyle _centeredLabel;
    private GUIStyle _headerLabel;
    private GUIStyle _balanceLabel;
    private GUIStyle _sectionLabel;

    private void OnEnable()
    {
        _inventoryField = typeof(PlayerInventory)
            .GetField("_inventory", BindingFlags.NonPublic | BindingFlags.Instance);
        _buybackField = typeof(StoreFrontManager)
            .GetField("_buyback", BindingFlags.NonPublic | BindingFlags.Instance);
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        if (!Application.isPlaying)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox("Storefront state is only visible during Play Mode.", MessageType.Info);
            return;
        }

        EnsureStyles();

        var manager = target as StoreFrontManager;

        // -----------------------------------------------------------------
        // Coin Balance
        // -----------------------------------------------------------------
        EditorGUILayout.Space(8);
        DrawDivider(new Color(0.9f, 0.75f, 0.2f, 0.8f));
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField($"Coin Balance:  {manager.CoinBalance:F2}", _balanceLabel);
        EditorGUILayout.Space(2);
        DrawDivider(new Color(0.9f, 0.75f, 0.2f, 0.8f));

        if (PlayerInventory.Instance == null)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox("No PlayerInventory instance found in scene.", MessageType.Warning);
            EditorUtility.SetDirty(target);
            return;
        }

        var dict = _inventoryField?.GetValue(PlayerInventory.Instance)
                       as Dictionary<ItemDefinition, int>;

        // -----------------------------------------------------------------
        // Sell Panel
        // -----------------------------------------------------------------
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Sell Panel  (Player Inventory)", _sectionLabel);
        EditorGUILayout.Space(4);

        var sellable = manager.GetSellableItems();

        if (sellable == null || sellable.Count == 0)
            EditorGUILayout.HelpBox("No sellable items currently in inventory.", MessageType.None);
        else
        {
            DrawSellHeader();
            EditorGUILayout.Space(2);
            foreach (var item in sellable)
            {
                int held = dict != null && dict.TryGetValue(item, out int c) ? c : 0;
                DrawSellRow(item, held);
            }
        }

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField($"Unique sellable items: {sellable?.Count ?? 0}", EditorStyles.miniLabel);

        // -----------------------------------------------------------------
        // Buy Panel
        // -----------------------------------------------------------------
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Buy Panel  (Store Catalogue)", _sectionLabel);
        EditorGUILayout.Space(4);

        var buyable = manager.GetBuyableItems();

        if (buyable == null || buyable.Count == 0)
            EditorGUILayout.HelpBox("No catalogue assigned or catalogue is empty.", MessageType.None);
        else
        {
            DrawBuyHeader();
            EditorGUILayout.Space(2);
            foreach (var item in buyable)
                DrawBuyRow(item, manager);
        }

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField($"Catalogue items: {buyable?.Count ?? 0}", EditorStyles.miniLabel);

        // -----------------------------------------------------------------
        // Buyback Panel
        // -----------------------------------------------------------------
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Buyback Panel  (Sold This Session)", _sectionLabel);
        EditorGUILayout.Space(4);

        var buybackItems = manager.GetBuybackItems();

        if (buybackItems == null || buybackItems.Count == 0)
            EditorGUILayout.HelpBox("Nothing in the buyback pool yet.", MessageType.None);
        else
        {
            DrawBuybackHeader();
            EditorGUILayout.Space(2);
            foreach (var item in buybackItems)
                DrawBuybackRow(item, manager);
        }

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField($"Buyback pool items: {buybackItems?.Count ?? 0}", EditorStyles.miniLabel);

        EditorUtility.SetDirty(target);
    }

    // -------------------------------------------------------------------------
    // Sell table
    // -------------------------------------------------------------------------

    private void DrawSellHeader()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(IconWidth + 2);
            EditorGUILayout.LabelField("Item", _centeredLabel);
            EditorGUILayout.LabelField("Sell", _centeredLabel, GUILayout.Width(ValueWidth));
            EditorGUILayout.LabelField("Held", _centeredLabel, GUILayout.Width(CountWidth));
        }
        DrawDivider(new Color(0.5f, 0.5f, 0.5f, 0.5f));
    }

    private void DrawSellRow(ItemDefinition item, int held)
    {
        if (item == null) return;

        using (new EditorGUILayout.HorizontalScope())
        {
            DrawIcon(item);
            GUILayout.Space(2);
            EditorGUILayout.LabelField(item.ItemName, _centeredLabel);
            EditorGUILayout.LabelField($"{item.BaseSellValue:F1}g", _centeredLabel,
                GUILayout.Width(ValueWidth));
            var bold = new GUIStyle(_centeredLabel) { fontStyle = FontStyle.Bold };
            EditorGUILayout.LabelField(held.ToString(), bold, GUILayout.Width(CountWidth));
        }
        DrawDivider(new Color(0.3f, 0.3f, 0.3f, 0.3f));
    }

    // -------------------------------------------------------------------------
    // Buy table
    // -------------------------------------------------------------------------

    private void DrawBuyHeader()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(IconWidth + 2);
            EditorGUILayout.LabelField("Item",    _centeredLabel);
            EditorGUILayout.LabelField("Buy",     _centeredLabel, GUILayout.Width(ValueWidth));
            EditorGUILayout.LabelField("Stock",   _centeredLabel, GUILayout.Width(StockWidth));
            EditorGUILayout.LabelField("Can Buy", _centeredLabel, GUILayout.Width(CountWidth));
        }
        DrawDivider(new Color(0.5f, 0.5f, 0.5f, 0.5f));
    }

    private void DrawBuyRow(ItemDefinition item, StoreFrontManager manager)
    {
        if (item == null) return;

        int stock    = manager.GetStock(item);
        int maxStock = manager.GetMaxStock(item);

        int canAfford = item.BaseBuyValue > 0
            ? Mathf.FloorToInt(manager.CoinBalance / item.BaseBuyValue)
            : 0;
        int canBuy = Mathf.Min(canAfford, stock);

        using (new EditorGUILayout.HorizontalScope())
        {
            DrawIcon(item);
            GUILayout.Space(2);
            EditorGUILayout.LabelField(item.ItemName, _centeredLabel);
            EditorGUILayout.LabelField($"{item.BaseBuyValue:F1}g", _centeredLabel,
                GUILayout.Width(ValueWidth));

            // Stock — amber when low (<=25% of max), grey when empty
            var prev = GUI.color;
            float stockRatio = maxStock > 0 ? (float)stock / maxStock : 0f;
            GUI.color = stock == 0
                ? new Color(0.6f, 0.6f, 0.6f)
                : stockRatio <= 0.25f
                    ? new Color(1f, 0.75f, 0.2f)
                    : Color.white;
            var bold = new GUIStyle(_centeredLabel) { fontStyle = FontStyle.Bold };
            EditorGUILayout.LabelField($"{stock}/{maxStock}", bold, GUILayout.Width(StockWidth));
            GUI.color = prev;

            // Can buy — green if > 0, grey otherwise
            GUI.color = canBuy > 0 ? new Color(0.4f, 1f, 0.4f) : new Color(0.6f, 0.6f, 0.6f);
            EditorGUILayout.LabelField(canBuy.ToString(), bold, GUILayout.Width(CountWidth));
            GUI.color = prev;
        }
        DrawDivider(new Color(0.3f, 0.3f, 0.3f, 0.3f));
    }

    // -------------------------------------------------------------------------
    // Buyback table
    // -------------------------------------------------------------------------

    private void DrawBuybackHeader()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(IconWidth + 2);
            EditorGUILayout.LabelField("Item",     _centeredLabel);
            EditorGUILayout.LabelField("Price",    _centeredLabel, GUILayout.Width(ValueWidth));
            EditorGUILayout.LabelField("Avail",    _centeredLabel, GUILayout.Width(CountWidth));
            EditorGUILayout.LabelField("Can Buy",  _centeredLabel, GUILayout.Width(CountWidth));
        }
        DrawDivider(new Color(0.5f, 0.5f, 0.5f, 0.5f));
    }

    private void DrawBuybackRow(ItemDefinition item, StoreFrontManager manager)
    {
        if (item == null) return;

        int available    = manager.GetBuybackCount(item);
        float price      = manager.GetBuybackPrice(item);
        int canAfford    = price > 0 ? Mathf.FloorToInt(manager.CoinBalance / price) : 0;
        int canBuyback   = Mathf.Min(canAfford, available);

        using (new EditorGUILayout.HorizontalScope())
        {
            DrawIcon(item);
            GUILayout.Space(2);
            EditorGUILayout.LabelField(item.ItemName, _centeredLabel);

            // Snapshotted price — tinted blue to distinguish from buy/sell prices
            var prev = GUI.color;
            GUI.color = new Color(0.5f, 0.8f, 1f);
            EditorGUILayout.LabelField($"{price:F1}g", _centeredLabel, GUILayout.Width(ValueWidth));
            GUI.color = prev;

            // Available count
            var bold = new GUIStyle(_centeredLabel) { fontStyle = FontStyle.Bold };
            EditorGUILayout.LabelField(available.ToString(), bold, GUILayout.Width(CountWidth));

            // Can buyback — green if > 0, grey otherwise
            GUI.color = canBuyback > 0 ? new Color(0.4f, 1f, 0.4f) : new Color(0.6f, 0.6f, 0.6f);
            EditorGUILayout.LabelField(canBuyback.ToString(), bold, GUILayout.Width(CountWidth));
            GUI.color = prev;
        }
        DrawDivider(new Color(0.3f, 0.3f, 0.3f, 0.3f));
    }

    // -------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------

    private void DrawIcon(ItemDefinition item)
    {
        Rect iconRect = GUILayoutUtility.GetRect(IconWidth, IconWidth,
            GUILayout.Width(IconWidth), GUILayout.Height(IconWidth));
        if (item.Icon != null)
            GUI.DrawTexture(iconRect, item.Icon.texture, ScaleMode.ScaleToFit);
        else
            EditorGUI.DrawRect(iconRect, new Color(0.25f, 0.25f, 0.25f, 0.5f));
    }

    private static void DrawDivider(Color color)
    {
        Rect r = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(r, color);
    }

    private void EnsureStyles()
    {
        if (_centeredLabel == null)
            _centeredLabel = new GUIStyle(EditorStyles.label)
                { alignment = TextAnchor.MiddleCenter };

        if (_headerLabel == null)
            _headerLabel = new GUIStyle(EditorStyles.boldLabel)
                { alignment = TextAnchor.MiddleCenter };

        if (_sectionLabel == null)
            _sectionLabel = new GUIStyle(EditorStyles.boldLabel)
                { alignment = TextAnchor.MiddleCenter, fontSize = 11 };

        if (_balanceLabel == null)
            _balanceLabel = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                normal = { textColor = new Color(0.95f, 0.8f, 0.2f) }
            };
    }
}