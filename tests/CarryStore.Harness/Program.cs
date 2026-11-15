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

// The native loader invoking OnEnabled does not imply that runtime patches are enabled.
// For a first install the config is deliberately OFF; show a reliable diagnostic file instead.
var plugin = new MainPlugin();
plugin.OnEnabled();
var pluginAssembly = typeof(MainPlugin).Assembly;
var configType = pluginAssembly.GetType("Sirro.BZ.MP.CarryStore.Configuration", true)!;
var parse = configType.GetMethod("ParseExplicitOptIn", BindingFlags.Static | BindingFlags.NonPublic)!;
bool Parse(string config) => (bool)parse.Invoke(null, new object[] { config })!;
Check(!Parse("enable_experimental=false"), "new installation keeps experimental patches disabled");
Check(Parse("enable_experimental=true"), "explicit opt-in accepted");
Check(Parse(" enable_experimental = TRUE  "), "whitespace and case accepted");
Check(!Parse("enable_experimental=true\\nenable_experimental=false"), "duplicate/ambiguous flags refused");
Check(!Parse("#enable_experimental=true\\nenable_experimental=false"), "commented opt-in ignored");
var statusType = pluginAssembly.GetType("Sirro.BZ.MP.CarryStore.Diagnostic", true)!;
string statusPath = (string)statusType.GetProperty("StatusPath", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
Check(System.IO.File.Exists(statusPath), "native startup writes independent status log");
var contents = System.IO.File.ReadAllText(statusPath);
Check(contents.Contains("STATUS=CONFIG_"), "status log explicitly distinguishes loaded vs enabled");

// The launcher may load its DLLs with Assembly.Load(byte[]) and leave Assembly.Location blank.
// Assert in-memory MVID fallback using the actual supplied 3.0.1 assemblies' module identities.
var compatibility = pluginAssembly.GetType("Sirro.BZ.MP.CarryStore.BinaryCompatibility", true)!;
var fingerprintMethod = compatibility.GetMethod("VerifyBuildFingerprint", BindingFlags.Static | BindingFlags.NonPublic)!;
string CheckFingerprint(string name, Guid mvid, string? sha) =>
    (string)fingerprintMethod.Invoke(null, new object?[] { name, mvid, sha })!;

var trusted = new (string Name, string Mvid, string Sha)[]
{
    ("Assembly-CSharp", "dd06c891-a952-4a5c-8663-e89fb2868b5c", "f5a9630cfd3f50c3a67e00170d96f2b48e05019e789443f61826ef4cf85c5b99"),
    ("Subnautica.API", "ec5ae426-d8d9-4444-a236-ac84d3e2f426", "386b9e69df15cd1982dd35faba89f7ddb99f40b5f56f10b02c56d6fc7cec3908"),
    ("Subnautica.Client", "b677faee-7813-48a0-87ed-429c9a71522d", "62b8f14287062e63b76e3e0b147dd9d1842231e5bc08728d6bd3c868238271db"),
    ("Subnautica.Server", "7bca8db2-1290-42e6-a690-928186c7b065", "ebfb909990b2950e30bd14e646768959051c7b3e787676059c35dac5b86eff75"),
    ("Subnautica.Network", "e6fd323d-1a4a-4e79-b050-463870c0c57c", "e1da83bca889b77363d8b46c362fd1f2390016a76342a62f773cc792294b8f66"),
    ("Subnautica.Events", "0bd4f7b5-43b7-44b4-bc8a-ea4daf3f0588", "b0a3abddcf9e4d97db66ca81c5032cc5fdd6737aff3ee0769bf72303b10f9a12"),
};
foreach (var item in trusted)
{
    Check(CheckFingerprint(item.Name, Guid.Parse(item.Mvid), null) == "MVID",
        "in-memory 3.0.1 " + item.Name + " recognized by exact MVID");
    Check(CheckFingerprint(item.Name, Guid.Empty, item.Sha) == "SHA256",
        "on-disk 3.0.1 " + item.Name + " recognized by SHA256");
    try
    {
        CheckFingerprint(item.Name, Guid.Empty, null);
        Check(false, "untrusted memory module must fail");
    }
    catch (TargetInvocationException e)
    {
        Check(e.InnerException is InvalidOperationException, "untrusted in-memory " + item.Name + " rejected");
    }
    try
    {
        // A mismatching on-disk hash must NOT be overruled by a valid MVID.
        CheckFingerprint(item.Name, Guid.Parse(item.Mvid), new string('0', 64));
        Check(false, "incorrect SHA256 must fail even with matching MVID");
    }
    catch (TargetInvocationException e)
    {
        Check(e.InnerException is InvalidOperationException, "wrong SHA256 for " + item.Name + " rejected");
    }
}

// Player inventory: alter the backing ItemsContainer rather than changing only the UI.
var inv = new Inventory();
Hooks.PlayerInventoryPostfix(inv);
Check(inv.container.sizeX == 7 && inv.container.sizeY == 10, "client inventory = 7x10");
Hooks.PlayerInventoryPostfix(inv);
Check(inv.container.resizeCalls == 1, "resize idempotent");
Hooks.InventoryUiPostfix();
Check(inv.container.resizeCalls == 1, "UI open does not repeat resize");

// BZ-specific native scrolling (no BepInEx/AdvancedInventory).
var tab = new uGUI_InventoryTab();
var playerVisual = tab.inventory.inventory;
var playerGrid = new ItemsContainer();
playerGrid.Resize(7, 10);
playerVisual.Init(playerGrid);
Hooks.ItemsContainerInitPostfix(playerVisual);
Check(playerVisual.transform.parent?.name == "CarryStoreMask", "inventory viewport created");
var playerScrollGo = playerVisual.transform.parent!.parent!.gameObject;
var playerScroll = (UnityEngine.UI.ScrollRect)playerScrollGo.GetComponent(typeof(UnityEngine.UI.ScrollRect))!;
Check(playerScroll.viewport != null && playerScroll.content == playerVisual.rectTransform,
    "scrollbar bound to inventory content");
Check(playerScroll.viewport!.sizeDelta.x == 517f && playerScroll.viewport.sizeDelta.y == 588f,
    "7x8 visible viewport with full 8x10 grid");
Check(playerVisual.rectTransform.sizeDelta.x == 497f && playerVisual.rectTransform.sizeDelta.y == 710f,
    "full 7x10 player content dimensions");
var storageVisual = tab.inventory.storage;
var storageGrid = new ItemsContainer();
storageGrid.Resize(7, 15);
storageVisual.Init(storageGrid);
Hooks.ItemsContainerInitPostfix(storageVisual);
Check(storageVisual.transform.parent?.name == "CarryStoreMask", "storage viewport created");
Check(storageVisual.rectTransform.sizeDelta.x == 497f && storageVisual.rectTransform.sizeDelta.y == 1065f,
    "full 7x15 storage content dimensions");
var storageScrollGo = storageVisual.transform.parent!.parent!.gameObject;
var originalStorageParent = storageVisual.transform.parent!.parent!.parent!;
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

// Regress the real reported bug: same PDA storage UI changes from expanded locker to
// ordinary unexpanded 3x3/5x6 grid, after its prior scroll view was disabled by Uninit.
Hooks.ItemsContainerUninitPostfix(storageVisual);
Check(!storageScrollGo.activeSelf, "storage scrolling hidden on Uninit");
var upgrades = new ItemsContainer();
upgrades.Resize(3, 3);
upgrades.recorded.Add("saved-module");
storageVisual.Init(upgrades);
Hooks.ItemsContainerInitPostfix(storageVisual);
Check(ReferenceEquals(originalStorageParent, storageVisual.transform.parent),
    "unmodified upgrade panel reparented out of invisible mask");
Check(upgrades.recorded.Count == 1, "module remains in original storage grid");
Check(storageVisual.rectTransform.sizeDelta.x == 213f && storageVisual.rectTransform.sizeDelta.y == 213f,
    "reparented vanilla 3x3 module panel recomputes dimensions");
var regular = new ItemsContainer();
regular.recorded.Add("kept-item");
storageVisual.Init(regular);
Hooks.ItemsContainerInitPostfix(storageVisual);
Check(regular.recorded.Count == 1 && ReferenceEquals(originalStorageParent, storageVisual.transform.parent),
    "ordinary unmodified storage remains visible with its items");
storageVisual.Init(storageGrid);
Hooks.ItemsContainerInitPostfix(storageVisual);
Check(storageVisual.transform.parent?.name == "CarryStoreMask",
    "expanded storage reopens after vanilla panel without hidden grid");
Check(storageVisual.transform.parent!.parent!.gameObject.activeSelf, "new storage scroll view visible");
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

// Ordinary storage components expand; specialized machinery is excluded.
foreach (var techType in new[] { TechType.Locker, TechType.SmallLocker })
{
    var locker = new StorageContainer { constructable = new Constructable { techType = techType } };
    Hooks.LockerLifecyclePostfix(locker);
    Check(locker.container.sizeX == 7 && locker.container.sizeY == 15, techType + " grid 7x15");
    Check(locker.width == 7 && locker.height == 15, techType + " serialized game container dimensions");
    var width = 5; var height = 6;
    Hooks.LockerResizePrefix(locker, ref width, ref height);
    Check(width == 7 && height == 15, techType + " resize guarded");
}
foreach (var techType in new[] { TechType.VehicleStorageModule, TechType.Fridge, TechType.QuantumLocker })
{
    var vehicle = new StorageContainer { constructable = new Constructable { techType = techType } };
    Hooks.LockerLifecyclePostfix(vehicle);
    Check(vehicle.container.sizeX == 7 && vehicle.container.sizeY == 15, techType + " bulk storage 7x15");
    var vWidth = 5; var vHeight = 6;
    Hooks.LockerResizePrefix(vehicle, ref vWidth, ref vHeight);
    Check(vWidth == 7 && vHeight == 15, techType + " storage Resize guard");
}
foreach (var techType in new[] { TechType.BaseBioReactor, TechType.BaseMapRoom, TechType.Aquarium,
    TechType.BatteryCharger, TechType.PlanterBox })
{
    var special = new StorageContainer { constructable = new Constructable { techType = techType } };
    Hooks.LockerLifecyclePostfix(special);
    Check(special.container.sizeX == 5 && special.container.sizeY == 6,
        techType + " gameplay-specific container not expanded");
    int w = 4, h = 6;
    Hooks.LockerResizePrefix(special, ref w, ref h);
    Check(w == 4 && h == 6, techType + " runtime resize preserved");
}
var scannerStorage = new StorageContainer { mapRoom = new MapRoomFunctionality() };
Hooks.LockerLifecyclePostfix(scannerStorage);
Check(scannerStorage.container.sizeX == 5 && scannerStorage.container.sizeY == 6,
    "scanner inner StorageContainer is not expanded to 7x15");
var garden = new StorageContainer { planter = new Planter() };
Hooks.LockerLifecyclePostfix(garden);
Check(garden.container.sizeX == 5 && garden.container.sizeY == 6,
    "garden container is not expanded to 7x15");

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
Check(owner.InventoryItems.GetSizeX() == 7 && owner.InventoryItems.GetSizeY() == 10, "host player inventory 7x10");
Check(old.Size == 11, "host loaded item footprint migrated");
Check(owner.InventoryItems.Items.Count == 1 && old.ItemId == "old-item", "migration preserves identity");
var lockerModel = Model.StorageContainer.Create(5, 6);
Hooks.ServerLockerPostfix(lockerModel);
Check(lockerModel.Size == 157, "host locker metadata 7x15");

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
    Check(hInventory.container.sizeX == 7 && hInventory.container.sizeY == 10,
        "Harmony patches Inventory.Awake to 7x10");
    Check(ForeignEvent.Calls == 1, "external Harmony postfix preserved during installation");
    var hLocker = new StorageContainer { constructable = new Constructable { techType = TechType.Locker } };
    hLocker.Awake();
    Check(hLocker.container.sizeX == 7 && hLocker.container.sizeY == 15,
        "Harmony patches StorageContainer.Awake to 7x15");
    hLocker.Resize(6, 8);
    Check(hLocker.width == 7 && hLocker.height == 15,
        "Harmony replaces actual Resize arguments");
    // Exercise the real v3.0.1 OnOpenPDA(object, bool) arity through Harmony,
    // rather than only direct fake-hook invocation. Previous stub incorrectly used /0,
    // masking the launcher activation failure.
    var hTab = new uGUI_InventoryTab();
    var hGrid = new ItemsContainer();
    hGrid.Resize(7, 10);
    hTab.inventory.inventory.Init(hGrid);
    Check(hTab.inventory.inventory.transform.parent?.name == "CarryStoreMask",
        "actual Harmony /1 uGUI_ItemsContainer.Init builds 7x10 viewport");
    var hUi = (UnityEngine.UI.ScrollRect)hTab.inventory.inventory.transform.parent!.parent!
        .gameObject.GetComponent(typeof(UnityEngine.UI.ScrollRect))!;
    hUi.horizontalNormalizedPosition = 1f;
    hUi.verticalNormalizedPosition = 0f;
    hTab.OnOpenPDA(new object(), false);
    Check(hUi.horizontalNormalizedPosition == 0f && hUi.verticalNormalizedPosition == 1f,
        "real Harmony /2 OnOpenPDA reopens native scrolling at top-left");

    var hScanner = new MapRoomFunctionality();
    hScanner.Start();
    Check(hScanner.storageContainer.container.sizeX == 3 &&
        hScanner.storageContainer.container.sizeY == 3, "Harmony patches scanner Start");

    var profile = new Subnautica.Server.Core.AuthorizationProfile();
    profile.Initialize("pilot", "identity");
    Check(profile.InventoryItems.Size == 107, "Harmony patches server Initialize to 7x10");

    var lockerProcessor = new Subnautica.Server.Processors.Metadata.StorageProcessor();
    var defaultLocker = Model.StorageContainer.Create(5, 6);
    lockerProcessor.Read(defaultLocker);
    Check(defaultLocker.Size == 157, "Harmony patches private GetStorageContainer to 7x15");
    // Test EVERY newly-registered authoritative storage accessor through the actual Harmony runtime.
    var fridgeModel = new Model.Fridge
    {
        StorageContainer = Model.StorageContainer.Create(5, 7)
    };
    Check(fridgeModel.StorageContainer!.Size == 157, "network fridge metadata getter expands to 7x15");
    var fridgeProcessor = new Subnautica.Server.Processors.Metadata.FridgeProcessor();
    var readerContainer = Model.StorageContainer.Create(5, 7);
    Check(fridgeProcessor.Read(readerContainer).Size == 157, "server FridgeProcessor expands storage to 7x15");
    var pod = new Subnautica.Server.Processors.General.LifepodProcessor();
    Check(pod.Read("starting-pod").Size == 157, "server lifepod storage expands 7x15");
    var seatruckProcessor = new Subnautica.Server.Processors.Vehicle.SeaTruckStorageModuleProcessor();
    Check(seatruckProcessor.Read("truck-locker").Size == 157, "server Seatruck locker expands 7x15");
    var waterproof = new Subnautica.Network.Models.WorldEntity.DynamicEntityComponents.SmallStorage();
    Check(waterproof.StorageContainer!.Size == 157, "network waterproof storage expands 7x15");
    var seaLocker = new Subnautica.Network.Models.WorldEntity.DynamicEntityComponents.Shared.SeaTruckLockerItem();
    Check(seaLocker.StorageContainer!.Size == 157, "network SeaTruckLockerItem expands 7x15");
    var exosuitModel = new Subnautica.Network.Models.WorldEntity.DynamicEntityComponents.Exosuit();
    Check(exosuitModel.StorageContainer!.Size == 157, "network Exosuit getter expands 7x15");
    exosuitModel.ResizeStorageContainer();
    Check(exosuitModel.StorageContainer!.Size == 157, "network Exosuit ResizeStorageContainer preserved 7x15");
    var supplyDrop = new Subnautica.Network.Models.Storage.World.Childrens.SupplyDrop();
    Check(supplyDrop.StorageContainer!.Size == 157, "network lifepod supply metadata expands 7x15");
    var world = new Subnautica.Network.Models.Storage.World.World();
    Check(world.QuantumLocker!.Size == 157, "network quantum locker metadata expands 7x15");
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

public enum TechType { Locker, SmallLocker, VehicleStorageModule, Fridge, QuantumLocker, BaseBioReactor, BaseMapRoom, Aquarium, BatteryCharger, PlanterBox }
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
    public MapRoomFunctionality? mapRoom;
    public Planter? planter;
    public object? GetComponentInParent(Type type) => type == typeof(Constructable) ? constructable :
        type == typeof(MapRoomFunctionality) ? mapRoom :
        type == typeof(Planter) ? planter : null;
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
public class Planter { }
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
    public void OnOpenPDA(object pda, bool instant) { }
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
    public void OnResize(int width, int height)
    {
        rectTransform.sizeDelta = new UnityEngine.Vector2(width * 71f, height * 71f);
    }
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
    public sealed class Fridge
    {
        private StorageContainer? storage;
        public StorageContainer? StorageContainer
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get => storage;
            set => storage = value;
        }
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
    public class FridgeProcessor
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        private Subnautica.Network.Models.Metadata.StorageContainer GetStorageContainer(object fridge) =>
            (Subnautica.Network.Models.Metadata.StorageContainer)fridge;
        public Subnautica.Network.Models.Metadata.StorageContainer Read(
            Subnautica.Network.Models.Metadata.StorageContainer container) => GetStorageContainer(container);
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

// Real 3.0.1 storage processor/model type signatures from Subnautica.Server/Network.dll.
// The game has separate authoritative routes for different inventory-bearing devices.
namespace Subnautica.Server.Processors.General
{
    public class LifepodProcessor
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        private Subnautica.Network.Models.Metadata.StorageContainer GetStorageContainer(string containerId) =>
            Subnautica.Network.Models.Metadata.StorageContainer.Create(6, 8);
        public Subnautica.Network.Models.Metadata.StorageContainer Read(string id) => GetStorageContainer(id);
    }
}
namespace Subnautica.Server.Processors.Vehicle
{
    public class SeaTruckStorageModuleProcessor
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        private Subnautica.Network.Models.Metadata.StorageContainer GetStorageContainer(string lockerId) =>
            Subnautica.Network.Models.Metadata.StorageContainer.Create(5, 6);
        public Subnautica.Network.Models.Metadata.StorageContainer Read(string id) => GetStorageContainer(id);
    }
}
namespace Subnautica.Network.Models.WorldEntity.DynamicEntityComponents
{
    public sealed class SmallStorage
    {
        private Subnautica.Network.Models.Metadata.StorageContainer? storage =
            Subnautica.Network.Models.Metadata.StorageContainer.Create(4, 4);
        public Subnautica.Network.Models.Metadata.StorageContainer? StorageContainer
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get => storage;
            set => storage = value;
        }
    }
    public sealed class Exosuit
    {
        private Subnautica.Network.Models.Metadata.StorageContainer? storage =
            Subnautica.Network.Models.Metadata.StorageContainer.Create(6, 8);
        public Subnautica.Network.Models.Metadata.StorageContainer? StorageContainer
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get => storage;
            set => storage = value;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ResizeStorageContainer()
        {
            storage?.Resize(6, 8);
        }
    }
}
namespace Subnautica.Network.Models.WorldEntity.DynamicEntityComponents.Shared
{
    public sealed class SeaTruckLockerItem
    {
        private Subnautica.Network.Models.Metadata.StorageContainer? storage =
            Subnautica.Network.Models.Metadata.StorageContainer.Create(5, 6);
        public Subnautica.Network.Models.Metadata.StorageContainer? StorageContainer
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get => storage;
            set => storage = value;
        }
    }
}
namespace Subnautica.Network.Models.Storage.World.Childrens
{
    public sealed class SupplyDrop
    {
        private Subnautica.Network.Models.Metadata.StorageContainer? storage =
            Subnautica.Network.Models.Metadata.StorageContainer.Create(6, 8);
        public Subnautica.Network.Models.Metadata.StorageContainer? StorageContainer
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get => storage;
            set => storage = value;
        }
    }
}
namespace Subnautica.Network.Models.Storage.World
{
    public sealed class World
    {
        private Subnautica.Network.Models.Metadata.StorageContainer? quantum =
            Subnautica.Network.Models.Metadata.StorageContainer.Create(4, 4);
        public Subnautica.Network.Models.Metadata.StorageContainer? QuantumLocker
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get => quantum;
            set => quantum = value;
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
