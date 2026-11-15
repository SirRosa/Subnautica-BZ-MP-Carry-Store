# Carry & Store — Subnautica Below Zero / BOT Benson Multiplayer 3.0.1

**Status: 0.1.4-dev native ServerClient plugin built, NOT approved for normal gameplay.** The plugin is inactive by default. This repository is separate from Construtor Zero.

## Features implemented in the experimental hooks

| Area | Requested | Implemented in prototype |
|---|---|---|
| Player backpack | 7×10 = 70 cells | Client Inventory lifecycle + server AuthorizationProfile.Initialize |
| All item footprints | 1×1 | Game TechData.GetItemSize plus network StorageItem.Create/GetSizeX/GetSizeY/HasRoomFor |
| Bulk storage | 7×15 = 105 cells | Client StorageContainer lifecycle/resize + explicit storage-only network metadata accessors |
| Scanner Room upgrades | 3×3 = 9 cells | Game MapRoomFunctionality plus server BaseMapRoomProcessor.OnDataReceived |

Server-side metadata changes are made before the original multiplayer capacity validation. The mod does not cancel or bypass server-authorized item transfers. Existing item identifiers are preserved when changing metadata footprint.

**Important limitations:** the actual 3.0.1 plugin activation/peer negotiation has not been verified in a running multiplayer session; expanded grids can be clipped by the UI, nine scanner upgrades may increase server scan load, and disabling/uninstalling after storing overflow items may make them inaccessible. There is no proven rollback or compatibility handshake yet. Do not enable on a production/shared save.

## Build and evidence

- Branch: `test/carry-store-301` (do not merge to `main` before multiplayer verification).
- CI compiles a **single native** `Sirro.BZ.MP.CarryStore.dll` for `net472` and tests the protocol (47 cases), simulated client/server state and real Harmony 2.3.3 patch injection.
- `src/Api.Contract` and `src/Game.Contract` are **compile-time stubs** only. Do not install or distribute their DLLs.
- Exact released 3.0.1 assemblies have been inspected for method signatures; see [ABI-301.md](docs/ABI-301.md). They are not committed.
- The CI `Carry-Store-experimental-not-playtested` artifact is DEVELOPMENT ONLY.

## Experimental toggle

When started in the multiplayer launcher, a config file is created at:

`%APPDATA%\.botbenson\Subnautica Below Zero\Game\Config\CarryStore.ini`

The only toggle is `enable_experimental=false` by default. **Do not change it to true except on a disposable two-client test save**, and only with the same plugin on host and guest. A missing Harmony target causes complete hook installation to fail rather than silently applying partial changes.

Project audit: [architecture](docs/ARCHITECTURE.md) | [test plan](docs/TEST-PLAN.md)

Related project: [Construtor Zero](https://github.com/SirRosa/Subnautica-BZ-MP-BeplnEx-Fix/tree/test/construtor-zero-cfc-301)

## Additional safety in the latest development candidate

On explicit opt-in, the plugin now checks SHA256 of all six actual 3.0.1 assemblies.
A missing, modified or updated assembly causes activation to fail with no Harmony patches installed.
A fatal runtime exception in any hook triggers a session-wide `CRITICAL_RUNTIME_FAULT` log entry;
no further CarryStore mutations are attempted. **This does not roll back prior changes or verify
that the other client has the plugin installed.** Gameplay certification remains blocked.

## Activation diagnostics (0.1.1-dev)

**The plugin being counted by the launcher and creating `CarryStore.ini` does not mean its Harmony hooks were enabled.** The default is `enable_experimental=false`. The latest build writes its exact state to `%APPDATA%\\.botbenson\\Subnautica Below Zero\\Game\\Config\\CarryStore-status.log`:

- `STATUS=CONFIG_DISABLED`: change `enable_experimental=true` in `CarryStore.ini` and restart both game clients **only on disposable test saves**.
- `STATUS=DLL_MATCH`: exact released libraries match the local 3.0.1 installation.
- `STATUS=PATCHES_INSTALLED`: Harmony owner checks passed; does **not** certify a real multiplayer session.
- `STATUS=PATCH_FAILED`: exception / unsupported binary or API prevented installation; send only this status log for a precise fix.

A plugin can appear in the launcher and create a config file while doing **nothing**, by design. This status file is created on each OnEnabled, even if the normal MP logging system is inaccessible.

## Fix for in-memory launcher assemblies (v0.1.2-dev)

In BOT Benson 3.0.1, the launcher may load Subnautica.API and other assemblies directly from bytes,
leaving `Assembly.Location` empty. Previously that caused `STATUS=PATCH_FAILED: Cannot hash required assembly Subnautica.API`
even on the correct installation.

Now the compatibility gate checks SHA256 **when the loaded assembly has a readable disk file**.
For an assembly loaded exclusively into memory, it checks its **exact ModuleVersionId (MVID)**
against the same six original 3.0.1 binaries used for ABI inspection. A DLL with a
readable, mismatching SHA256 is rejected even if its MVID matches.

The log now emits `VERIFIED_SHA256` or `VERIFIED_MVID` per assembly, then
`STATUS=PATCHES_INSTALLED` only when all remaining hooks install successfully.
This patch fixes the demonstrated activation blocker; it does not yet prove two-player gameplay.

## Activation blocker fixed in v0.1.3-dev

On BOT Benson 3.0.1 the **real** `uGUI_InventoryTab.OnOpenPDA` has **two arguments**.
The previous hook targeted `/0`, preventing the entire 26-hook transaction from installing
with `STATUS=PATCH_FAILED: System.MissingMethodException: uGUI_InventoryTab.OnOpenPDA/0`.
The hook arity and the Harmony regression harness have been corrected. All other
23 named target arities and three network factory overloads were re-audited against the
original 3.0.1 DLL metadata. See `docs/ABI-301.md` for the audit.

## v0.1.4 experimental storage policy

Player backpack: **7x10**; ordinary storage containers, lockers, fridge, drop pod, Seatruck storage,
Prawn storage, waterproof/quantum storage: **7x15**; scanner upgrades: **3x3**.
Garden/planter, reactor, aquarium and equipment/vehicle-upgrade slot semantics remain untouched;
the game equipment-slot list is not a freely resizable ItemsContainer. Adding nine
working vehicle module IDs requires a separate multiplayer protocol change.
The shared PDA storage view now detaches stale scroll masks when opened for an
unmodified container. It restores the original grid parent before deactivating
the old scroll GameObject: no more invisible storage slots when switching containers.
**Back up the world and move excess items before changing backpack from 80 to 70 cells.**
