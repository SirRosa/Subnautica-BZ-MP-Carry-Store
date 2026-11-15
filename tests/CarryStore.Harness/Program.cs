using Sirro.BZ.MP.CarryStore;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model = Subnautica.Network.Models.Metadata;

var checkCount = 0;
void Check(bool condition, string note)
{
    if (!condition) throw new Exception("FAIL: " + note);
    checkCount++;
}

// Player inventory: alter the backing ItemsContainer rather than changing only the UI.
var inv = new Inventory();
Hooks.PlayerInventoryPostfix(inv);
Check(inv.container.sizeX == 8 && inv.container.sizeY == 10, "client inventory = 8x10");
Hooks.PlayerInventoryPostfix(inv);
Check(inv.container.resizeCalls == 1, "resize idempotent");
Hooks.InventoryUiPostfix();
Check(inv.container.resizeCalls == 1, "UI open does not repeat resize");

// BZ-specific native scrolling (no BepInEx/AdvancedInventory).
var tab = new uGUI_InventoryTab();
var playerVisual = tab.inventory.inventory;
var playerGrid = new ItemsContainer();
playerGrid.Resize(8, 10);
playerVisual.Init(playerGrid);
Hooks.ItemsContainerInitPostfix(playerVisual);
Check(playerVisual.transform.parent?.name == "CarryStoreMask", "inventory viewport created");
var playerScrollGo = playerVisual.transform.parent!.parent!.gameObject;
var playerScroll = (UnityEngine.UI.ScrollRect)playerScrollGo.GetComponent(typeof(UnityEngine.UI.ScrollRect))!;
Check(playerScroll.viewport != null && playerScroll.content == playerVisual.rectTransform,
    "scrollbar bound to inventory content");
Check(playerScroll.viewport!.sizeDelta.x == 517f && playerScroll.viewport.sizeDelta.y == 588f,
    "7x8 visible viewport with full 8x10 grid");
Check(playerVisual.rectTransform.sizeDelta.x == 568f && playerVisual.rectTransform.sizeDelta.y == 710f,
    "full 8x10 content dimensions");
var storageVisual = tab.inventory.storage;
var storageGrid = new ItemsContainer();
storageGrid.Resize(8, 10);
storageVisual.Init(storageGrid);
Hooks.ItemsContainerInitPostfix(storageVisual);
Check(storageVisual.transform.parent?.name == "CarryStoreMask", "storage viewport created");
Check(storageVisual.transform.parent!.parent!.gameObject != playerScrollGo,
    "separate scrolling view per grid");
Hooks.InventoryPdaOpenedPostfix(tab);
Check(playerScroll.horizontalNormalizedPosition == 0f && playerScroll.verticalNormalizedPosition == 1f,
    "PDA reopens scrolled to top-left");
Hooks.ItemsContainerUninitPostfix(playerVisual);
Check(!playerScrollGo.activeSelf, "scroll view hidden when inventory uninitialized");
Hooks.ItemsContainerInitPostfix(playerVisual);
Check(playerScrollGo.activeSelf, "scroll view restored without duplicate game object");
var previousScrollGo = playerVisual.transform.parent!.parent!.gameObject;
Hooks.ItemsContainerInitPostfix(playerVisual);
Check(ReferenceEquals(previousScrollGo, playerVisual.transform.parent!.parent!.gameObject),
    "scroll init is idempotent");
var vanillaVisual = new uGUI_ItemsContainer();
vanillaVisual.inventory = tab.inventory;
vanillaVisual.Init(new ItemsContainer());
Hooks.ItemsContainerInitPostfix(vanillaVisual);
Check(vanillaVisual.transform.parent?.name != "CarryStoreMask",
    "unrelated vanilla grids left untouched");

// The universal 1x1 patch changes only the grid footprint, never TechType/item identity.
var dimensions = new Vector2int(4, 2);
Check(!Hooks.ItemSizePrefix(ref dimensions), "vanilla TechData.GetItemSize skipped");
Check(dimensions.x == 1 && dimensions.y == 1, "any item footprint 1x1");

