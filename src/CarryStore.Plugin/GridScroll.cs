using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Sirro.BZ.MP.CarryStore
{
    /// <summary>
    /// Native runtime-only ScrollRect for expanded grids, without BepInEx, Nautilus or Unity dll references.
    /// Uses the game's existing uGUI_InventoryTab + uGUI_ItemsContainer and UnityEngine.UI components.
    /// The viewport shows 7 columns x 8 rows, scrolling the 7x10 player and 7x15 storage grids.
    /// </summary>
    internal static class GridScroll
    {
        private const float CellSize = 71f;
        private const int VisibleColumns = 7;
        private const int VisibleRows = 8;
        private const string MaskName = "CarryStoreMask";

        private sealed class ScrollWrapper
        {
            internal object ScrollObject = null!;
            internal object ScrollTransform = null!;
            internal object Mask = null!;
            internal object ScrollRect = null!;
            internal object OriginalParent = null!;
        }

        // Each PDA visual owns at most one reusable ScrollView, even when the player
        // repeatedly switches locker -> reactor/upgrade panel -> locker.
        // ConditionalWeakTable does not retain destroyed/collected UI components.
        private static readonly ConditionalWeakTable<object, ScrollWrapper> Wrappers =
            new ConditionalWeakTable<object, ScrollWrapper>();

        internal static void Refresh(object? tab)
        {
            var inventory = R.Get(tab, "inventory");
            Ensure(R.Get(inventory, "inventory"));
            Ensure(R.Get(inventory, "storage"));
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
            var x = container == null ? 0 : Convert.ToInt32(R.Get(container, "sizeX") ?? 0);
            var y = container == null ? 0 : Convert.ToInt32(R.Get(container, "sizeY") ?? 0);
            bool managed = x == 7 && ((isPlayer && y == 10) || (isStorage && y == 15));
            if (!managed)
            {
                // CRITICAL: uGUI_Inventory.storage is reused for every container type.
                // Our previous Close() left that shared grid INSIDE a disabled ScrollView.
                // A later Init() of a vanilla/upgrade/planter grid would return here,
                // leaving the content and all its items invisible. Always detach it.
                RestoreOriginalPanel(visual, x, y);
                return;
            }

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

            if (!string.Equals(R.Get(parent, "name")?.ToString(), MaskName, StringComparison.Ordinal)
                && Wrappers.TryGetValue(visual, out var reusable))
            {
                scrollObject = reusable.ScrollObject;
                scrollTransform = reusable.ScrollTransform;
                mask = reusable.Mask;
                scroll = reusable.ScrollRect;
                R.Call(transform, "SetParent", mask, false);
                R.Call(scrollObject, "SetActive", true);
            }
            else if (string.Equals(R.Get(parent, "name")?.ToString(), MaskName, StringComparison.Ordinal))
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
                scrollTransform = R.Get(scrollObject, "transform")
                    ?? throw new MissingMemberException("ScrollView RectTransform after addition");

                var maskObject = Activator.CreateInstance(gameObjectType, new object[] { MaskName })
                    ?? throw new InvalidOperationException("Cannot create viewport GameObject");
                mask = R.Get(maskObject, "transform")
                    ?? throw new MissingMemberException("viewport transform");
                R.Call(mask, "SetParent", scrollTransform, false);
                R.Call(maskObject, "AddComponent", rectType);
                mask = R.Get(maskObject, "transform")
                    ?? throw new MissingMemberException("viewport RectTransform after addition");
                R.Call(maskObject, "AddComponent", rectMaskType);
                R.Call(transform, "SetParent", mask, false);

                scroll = R.Call(scrollObject, "AddComponent", scrollType)
                    ?? throw new InvalidOperationException("Cannot create ScrollRect");
                R.Set(scroll, "viewport", mask);
                R.Set(scroll, "content", gridTransform);
                R.Set(scroll, "horizontal", false);
                R.Set(scroll, "vertical", true);
                var enumType = scrollType.GetProperty("movementType", R.All)?.PropertyType
                    ?? throw new MissingMemberException("ScrollRect.movementType");
                R.Set(scroll, "movementType", Enum.Parse(enumType, "Clamped"));
                R.Set(scroll, "scrollSensitivity", 50f);
                Wrappers.Add(visual, new ScrollWrapper
                {
                    ScrollObject = scrollObject,
                    ScrollTransform = scrollTransform,
                    ScrollRect = scroll,
                    Mask = mask,
                    OriginalParent = parent
                });
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

        private static void RestoreOriginalPanel(object visual, int width, int height)
        {
            var transform = R.Get(visual, "transform");
            var mask = R.Get(transform, "parent");
            if (!string.Equals(R.Get(mask, "name")?.ToString(), MaskName, StringComparison.Ordinal))
                return;

            var scrollTransform = R.Get(mask, "parent")
                ?? throw new MissingMemberException("CarryStoreMask parent missing");
            var originalParent = Wrappers.TryGetValue(visual, out var cached)
                ? cached.OriginalParent : R.Get(scrollTransform, "parent")
                    ?? throw new MissingMemberException("CarryStoreScrollView original parent missing");

            // Reactivate real GUI grid BEFORE tearing down the wrapper; no object
            // or inventory item is destroyed. An inactive wrapper cannot hide a
            // later vanilla storage panel after it has been reparented.
            R.Call(transform, "SetParent", originalParent, false);
            var scrollGo = cached?.ScrollObject ?? R.Get(scrollTransform, "gameObject");
            if (scrollGo != null) R.Call(scrollGo, "SetActive", false);

            if (width > 0 && height > 0)
            {
                // The vanilla Init was called while the old ScrollRect was inactive.
                // Re-fire the ordinary UI geometry event on the restored panel.
                R.Call(visual, "OnResize", width, height);
            }
            Diagnostic.Info("UI_SCROLL_DETACHED: unmodified " + width + "x" + height +
                " panel restored with its items intact");
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
