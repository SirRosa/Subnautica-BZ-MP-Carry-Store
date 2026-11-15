using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Sirro.BZ.MP.CarryStore
{
    internal static class RuntimeHooks
    {
        internal const string OwnerId = "sirro.bz.mp.carry-store.301";
        private static object? harmony;
        private static volatile bool fatalFault;
        internal static bool IsFaulted => fatalFault;
        internal static void ReportFault(string area, Exception error)
        {
            fatalFault = true;
            Diagnostic.Info("CRITICAL_RUNTIME_FAULT: " + area + " " + error.GetType().Name + " " + error.Message + ". STOP GAMEPLAY, EXIT SESSION, restore backup; further CarryStore hooks disabled.");
        }
        private static Type? harmonyMethodType;
        private static MethodInfo? patchMethod;
        private static MethodInfo? unpatchAll;

        internal static void Install()
        {
            if (harmony != null) throw new InvalidOperationException("CarryStore hooks already installed");
            fatalFault = false;
            // Validate every critical target BEFORE applying any patch.
            var targets = BuildTargets();
            var harmonyType = R.Type("HarmonyLib.Harmony")
                ?? throw new MissingMemberException("HarmonyLib.Harmony");
            harmonyMethodType = R.Type("HarmonyLib.HarmonyMethod")
                ?? throw new MissingMemberException("HarmonyLib.HarmonyMethod");
            patchMethod = harmonyType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "Patch" && m.GetParameters().Length >= 3 &&
                    m.GetParameters()[0].ParameterType == typeof(MethodBase))
                ?? throw new MissingMemberException("Harmony.Patch");
            unpatchAll = harmonyType.GetMethod("UnpatchAll", BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(string) }, null)
                ?? throw new MissingMemberException("Harmony.UnpatchAll(string)");
            harmony = Activator.CreateInstance(harmonyType, new object[] { OwnerId })
                ?? throw new InvalidOperationException("Harmony constructor returned null");

            try
            {
                foreach (var t in targets)
                {
                    var callback = typeof(Hooks).GetMethod(t.Callback, BindingFlags.Static | BindingFlags.Public)
                        ?? throw new MissingMethodException("Hooks." + t.Callback);
                    var patchInfo = Activator.CreateInstance(harmonyMethodType, new object[] { callback })
                        ?? throw new InvalidOperationException("HarmonyMethod construction failed");
                    var args = new object?[patchMethod.GetParameters().Length];
                    args[0] = t.Target;
                    args[t.Prefix ? 1 : 2] = patchInfo;
                    patchMethod.Invoke(harmony, args);
                }
                VerifyPatchOwners(targets);
                Diagnostic.Info("PATCHES=" + targets.Count + "; pluginType=ServerClient; inventory=7x10; storages=7x15; scanner=3x3; items=1x1");
            }
            catch
            {
                Uninstall();
                throw;
            }
        }

        private static void VerifyPatchOwners(List<PatchTarget> targets)
        {
            var harmonyType = R.Type("HarmonyLib.Harmony")
                ?? throw new TypeLoadException("HarmonyLib.Harmony");
            var infoGetter = harmonyType.GetMethod("GetPatchInfo", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(MethodBase) }, null)
                ?? throw new MissingMethodException("Harmony.GetPatchInfo(MethodBase)");

            foreach (var target in targets)
            {
                var info = infoGetter.Invoke(null, new object[] { target.Target });
                if (info == null) throw new InvalidOperationException("Harmony reports no patches on " + target.Target);
                var group = R.Get(info, target.Prefix ? "Prefixes" : "Postfixes") as IEnumerable;
                if (group == null) throw new InvalidOperationException("Harmony patch info missing owner list: " + target.Target);

                bool found = false;
                foreach (var entry in group)
                {
                    if (string.Equals(R.Get(entry, "owner")?.ToString(), OwnerId,
                        StringComparison.Ordinal))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                    throw new InvalidOperationException("CarryStore patch missing or overwritten: " + target.Target);
            }
        }

        internal static void Uninstall()
        {
            try
            {
                if (harmony != null) unpatchAll?.Invoke(harmony, new object[] { OwnerId });
            }
            catch (Exception e) { Diagnostic.Error("UNPATCH", e); }
            finally { harmony = null; fatalFault = false; }
        }

        private sealed class PatchTarget
        {
            internal MethodInfo Target = null!;
            internal string Callback = string.Empty;
            internal bool Prefix;
        }

        private static List<PatchTarget> BuildTargets()
        {
            var patch = new List<PatchTarget>();
            void Add(string type, string method, int paramCount, string cb, bool prefix = false)
            {
                var runtimeType = R.Type(type) ?? throw new TypeLoadException(type);
                var methods = runtimeType.GetMethods(R.All)
                    .Where(m => m.Name == method && m.GetParameters().Length == paramCount).ToArray();
                if (methods.Length != 1)
                    throw new MissingMethodException(type + "." + method + "/" + paramCount +
                        " (matches " + methods.Length + ")");
                patch.Add(new PatchTarget { Target = methods[0], Callback = cb, Prefix = prefix });
            }

            // Exact method counts/signatures verified against the user's six DLL files.
            Add("TechData", "GetItemSize", 1, nameof(Hooks.ItemSizePrefix), true);
            if (patch[0].Target.ReturnType != typeof(Vector2int))
                throw new InvalidOperationException("TechData.GetItemSize return type changed");

            Add("Inventory", "Awake", 0, nameof(Hooks.PlayerInventoryPostfix));
            Add("Inventory", "ResetInventory", 0, nameof(Hooks.PlayerInventoryPostfix));
            Add("Inventory", "UpdateContainers", 0, nameof(Hooks.PlayerInventoryPostfix));
            Add("Inventory", "OnProtoDeserialize", 1, nameof(Hooks.PlayerInventoryPostfix));
            Add("uGUI_InventoryTab", "Open", 0, nameof(Hooks.InventoryUiPostfix));
            Add("uGUI_InventoryTab", "OnOpenPDA", 2, nameof(Hooks.InventoryPdaOpenedPostfix));
            Add("uGUI_ItemsContainer", "Init", 1, nameof(Hooks.ItemsContainerInitPostfix));
            Add("uGUI_ItemsContainer", "Uninit", 0, nameof(Hooks.ItemsContainerUninitPostfix));

            Add("StorageContainer", "Awake", 0, nameof(Hooks.LockerLifecyclePostfix));
            Add("StorageContainer", "ResetContainer", 0, nameof(Hooks.LockerLifecyclePostfix));
            Add("StorageContainer", "OnOpen", 0, nameof(Hooks.LockerLifecyclePostfix));
            Add("StorageContainer", "Resize", 2, nameof(Hooks.LockerResizePrefix), true);

            Add("MapRoomFunctionality", "Start", 0, nameof(Hooks.ScannerLifecyclePostfix));
            Add("MapRoomFunctionality", "OnPostRebuildGeometry", 1, nameof(Hooks.ScannerLifecyclePostfix));
            Add("MapRoomFunctionality", "IsAllowedToAdd", 2, nameof(Hooks.ScannerCanAddPrefix), true);
            Add("MapRoomFunctionality", "AddItem", 1, nameof(Hooks.ScannerCanAddPrefix), true);

            Add("Subnautica.Server.Core.AuthorizationProfile", "Initialize", 2, nameof(Hooks.ServerPlayerInitializePostfix));
            Add("Subnautica.Server.Processors.Metadata.StorageProcessor", "GetStorageContainer", 1, nameof(Hooks.ServerLockerPostfix));
            Add("Subnautica.Server.Processors.Metadata.FridgeProcessor", "GetStorageContainer", 1, nameof(Hooks.ServerLockerPostfix));
            Add("Subnautica.Server.Processors.General.LifepodProcessor", "GetStorageContainer", 1, nameof(Hooks.ServerLockerPostfix));
            Add("Subnautica.Server.Processors.Vehicle.SeaTruckStorageModuleProcessor", "GetStorageContainer", 1, nameof(Hooks.ServerLockerPostfix));

            // Explicit inventory-bearing network metadata. These accessors are used by
            // server storage processors; the aquarium, bioreactor, planter and module
            // equipment containers intentionally have NO bulk-storage hooks.
            Add("Subnautica.Network.Models.WorldEntity.DynamicEntityComponents.SmallStorage", "get_StorageContainer", 0, nameof(Hooks.ServerLockerPostfix));
            Add("Subnautica.Network.Models.WorldEntity.DynamicEntityComponents.Shared.SeaTruckLockerItem", "get_StorageContainer", 0, nameof(Hooks.ServerLockerPostfix));
            Add("Subnautica.Network.Models.WorldEntity.DynamicEntityComponents.Exosuit", "get_StorageContainer", 0, nameof(Hooks.ServerLockerPostfix));
            Add("Subnautica.Network.Models.Metadata.Fridge", "get_StorageContainer", 0, nameof(Hooks.ServerLockerPostfix));
            Add("Subnautica.Network.Models.Storage.World.Childrens.SupplyDrop", "get_StorageContainer", 0, nameof(Hooks.ServerLockerPostfix));
            Add("Subnautica.Network.Models.Storage.World.World", "get_QuantumLocker", 0, nameof(Hooks.ServerLockerPostfix));
            Add("Subnautica.Network.Models.WorldEntity.DynamicEntityComponents.Exosuit", "ResizeStorageContainer", 0, nameof(Hooks.ServerExosuitResizedPostfix));
            Add("Subnautica.Server.Processors.Metadata.BaseMapRoomProcessor", "OnDataReceived", 3, nameof(Hooks.ServerScannerPrefix), true);

            var storageItem = R.Type("Subnautica.Network.Models.Metadata.StorageItem")
                ?? throw new TypeLoadException("Metadata.StorageItem");
            var storageContainer = R.Type("Subnautica.Network.Models.Metadata.StorageContainer")
                ?? throw new TypeLoadException("Metadata.StorageContainer");
            if (storageItem.GetProperty("Size", R.All)?.PropertyType != typeof(byte) ||
                storageContainer.GetProperty("Size", R.All)?.PropertyType != typeof(byte))
                throw new InvalidOperationException("Network metadata Size is not byte");

            foreach (var creator in storageItem.GetMethods(R.All)
                .Where(m => m.IsStatic && m.Name == "Create" &&
                    m.ReturnType == storageItem).ToArray())
                patch.Add(new PatchTarget { Target = creator, Callback = nameof(Hooks.ServerItemCreatedPostfix) });
            if (patch.Count(t => t.Callback == nameof(Hooks.ServerItemCreatedPostfix)) < 2)
                throw new MissingMethodException("StorageItem.Create overloads");

            Add("Subnautica.Network.Models.Metadata.StorageItem", "GetSizeX", 0, nameof(Hooks.ServerItemSizePrefix), true);
            Add("Subnautica.Network.Models.Metadata.StorageItem", "GetSizeY", 0, nameof(Hooks.ServerItemSizePrefix), true);
            Add("Subnautica.Network.Models.Metadata.StorageContainer", "HasRoomFor", 1, nameof(Hooks.ServerHasRoomPrefix), true);

            // The runtime will not partially enable if any essential game/server ABI target is unavailable.
            return patch;
        }
    }

    internal static class R
    {
        internal const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;
        private static readonly string[] Known = { "Assembly-CSharp", "Subnautica.Server",
            "Subnautica.Network", "Subnautica.API", "Subnautica.Client", "Subnautica.Events" };

        internal static Type? Type(string name)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetType(name, false) is Type found) return found;
            foreach (var nameOfAssembly in Known)
            {
                try
                {
                    var assembly = Assembly.Load(nameOfAssembly);
                    if (assembly.GetType(name, false) is Type found) return found;
                }
                catch { }
            }
            return null;
        }

        internal static object? Get(object? target, string name)
        {
            if (target == null) return null;
            var type = target as Type ?? target.GetType();
            var instance = target is Type ? null : target;
            var property = type.GetProperty(name, All);
            if (property != null) return property.GetValue(instance, null);
            return type.GetField(name, All)?.GetValue(instance);
        }

        internal static bool Set(object? target, string name, object? value)
        {
            if (target == null) return false;
            var type = target.GetType();
            var property = type.GetProperty(name, All);
            if (property != null && property.CanWrite) { property.SetValue(target, value, null); return true; }
            var field = type.GetField(name, All);
            if (field != null) { field.SetValue(target, value); return true; }
            return false;
        }

        internal static object? Call(object? target, string name, params object?[] args)
        {
            if (target == null) return null;
            var type = target as Type ?? target.GetType();
            var method = type.GetMethods(All).FirstOrDefault(m =>
                m.Name == name && m.GetParameters().Length == args.Length && !m.IsGenericMethodDefinition);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            return method.Invoke(target is Type ? null : target, args);
        }

        internal static object? Component(object obj, Type type)
        {
            var methods = obj.GetType().GetMethods(All).Where(m =>
                m.Name == "GetComponent" && m.IsGenericMethodDefinition &&
                m.GetParameters().Length == 0).ToArray();
            if (methods.Length == 0) return null;
            return methods[0].MakeGenericMethod(type).Invoke(obj, null);
        }

        internal static bool Size(object? container, int width, int height)
        {
            if (container == null) return false;
            object? w = Get(container, "sizeX") ?? Get(container, "GetSizeX");
            object? h = Get(container, "sizeY") ?? Get(container, "GetSizeY");
            // Fallback method getters (no properties) on the server metadata container.
            if (w == null && container.GetType().GetMethod("GetSizeX", All) != null) w = Call(container, "GetSizeX");
            if (h == null && container.GetType().GetMethod("GetSizeY", All) != null) h = Call(container, "GetSizeY");
            if (w != null && h != null && Convert.ToInt32(w) == width && Convert.ToInt32(h) == height)
                return false;
            var resize = container.GetType().GetMethods(All).SingleOrDefault(m =>
                m.Name == "Resize" && m.GetParameters().Length == 2);
            if (resize == null) throw new MissingMethodException(container.GetType().FullName, "Resize");
            var parameters = resize.GetParameters();
            var x = Convert.ChangeType(width, parameters[0].ParameterType);
            var y = Convert.ChangeType(height, parameters[1].ParameterType);
            resize.Invoke(container, new[] { x, y });
            return true;
        }
    }
}