// Both built locker sizes must expand, not arbitrary container types.
foreach (var techType in new[] { TechType.Locker, TechType.SmallLocker })
{
    var locker = new StorageContainer { constructable = new Constructable { techType = techType } };
    Hooks.LockerLifecyclePostfix(locker);
    Check(locker.container.sizeX == 8 && locker.container.sizeY == 10, techType + " grid 8x10");
    Check(locker.width == 8 && locker.height == 10, techType + " serialized game container dimensions");
    var width = 5; var height = 6;
    Hooks.LockerResizePrefix(locker, ref width, ref height);
    Check(width == 8 && height == 10, techType + " resize guarded");
}
var vehicle = new StorageContainer { constructable = new Constructable { techType = TechType.VehicleStorageModule } };
Hooks.LockerLifecyclePostfix(vehicle);
Check(vehicle.container.sizeX == 5 && vehicle.container.sizeY == 6, "unrelated storage unchanged");
var vWidth = 5; var vHeight = 6;
Hooks.LockerResizePrefix(vehicle, ref vWidth, ref vHeight);
Check(vWidth == 5 && vHeight == 6, "unrelated resize unchanged");

// Scanner UI 3x3: local grid, existing contents kept.
var scanner = new MapRoomFunctionality();
scanner.storageContainer.container.recorded.Add("scanner-module");
Hooks.ScannerLifecyclePostfix(scanner);
Check(scanner.storageContainer.container.sizeX == 3 && scanner.storageContainer.container.sizeY == 3, "client scanner 3x3");
Check(scanner.storageContainer.container.recorded.Count == 1, "scanner existing items not removed");
Hooks.ScannerCanAddPrefix(scanner);
Check(scanner.storageContainer.container.resizeCalls == 1, "scanner resize idempotent");

// Network metadata: 8x10 / 3x3 and each stored footprint 1x1.
var owner = new MockProfile { InventoryItems = Model.StorageContainer.Create(6, 8) };
var old = new Model.StorageItem { ItemId = "old-item", TechType = "Lithium", Size = 22 };
owner.InventoryItems.Items.Add(old);
Hooks.ServerPlayerInitializePostfix(owner);
Check(owner.InventoryItems.GetSizeX() == 8 && owner.InventoryItems.GetSizeY() == 10, "host player inventory 8x10");
Check(old.Size == 11, "host loaded item footprint migrated");
Check(owner.InventoryItems.Items.Count == 1 && old.ItemId == "old-item", "migration preserves identity");
var lockerModel = Model.StorageContainer.Create(5, 6);
Hooks.ServerLockerPostfix(lockerModel);
Check(lockerModel.Size == 108, "host locker metadata 8x10");

var fresh = new Model.StorageItem { ItemId = "new-item", Size = 42 };
Hooks.ServerItemCreatedPostfix(fresh);
Check(fresh.Size == 11 && fresh.ItemId == "new-item", "new storage item footprint 1x1");
byte result = 4;
Check(!Hooks.ServerItemSizePrefix(ref result) && result == 1, "server-side item dimensions 1x1");
var incoming = new Model.StorageItem { ItemId = "incoming", Size = 33 };
Hooks.ServerHasRoomPrefix(owner.InventoryItems, new object[] { incoming });
Check(incoming.Size == 11, "incoming item size normalized before HasRoomFor");

// MapRoom processor must enlarge BEFORE vanilla 2x2 capacity check.
var update = new MockPacket { SecretTechType = "None", Component = new MockComponent { Value = new Model.BaseMapRoom() } };
var construction = new MockConstruction();
Hooks.ServerScannerPrefix(new object[] { new object(), update, construction });
Check(construction.Room != null, "host creates MapRoom component");
Check(construction.Room!.StorageContainer!.GetSizeX() == 3 && construction.Room.StorageContainer.GetSizeY() == 3,
    "host scanner capacity 3x3 before vanilla validation");
construction.Room.StorageContainer.Items.Add(new Model.StorageItem { ItemId = "saved-upgrade", Size = 22 });
Hooks.ServerScannerPrefix(new object[] { new object(), update, construction });
Check(construction.Room.StorageContainer.Items.Count == 1 &&
      construction.Room.StorageContainer.Items[0].Size == 11, "scanner upgrade migrated without loss");
