using System;
using System.Reflection;

namespace Sirro.BZ.MP.CarryStore
{
    /// <summary>
    /// Native runtime-only ScrollRect for expanded grids, without BepInEx, Nautilus or Unity dll references.
    /// Uses the game's existing uGUI_InventoryTab + uGUI_ItemsContainer and UnityEngine.UI components.
    /// The viewport shows 7 columns x 8 rows, scrolling to the full 8x10 extent.
    /// </summary>
    internal static class GridScroll
    {
        private const float CellSize = 71f;
        private const int VisibleColumns = 7;
        private const int VisibleRows = 8;
        private const string MaskName = "CarryStoreMask";

        internal static void Refresh(object? tab)
        {
            var inventory = R.Get(tab, "inventory");
            Ensure(inventory);
            Ensure(R.Get(tab, "storage"));
        }

        internal static void Ensure(object? visual)
        {
            if (visual == null) return;
            var tabs = R.Get(visual, "inventory");
            if (tabs == null) return;
            var playerGrid = R.Get(tabs, "inventory");
            var storageGrid = R.Get(tabs, "storage");
            bool isPlayer = ReferenceEquals(playerGrid, visual);
            bool isStorage = ReferenceEquals(storageGrid, visual);
            if (!isPlayer && !isStorage) return;

            var container = R.Get(visual, "container");
            if (container == null) return;
            var x = Convert.ToInt32(R.Get(container, "sizeX") ?? 0);
            var y = Convert.ToInt32(R.Get(container, "sizeY") ?? 0);
            // Ignore vanilla/other plugin grids; scrolling is for exactly our expanded 8x10 containers.
            if (x != 8 || y != 10) return;

            var gridTransform = R.Get(visual, "rectTransform")
                ?? throw new MissingMemberException("uGUI_ItemsContainer.rectTransform");
            var transform = R.Get(visual, "transform")
                ?? throw new MissingMemberException("uGUI_ItemsContainer.transform");
            var parent = R.Get(transform, "parent")
                ?? throw new MissingMemberException("uGUI_ItemsContainer.transform.parent");

            var scrollType = R.Type("UnityEngine.UI.ScrollRect")
                ?? throw new TypeLoadException("UnityEngine.UI.ScrollRect");
            var rectType = R.Type("UnityEngine.RectTransform")
                ?? throw new TypeLoadException("UnityEngine.RectTransform");
            var rectMaskType = R.Type("UnityEngine.UI.RectMask2D")
                ?? throw new TypeLoadException("UnityEngine.UI.RectMask2D");
            var gameObjectType = R.Type("UnityEngine.GameObject")
                ?? throw new TypeLoadException("UnityEngine.GameObject");

            object scrollObject;
            object mask;
            object scroll;
            object scrollTransform;

            if (string.Equals(R.Get(parent, "name")?.ToString(), MaskName, StringComparison.Ordinal))
            {
                mask = parent;
                scrollTransform = R.Get(parent, "parent")
                    ?? throw new MissingMemberException("Existing CarryStoreMask lacks parent");
                scrollObject = R.Get(scrollTransform, "gameObject")
                    ?? throw new MissingMemberException("Existing scroll transform lacks gameObject");
                scroll = R.Call(scrollObject, "GetComponent", scrollType)
                    ?? throw new MissingMemberException("Existing CarryStoreScroll lacks ScrollRect");
                R.Call(scrollObject, "SetActive", true);
            }
            else
            {
                scrollObject = Activator.CreateInstance(gameObjectType, new object[] { "CarryStoreScrollView" })
                    ?? throw new InvalidOperationException("Cannot create ScrollView GameObject");
                scrollTransform = R.Get(scrollObject, "transform")
                    ?? throw new MissingMemberException("ScrollView transform");
                R.Call(scrollTransform, "SetParent", parent, false);
                R.Call(scrollObject, "AddComponent", rectType);

                var maskObject = Activator.CreateInstance(gameObjectType, new object[] { MaskName })
                    ?? throw new InvalidOperationException("Cannot create viewport GameObject");
                mask = R.Get(maskObject, "transform")
                    ?? throw new MissingMemberException("viewport transform");
                R.Call(mask, "SetParent", scrollTransform, false);
                R.Call(maskObject, "AddComponent", rectType);
                R.Call(maskObject, "AddComponent", rectMaskType);
                R.Call(transform, "SetParent", mask, false);

                scroll = R.Call(scrollObject, "AddComponent", scrollType)
                    ?? throw new InvalidOperationException("Cannot create ScrollRect");
                R.Set(scroll, "viewport", mask);
                R.Set(scroll, "content", gridTransform);
                R.Set(scroll, "horizontal", true);
                R.Set(scroll, "vertical", true);
                var enumType = scrollType.GetProperty("movementType", R.All)?.PropertyType
                    ?? throw new MissingMemberException("ScrollRect.movementType");
                R.Set(scroll, "movementType", Enum.Parse(enumType, "Clamped"));
                R.Set(scroll, "scrollSensitivity", 50f);
                Diagnostic.Info("UI_SCROLL_CREATED: " + (isPlayer ? "inventory" : "storage"));
            }

            var scrollRect = R.Get(scrollTransform, "gameObject") == null
                ? null : R.Call(scrollObject, "GetComponent", rectType);
            var maskRect = R.Get(mask, "gameObject") == null
                ? null : R.Call(R.Get(mask, "gameObject"), "GetComponent", rectType);
            if (scrollRect == null || maskRect == null)
                throw new MissingMemberException("ScrollView RectTransform missing");

            float viewWidth = VisibleColumns * CellSize + 20f;
            float viewHeight = VisibleRows * CellSize + 20f;
            R.Set(scrollRect, "sizeDelta", V2(viewWidth, viewHeight));
            R.Set(scrollRect, "anchorMin", V2(isPlayer ? 0.25f : 0.75f, 0.5f));
            R.Set(scrollRect, "anchorMax", V2(isPlayer ? 0.25f : 0.75f, 0.5f));
            R.Set(scrollRect, "anchoredPosition", V2(isPlayer ? CellSize/4f : -CellSize/4f, -CellSize));
            R.Set(maskRect, "sizeDelta", V2(viewWidth, viewHeight));
            R.Set(maskRect, "anchoredPosition", V2(0, 0));

            R.Set(gridTransform, "anchorMin", V2(.5f, .5f));
            R.Set(gridTransform, "anchorMax", V2(.5f, .5f));
            R.Set(gridTransform, "sizeDelta", V2(x*CellSize, y*CellSize));
            R.Set(gridTransform, "anchoredPosition", V2(0, 0));
            R.Set(scroll, "horizontalNormalizedPosition", 0f);
            R.Set(scroll, "verticalNormalizedPosition", 1f);
        }

        internal static void Close(object? visual)
        {
            if (visual == null) return;
            var transform = R.Get(visual, "transform");
            var mask = R.Get(transform, "parent");
            if (!string.Equals(R.Get(mask, "name")?.ToString(), MaskName, StringComparison.Ordinal))
                return;
            var scroll = R.Get(mask, "parent");
            var go = R.Get(scroll, "gameObject");
            if (go != null) R.Call(go, "SetActive", false);
        }

        private static object V2(float x, float y)
        {
            var type = R.Type("UnityEngine.Vector2") ?? throw new TypeLoadException("UnityEngine.Vector2");
            return Activator.CreateInstance(type, new object[] { x, y })
                ?? throw new MissingMethodException("UnityEngine.Vector2(float,float)");
        }
    }
}
