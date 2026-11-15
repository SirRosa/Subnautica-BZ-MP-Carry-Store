using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace Sirro.BZ.MP.CarryStore
{
    public static class Hooks
    {
        // Signature verified from actual Assembly-CSharp.dll: Vector2int TechData.GetItemSize(TechType).
        public static bool ItemSizePrefix(ref Vector2int __result)
        {
            if (RuntimeHooks.IsFaulted) return true;
            __result = new Vector2int(1, 1);
            return false;
        }

        public static void PlayerInventoryPostfix(object __instance)
        {
            Safe("INVENTORY", () =>
            {
                var grid = R.Get(__instance, "container");
                R.Size(grid, 7, 10);
            });
        }

        public static void InventoryUiPostfix()
        {
            Safe("INVENTORY_UI", () =>
            {
                var type = R.Type("Inventory");
                var inventory = type == null ? null : R.Get(type, "main");
                if (inventory != null) PlayerInventoryPostfix(inventory);
            });
        }

        public static void ItemsContainerInitPostfix(object __instance)
        {
            Safe("UI_INIT", () => GridScroll.Ensure(__instance));
        }

        public static void ItemsContainerUninitPostfix(object __instance)
        {
            Safe("UI_UNINIT", () => GridScroll.Close(__instance));
        }

        public static void InventoryPdaOpenedPostfix(object __instance)
        {
            Safe("PDA_OPEN", () => GridScroll.Refresh(__instance));
        }

        public static void LockerLifecyclePostfix(object __instance)
        {
            Safe("LOCKER", () =>
            {
                if (!IsRealStorage(__instance)) return;
                R.Set(__instance, "width", 7);
                R.Set(__instance, "height", 15);
                R.Size(R.Get(__instance, "container"), 7, 15);
            });
        }

        public static void LockerResizePrefix(object __instance, ref int width, ref int height)
        {
            if (RuntimeHooks.IsFaulted) return;
            // C# forbids capturing ref parameters in a lambda.
            try
            {
                if (!IsRealStorage(__instance)) return;
                width = 7;
                height = 15;
            }
            catch (Exception error) { RuntimeHooks.ReportFault("LOCKER_RESIZE", error); }
        }

        public static void ScannerLifecyclePostfix(object __instance)
        {
            Safe("SCANNER", () => ResizeScanner(__instance));
        }

        public static void ScannerCanAddPrefix(object __instance)
        {
            Safe("SCANNER_ADD", () => ResizeScanner(__instance));
        }

        public static void ServerPlayerInitializePostfix(object __result)
        {
            Safe("SERVER_PLAYER", () =>
            {
                if (__result == null) return;
                var grid = R.Get(__result, "InventoryItems") ?? throw new InvalidOperationException("Player profile missing InventoryItems after Initialize");
                ResizeNetwork(grid, 7, 10);
                if (!GridMatches(grid, 7, 10)) throw new InvalidOperationException("Server player inventory was not resized to 7x10");
            });
        }

        public static void ServerLockerPostfix(object __result)
        {
            // Only registered on known permanent-storage metadata accessors.
            Safe("SERVER_STORAGE", () => ResizeNetwork(__result, 7, 15));
        }

        public static void ServerExosuitResizedPostfix(object __instance)
        {
            Safe("SERVER_EXOSUIT", () => ResizeNetwork(R.Get(__instance, "StorageContainer"), 7, 15));
        }

        public static void ServerScannerPrefix(object[] __args)
        {
            Safe("SERVER_SCANNER", () =>
            {
                if (__args == null || __args.Length != 3 || __args[1] == null || __args[2] == null)
                    return;
                object packet = __args[1];
                if (R.Get(packet, "SecretTechType")?.ToString() == "Fabricator")
                    return;

                var metadataType = R.Type("Subnautica.Network.Models.Metadata.BaseMapRoom");
                var packetMetadata = R.Get(packet, "Component");
                if (metadataType == null || packetMetadata == null) return;

                var getIncoming = packetMetadata.GetType().GetMethods(R.All).FirstOrDefault(m =>
                    m.Name == "GetComponent" && m.IsGenericMethodDefinition &&
                    m.GetParameters().Length == 0);
                if (getIncoming == null ||
                    getIncoming.MakeGenericMethod(metadataType).Invoke(packetMetadata, null) == null)
                    return;

                // Exactly the same construction.EnsureComponent<T>() as the original server processor.
                object construction = __args[2];
                var ensure = construction.GetType().GetMethods(R.All).FirstOrDefault(m =>
                    m.Name == "EnsureComponent" && m.IsGenericMethodDefinition &&
                    m.GetParameters().Length == 0)
                    ?? throw new MissingMethodException("ConstructionItem.EnsureComponent<T>()");
                var mapRoom = ensure.MakeGenericMethod(metadataType).Invoke(construction, null);
                if (mapRoom == null) return;

                var grid = R.Get(mapRoom, "StorageContainer");
                if (grid == null)
                {
                    var networkContainer = R.Type("Subnautica.Network.Models.Metadata.StorageContainer")
                        ?? throw new TypeLoadException("Network StorageContainer");
                    var create = networkContainer.GetMethod("Create", R.All, null,
                        new[] { typeof(byte), typeof(byte) }, null)
                        ?? throw new MissingMethodException("Network StorageContainer.Create");
                    grid = create.Invoke(null, new object[] { (byte)3, (byte)3 });
                    R.Set(mapRoom, "StorageContainer", grid);
                }
                ResizeNetwork(grid, 3, 3);
            });
        }

        public static void ServerItemCreatedPostfix(object __result)
        {
            Safe("SERVER_ITEM_CREATED", () =>
            {
                if (__result != null && !R.Set(__result, "Size", (byte)11))
                    throw new InvalidOperationException("StorageItem.Size mutation failed");
            });
        }

        // Separate server-side default item size, not just a local UI patch.
        public static bool ServerItemSizePrefix(ref byte __result)
        {
            if (RuntimeHooks.IsFaulted) return true;
            __result = 1;
            return false;
        }

        public static void ServerHasRoomPrefix(object __instance, object[] __args)
        {
            Safe("SERVER_HAS_ROOM", () =>
            {
                NormalizeItems(__instance);
                if (__args != null && __args.Length > 0 && __args[0] != null)
                    R.Set(__args[0], "Size", (byte)11);
            });
        }

        private static void ResizeNetwork(object? networkContainer, int width, int height)
        {
            if (networkContainer == null) return;
            NormalizeItems(networkContainer);
            R.Size(networkContainer, width, height);
            if (!GridMatches(networkContainer, width, height))
                throw new InvalidOperationException("Network container dimensions did not persist");
        }

        private static bool GridMatches(object container, int width, int height)
        {
            var x = R.Call(container, "GetSizeX");
            var y = R.Call(container, "GetSizeY");
            return Convert.ToInt32(x) == width && Convert.ToInt32(y) == height;
        }

        private static void NormalizeItems(object container)
        {
            if (!(R.Get(container, "Items") is IEnumerable list)) return;
            foreach (var item in list)
                if (item != null && !Equals(R.Get(item, "Size"), (byte)11) &&
                    !R.Set(item, "Size", (byte)11))
                    throw new InvalidOperationException("Could not migrate existing StorageItem.Size");
        }

        private static void ResizeScanner(object instance)
        {
            var holder = R.Get(instance, "storageContainer");
            R.Size(R.Get(holder, "container"), 3, 3);
        }

        private static readonly string[] ExcludedStorageParents =
        {
            "MapRoomFunctionality", "Planter", "LargeRoomWaterParkPlanter",
            "Aquarium", "SeaTruckAquarium", "BaseBioReactorGeometry", "BaseBioReactor",
            "BaseNuclearReactorGeometry", "BaseNuclearReactor", "Charger",
            "BatteryCharger", "PowerCellCharger", "VehicleUpgradeConsoleInput",
            "UpgradeConsole", "BaseUpgradeConsoleGeometry", "NuclearReactor",
            "FiltrationMachine", "BaseFiltrationMachineGeometry",
            "ExosuitTorpedoArm", "SeamothTorpedo", "RestrictStorageContainer"
        };

        private static readonly string[] ExcludedConstructables =
        {
            "BaseBioReactor", "BaseNuclearReactor", "BaseMapRoom", "PlanterBox",
            "PlanterPot", "BasePlanter", "Aquarium", "SeaTruckAquariumModule",
            "BatteryCharger", "PowerCellCharger", "VehicleUpgradeConsole",
            "BaseUpgradeConsole", "FiltrationMachine", "BaseFiltrationMachine",
            "WaterFiltrationMachine"
        };

        private static object? GetParentComponent(object instance, Type type)
        {
            var methods = instance.GetType().GetMethods(R.All);
            var overload = methods.FirstOrDefault(m => m.Name == "GetComponentInParent" &&
                !m.IsGenericMethodDefinition && m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType == typeof(Type));
            if (overload != null) return overload.Invoke(instance, new object[] { type });

            // Unity supports generic GetComponentInParent<T>() on some game versions.
            var generic = methods.FirstOrDefault(m => m.Name == "GetComponentInParent" &&
                m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
            return generic?.MakeGenericMethod(type).Invoke(instance, null);
        }

        private static bool IsRealStorage(object instance)
        {
            // Equipment and planters have their own slot semantics and must NEVER be
            // transformed into a bulk ItemsContainer. Scanner upgrades are handled 3x3 separately.
            foreach (string name in ExcludedStorageParents)
            {
                var component = R.Type(name);
                if (component != null && GetParentComponent(instance, component) != null)
                    return false;
            }

            var constructable = R.Type("Constructable");
            var tech = constructable == null ? null : R.Get(GetParentComponent(instance, constructable), "techType")?.ToString();
            if (tech != null && ExcludedConstructables.Contains(tech, StringComparer.OrdinalIgnoreCase))
                return false;

            // The actual game type StorageContainer is reserved for storage.
            // Whitelist exemptions above prevent enlarging non-storage machinery.
            return true;
        }

        private static void Safe(string area, Action operation)
        {
            if (RuntimeHooks.IsFaulted) return;
            try { operation(); }
            catch (Exception error) { RuntimeHooks.ReportFault(area, error); }
        }
    }
}