var noRoom = new MockConstruction();
Hooks.ServerScannerPrefix(new object[] { new object(), new MockPacket {
    SecretTechType = "Fabricator", Component = update.Component }, noRoom });
Check(noRoom.Room == null, "fabricator request untouched");

// The actual Harmony 2.3.3 engine must accept every patch signature as-is.
var native = typeof(MainPlugin).Assembly.GetType("Sirro.BZ.MP.CarryStore.RuntimeHooks", true)!;
var install = native.GetMethod("Install", BindingFlags.NonPublic | BindingFlags.Static)!;
var uninstall = native.GetMethod("Uninstall", BindingFlags.NonPublic | BindingFlags.Static)!;
var externalHarmony = new Harmony("test.external.original-patches");
externalHarmony.Patch(typeof(Inventory).GetMethod(nameof(Inventory.Awake))!,
    postfix: new HarmonyMethod(typeof(ForeignEvent).GetMethod(nameof(ForeignEvent.Postfix))!));
try
{
    install.Invoke(null, null);
    var info = Harmony.GetPatchInfo(typeof(TechData).GetMethod(nameof(TechData.GetItemSize))!);
    Check(info != null && info.Prefixes.Count > 0, "Harmony registered item-size prefix");
    var tech = TechData.GetItemSize(TechType.VehicleStorageModule);
    var reflectedTech = (Vector2int)typeof(TechData).GetMethod(nameof(TechData.GetItemSize))!.Invoke(null,
        new object[] { TechType.VehicleStorageModule })!;
    Check(reflectedTech.x == 1 && reflectedTech.y == 1, "reflection call invokes native Harmony patch");
    Check(tech.x == 1 && tech.y == 1, "Harmony overrides GetItemSize (value-type ref result)");
    var hInventory = new Inventory();
    hInventory.Awake();
    Check(hInventory.container.sizeX == 8 && hInventory.container.sizeY == 10,
        "Harmony patches Inventory.Awake");
    Check(ForeignEvent.Calls == 1, "external Harmony postfix preserved during installation");
    var hLocker = new StorageContainer { constructable = new Constructable { techType = TechType.Locker } };
    hLocker.Awake();
    Check(hLocker.container.sizeX == 8 && hLocker.container.sizeY == 10,
        "Harmony patches StorageContainer.Awake");
    hLocker.Resize(6, 8);
    Check(hLocker.width == 8 && hLocker.height == 10,
        "Harmony replaces actual Resize arguments");
    var hScanner = new MapRoomFunctionality();
    hScanner.Start();
    Check(hScanner.storageContainer.container.sizeX == 3 &&
        hScanner.storageContainer.container.sizeY == 3, "Harmony patches scanner Start");

    var profile = new Subnautica.Server.Core.AuthorizationProfile();
    profile.Initialize("pilot", "identity");
    Check(profile.InventoryItems.Size == 108, "Harmony patches server Initialize return");

    var lockerProcessor = new Subnautica.Server.Processors.Metadata.StorageProcessor();
    var defaultLocker = Model.StorageContainer.Create(5, 6);
    lockerProcessor.Read(defaultLocker);
    Check(defaultLocker.Size == 108, "Harmony patches private GetStorageContainer");
    var scannerProcessor = new Subnautica.Server.Processors.Metadata.BaseMapRoomProcessor();
    var scannerConstruct = new MockConstruction();
    scannerProcessor.OnDataReceived(profile, update, scannerConstruct);
    Check(scannerProcessor.SizeBeforeVanillaValidation == 33,
        "Harmony scanner hook executes BEFORE original server validator");
    var added = Model.StorageItem.Create("generated", "Gold");
    Check(added.Size == 11, "Harmony patches server-generated StorageItem.Create");

    var oldMeta = new Model.StorageItem { ItemId = "old-meta", Size = 42 };
    var width1 = oldMeta.GetSizeX();
    Check(width1 == 1, "Harmony prefixes GetSizeX");
    var grid = Model.StorageContainer.Create(2, 2);
    grid.Items.Add(oldMeta);
    var newMeta = new Model.StorageItem { ItemId = "incoming", Size = 44 };
    grid.HasRoomFor(newMeta);
    Check(newMeta.Size == 11 && oldMeta.Size == 11,
        "Harmony normalizes existing and incoming sizes before HasRoomFor");
}
finally
{
    uninstall.Invoke(null, null);
}
var after = TechData.GetItemSize(TechType.Locker);
Check(after.x == 2 && after.y == 2, "our Harmony patch removed on disable");
new Inventory().Awake();
Check(ForeignEvent.Calls == 2, "external Harmony patch survives plugin unload");
externalHarmony.UnpatchAll("test.external.original-patches");
Console.WriteLine($"PASS {checkCount} fake + real Harmony 2.3.3 assertions (NOT a running game)");

