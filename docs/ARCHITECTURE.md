# Carry & Store — multiplayer-first architecture (2026-11-15)

## Evidence and important limits

Target: Subnautica Below Zero with BOT Benson Multiplayer 3.0.1. Construtor Zero shows a native `SubnauticaPlugin` client DLL loaded from `Game/Plugins` without BepInEx, Nautilus or SML; its compilation-only Subnautica.API contract is NOT for distribution. Its latest audited candidate on branch `test/construtor-zero-cfc-301` passed simulated tests, not an actual two-client session.

The public upstream multiplayer source warns that it may not match the shipped version. The following are confirmed in the public source, **not independently confirmed against the released 3.0.1 binaries**:

1. `Server/Core/AuthorizationProfile.cs` initializes player `InventoryItems` as `StorageContainer.Create(6,8)`.
2. `Server/Processors/Metadata/StorageProcessor.cs` initializes `SmallLocker` as 5x6 and `Locker` as 6x8.
3. `Server/Processors/Metadata/BaseMapRoomProcessor.cs` initializes the scanner room upgrades as 2x2.
4. `Server/Logic/Storage.cs` calls `targetContainer.HasRoomFor(pickupItem.Item)` before accepting moves.
5. `Network/Models/Metadata/StorageContainer.cs` serializes dimensions as `byte Size = width + height*10`; the 8x10, 3x3 and 1x1 targets fit this representation.
6. `Network/Models/Metadata/StorageItem.cs` persists **item footprint** in `byte Size`, derived from `TechData.GetItemSize(TechType)`, including items created on the server.
7. `Events/Patches/Events/Storage/ItemToggle.cs` patches `Inventory.AddOrSwap` and intercepts storage transfers. `Client/Synchronizations/Processors/Metadata/StorageProcessor.cs` sends requests and applies server replies. Never bypass either to force a local move.
8. `Server/Logic/Furnitures/BaseMapRoom.cs`: extra speed/range upgrades affect `GetScanInterval` and `GetMaxDistance`; 9-slot scanner changes have both gameplay and performance impact.

Source: https://github.com/ismail0234/Subnautica-Below-Zero-Multiplayer
Related project: https://github.com/SirRosa/Subnautica-BZ-MP-BeplnEx-Fix/tree/test/construtor-zero-cfc-301

## Proposed runtime layers (NOT implemented)

- Client: Harmony hook at actual `TechData.GetItemSize` signature to return 1x1 **only for items in inventory grids**, leaving equipment slot rules intact. Inventory 8x10, both locker types 8x10, scanner module container 3x3, layout/scrolling of relevant uGUI, and resizing after world/late entity initialization.
- Authoritative host/server: inspect actual 3.0.1 loader and support for `Server`/`ServerClient` plugin types; ensure server-side player storage and locker/maproom metadata are resized BEFORE `HasRoomFor` and serializing/storage initialization. Ensure `StorageItem` is represented as 1x1 for server-generated and client-generated items; do not trust client-side dimensions unchecked.
- Handshake: activate expanded storage only when both players and the host agree on feature version, dimension config and patch coverage. If no supported handshake exists, do not silently activate on only one side.
- Persistence: preserve all `ItemId`, `TechType`, serialized item data, original dimensions for safe revert, and avoid reinitializing maps in ways that drop items. Load/rejoin/restart/late-join require reconciliation.
- Disable/uninstall: fail closed if items no longer fit vanilla grids. Never silently truncate, delete or scatter overflow.
- Scanner room: ensure nine upgrades are consumed in both gameplay calculation and metadata sync, evaluate cap/performance of range and speed.
- Compatibility: isolate Harmony owner IDs and patch target checks. Do not ship any of the proprietary game or multiplayer DLLs. Do not modify Construtor Zero.

## Non-negotiable gate before releasing DLL

The 3.0.1 released `Assembly-CSharp.dll`, `Subnautica.API.dll`, `Subnautica.Client.dll`, `Subnautica.Server.dll`, `Subnautica.Network.dll` and `Subnautica.Events.dll` must be independently inspected for exact patch signatures and server plugin activation. Existing Construtor Zero ABI notes do NOT cover all inventory/UI signatures. Static protocol assertions are **not** an integration test.
