using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom editor for PlayerInventory.
/// Displays the live runtime dictionary as a read-only table during Play Mode,
/// refreshing every editor frame so counts update as they change.
/// </summary>
[CustomEditor(typeof(PlayerInventory))]
public class PlayerInventoryEditor : Editor
{
    private FieldInfo _inventoryField;

    // Column widths
    private const float IconWidth    = 20f;
    private const float CountWidth   = 36f;
    private const float TypeWidth    = 60f;
    private const float FeedWidth    = 28f;
    private const float StoreWidth   = 28f;

    // Cached styles
    private GUIStyle _centeredLabel;
    private GUIStyle _headerLabel;

    private void OnEnable()
    {
        // Grab the private _inventory field via reflection so we never need to
        // make it public or change any runtime code.
        _inventoryField = typeof(PlayerInventory)
            .GetField("_inventory", BindingFlags.NonPublic | BindingFlags.Instance);
    }

    public override void OnInspectorGUI()
    {
        // Always draw the default inspector (Starting Items list, etc.) first.
        DrawDefaultInspector();

        if (!Application.isPlaying)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox("Inventory state is only visible during Play Mode.", MessageType.Info);
            return;
        }

        EnsureStyles();

        var inventory = target as PlayerInventory;
        var dict = _inventoryField?.GetValue(inventory)
                       as Dictionary<ItemDefinition, int>;

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Inventory", _headerLabel);
        EditorGUILayout.Space(4);

        if (dict == null || dict.Count == 0)
        {
            EditorGUILayout.HelpBox("Inventory is empty.", MessageType.None);
        }
        else
        {
            DrawTableHeader();
            EditorGUILayout.Space(2);

            foreach (var kvp in dict)
                DrawItemRow(kvp.Key, kvp.Value);
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"Total distinct items: {dict?.Count ?? 0}", EditorStyles.miniLabel);

        // Repaint every frame so counts stay live without needing to click.
        EditorUtility.SetDirty(target);
    }

    private void DrawTableHeader()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(IconWidth + 2);
            EditorGUILayout.LabelField("Item",   _centeredLabel);
            EditorGUILayout.LabelField("Type",   _centeredLabel, GUILayout.Width(TypeWidth));
            EditorGUILayout.LabelField("Count",  _centeredLabel, GUILayout.Width(CountWidth));
            EditorGUILayout.LabelField("Feed",   _centeredLabel, GUILayout.Width(FeedWidth));
            EditorGUILayout.LabelField("Store",  _centeredLabel, GUILayout.Width(StoreWidth));
        }

        // Divider
        Rect r = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.5f));
    }

    private void DrawItemRow(ItemDefinition item, int count)
    {
        if (item == null) return;

        using (new EditorGUILayout.HorizontalScope())
        {
            // Icon
            Rect iconRect = GUILayoutUtility.GetRect(IconWidth, IconWidth,
                GUILayout.Width(IconWidth), GUILayout.Height(IconWidth));
            if (item.Icon != null)
                GUI.DrawTexture(iconRect, item.Icon.texture, ScaleMode.ScaleToFit);
            else
                EditorGUI.DrawRect(iconRect, new Color(0.25f, 0.25f, 0.25f, 0.5f));

            GUILayout.Space(2);

            // Name
            EditorGUILayout.LabelField(item.ItemName, _centeredLabel);

            // Type
            EditorGUILayout.LabelField(item.ItemType.ToString(), _centeredLabel,
                GUILayout.Width(TypeWidth));

            // Count (bold when > 0)
            var countStyle = new GUIStyle(_centeredLabel) { fontStyle = FontStyle.Bold };
            EditorGUILayout.LabelField(count.ToString(), countStyle,
                GUILayout.Width(CountWidth));

            // Feedable tick
            DrawTick(item.IsAvailableForFeeding, FeedWidth);

            // Storefront tick
            DrawTick(item.IsAvailableForStorefront, StoreWidth);
        }

        // Row divider
        Rect divider = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(divider, new Color(0.3f, 0.3f, 0.3f, 0.3f));
    }

    private static void DrawTick(bool value, float width)
    {
        var color = GUI.color;
        GUI.color = value ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.4f, 0.4f);
        EditorGUILayout.LabelField(value ? "✓" : "✗",
            new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter },
            GUILayout.Width(width));
        GUI.color = color;
    }

    private void EnsureStyles()
    {
        if (_centeredLabel == null)
        {
            _centeredLabel = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter
            };
        }

        if (_headerLabel == null)
        {
            _headerLabel = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
        }
    }
}