public enum TechType { Locker, SmallLocker, VehicleStorageModule }
public class ItemsContainer
{
    public int sizeX { get; private set; } = 5;
    public int sizeY { get; private set; } = 6;
    public int resizeCalls;
    public List<string> recorded = new();
    public void Resize(int width, int height)
    {
        sizeX = width;
        sizeY = height;
        resizeCalls++;
    }
}
public class Inventory
{
    public static Inventory? main;
    public ItemsContainer container = new();
    public Inventory() => main = this;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Awake() { }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void ResetInventory() { }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void UpdateContainers() { }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void OnProtoDeserialize(object serializer) { }
}
public class Constructable { public TechType techType; }
public class StorageContainer
{
    public int width = 5, height = 6;
    public ItemsContainer container = new();
    public Constructable? constructable;
    public object? GetComponentInParent(Type type) => type == typeof(Constructable) ? constructable : null;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Awake() { }
    public void ResetContainer() { }
    public void OnOpen() { }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Resize(int width, int height)
    {
        this.width = width;
        this.height = height;
        container.Resize(width, height);
    }
}
public class MapRoomFunctionality
{
    public StorageContainer storageContainer = new();
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Start() { }
    public void OnPostRebuildGeometry(object data) { }
    public bool IsAllowedToAdd(object item, bool verbose) => true;
    public void AddItem(object item) { }
}
public static class TechData
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Vector2int GetItemSize(TechType type) => new Vector2int(2, 2);
}
public class uGUI_InventoryTab
{
    public uGUI_Inventory inventory = new();
    public void Open() { }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void OnOpenPDA() { }
}
public class uGUI_Inventory
{
    public uGUI_ItemsContainer inventory = new();
    public uGUI_ItemsContainer storage = new();
    public uGUI_Inventory()
    {
        this.inventory.inventory = this;
        this.storage.inventory = this;
    }
}
public class uGUI_ItemsContainer
{
    public uGUI_Inventory inventory = null!;
    public ItemsContainer? container;
    public UnityEngine.RectTransform rectTransform;
    public UnityEngine.Transform transform => rectTransform;
    public uGUI_ItemsContainer()
    {
        var holder = new UnityEngine.GameObject("VanillaPDAParent");
        var grid = new UnityEngine.GameObject("VanillaGrid");
        rectTransform = (UnityEngine.RectTransform)grid.AddComponent(typeof(UnityEngine.RectTransform));
        rectTransform.SetParent(holder.transform, false);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Init(ItemsContainer items) => container = items;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Uninit() => container = null;
}
public static class ForeignEvent
{
    public static int Calls;
    public static void Postfix() => Calls++;
}
public class MockProfile { public Model.StorageContainer InventoryItems { get; set; } = null!; }
public class MockPacket
{
    public string SecretTechType { get; set; } = "None";
    public MockComponent Component { get; set; } = new();
}
public class MockComponent
{
    public object? Value { get; set; }
    public T? GetComponent<T>() where T : class => Value as T;
}
public class MockConstruction
{
    public Model.BaseMapRoom? Room;
    public T EnsureComponent<T>() where T : class, new()
    {
        Room ??= new Model.BaseMapRoom();
        return (T)(object)Room;
    }
}
namespace Subnautica.Network.Models.Metadata
{
    public class StorageItem
    {
        public byte Size { get; set; }
        public string ItemId { get; set; } = "";
        public string TechType { get; set; } = "";
        [MethodImpl(MethodImplOptions.NoInlining)]
        public byte GetSizeX() => (byte)(Size % 10);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public byte GetSizeY() => (byte)(Size / 10);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static StorageItem Create(string itemId, string techType) =>
            new StorageItem { ItemId = itemId, TechType = techType, Size = 22 };
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static StorageItem Create(string techType) => Create("generated", techType);
    }
    public class StorageContainer
    {
        public byte Size { get; set; }
        public List<StorageItem> Items { get; set; } = new();
        public static StorageContainer Create(byte x, byte y)
        {
            var c = new StorageContainer();
            c.Resize(x, y);
            return c;
        }
        public void Resize(byte x, byte y) => Size = checked((byte)(x + 10*y));
        [MethodImpl(MethodImplOptions.NoInlining)]
        public byte GetSizeX() => (byte)(Size % 10);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public byte GetSizeY() => (byte)(Size / 10);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool HasRoomFor(StorageItem item) => Items.Count < GetSizeX()*GetSizeY();
    }
    public class BaseMapRoom
    {
        public StorageContainer? StorageContainer { get; set; }
    }
}

namespace Subnautica.Server.Core
{
    public class AuthorizationProfile
    {
        public Subnautica.Network.Models.Metadata.StorageContainer InventoryItems { get; set; } =
            Subnautica.Network.Models.Metadata.StorageContainer.Create(6, 8);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public AuthorizationProfile Initialize(string playerName, string id) => this;
    }
}
namespace Subnautica.Server.Processors.Metadata
{
    public class StorageProcessor
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        private Subnautica.Network.Models.Metadata.StorageContainer GetStorageContainer(object construction) =>
            (Subnautica.Network.Models.Metadata.StorageContainer)construction;
        public void Read(Subnautica.Network.Models.Metadata.StorageContainer construction) =>
            GetStorageContainer(construction);
    }
    public class BaseMapRoomProcessor
    {
        public byte SizeBeforeVanillaValidation;
        public bool OnDataReceived(Subnautica.Server.Core.AuthorizationProfile profile,
            MockPacket packet, MockConstruction construction)
        {
            SizeBeforeVanillaValidation = construction.Room?.StorageContainer?.Size ?? 0;
            return SizeBeforeVanillaValidation == 33;
        }
    }
}

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x=x;this.y=y; }
    }
    public class Transform
    {
        public GameObject gameObject { get; set; } = null!;
        public Transform? parent { get; private set; }
        public string name => gameObject.name;
        public void SetParent(Transform p, bool worldPositionStays) => parent=p;
    }
    public class RectTransform : Transform
    {
        public Vector2 sizeDelta { get; set; }
        public Vector2 anchoredPosition { get; set; }
        public Vector2 anchorMin { get; set; }
        public Vector2 anchorMax { get; set; }
    }
    public sealed class GameObject
    {
        private readonly Dictionary<Type, object> comps = new();
        public string name { get; set; }
        public Transform transform { get; private set; }
        public bool activeSelf { get; private set; }=true;
        public GameObject(string name)
        {
            this.name=name;
            transform=new Transform { gameObject=this };
        }
        public object AddComponent(Type type)
        {
            var c=Activator.CreateInstance(type)!;
            if(c is Transform t)
            {
                t.gameObject=this;
                var existingParent=transform.parent;
                transform=t;
                if(existingParent!=null)t.SetParent(existingParent,false);
            }
            comps[type]=c;
            return c;
        }
        public object? GetComponent(Type type) => comps.TryGetValue(type,out var c)?c:null;
        public void SetActive(bool enable) => activeSelf=enable;
    }
}
namespace UnityEngine.UI
{
    public class RectMask2D { }
    public class ScrollRect
    {
        public enum MovementType { Unrestricted, Elastic, Clamped }
        public UnityEngine.RectTransform? viewport {get;set;}
        public UnityEngine.RectTransform? content {get;set;}
        public bool horizontal {get;set;}
        public bool vertical {get;set;}
        public MovementType movementType {get;set;}
        public float scrollSensitivity {get;set;}
        public float horizontalNormalizedPosition {get;set;}
        public float verticalNormalizedPosition {get;set;}
    }
}
