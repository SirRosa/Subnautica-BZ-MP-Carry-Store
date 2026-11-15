# Carry & Store — two-client go/no-go matrix

## CI/protocol tests (implemented)

- 8x10 = 80 slots, on-wire byte 108; 3x3 = 9 slots, byte 33; 1x1 = byte 11.
- Vanilla/default metadata: player 6x8, Locker 6x8, SmallLocker 5x6, MapRoom 2x2 in public MP source.
- Reject unsupported dimension encodings; fail closed unless all client/server/handshake/migration checks pass.
- These tests intentionally do NOT claim successful gameplay, an actual compatible DLL, or server safety.

## ABI validation (method metadata verified; runtime behavior still unverified)

- Exact `TechData.GetItemSize`, `ItemsContainer` resize and inventory initialization signatures.
- Exact scanner `MapRoomFunctionality` storage and upgrades behavior.
- Server `AuthorizationProfile`, `StorageProcessor`, `BaseMapRoomProcessor`, `StorageItem`, metadata and save routes.
- Server versus client plugin loader; same patch/hook coverage on owner and joining player.
- Harmony priority, signature and isolation compared with multiplayer event hooks and Construtor Zero.

## Real gameplay criteria (requires isolated disposable save and two clients)

1. Confirm handshake/version/config on BOTH players and host; mismatch must prohibit enabling.
2. Fresh character 80 slots: move item to every edge, craft/drop/pick up a 2x2 vanilla item now 1x1; log no loss or duplication.
3. Both large and small lockers: fill beyond vanilla bounds to 80 items, move between players simultaneously, reopen, save, reload and late join.
4. Scanner upgrades: install 5–9 valid modules; verify scanner behavior/radius, remove and reinsert, save/restart/rejoin; check CPU/server latency.
5. Craft from inventory and Construtor Zero Craft From Containers enabled and disabled: ensure correct inventory withdrawals, no double-spend, no fake success.
6. Disconnect mid-transfer, host restart, packet delay, duplicate request, and rejoin: count exact item identities on server and both clients.
7. Full inventory/storage then disable one client: must fail closed with explicit notification, not discard items.
8. Uninstall and legacy save migration: require an overflow-safe export or refusal. No irreversible changes to shared world without verified rollback.
9. UI: 8 columns × 10 rows visible and operable on common resolutions/scales, with usable scroll behavior.
10. Only after all relevant tests pass: produce release-tagged client + server plugin package and verification log.

## Status

Runtime plugin: implemented and buildable, but OFF by default. CI validates direct hooks and Harmony 2.3.3 integration with fake game/server objects. Real two-client runtime tests, peer negotiation and overflow-safe uninstall are not complete; no production-ready DLL should be distributed.

## Hard-fail runtime compatibility gate

When explicitly enabled the plugin hashes the six original game/MP DLL files from the running process.
Only exact hashes from the supplied 3.0.1 installation are permitted. SHA mismatch stops activation
before installing ANY Harmony patches. A fault inside a hook sets a session-wide fault flag;
later CarryStore hooks stop modifying game state, and the log explicitly requires stopping gameplay
and restoring the backed-up test save. This does not undo earlier changes and is not a substitute
for a real-world save/rejoin test.

## v0.1.4 user-reported regression / must-run before any existing save

- [ ] Backpack opens at exactly **7 columns x 10 rows**; confirm 1x1 for 2x2 vanilla resources
- [ ] Ordinary 7x15 storage grid is visible, scrollable, and displays every stored item
- [ ] Place item in storage then move from locker to garden, reactor, aquarium, battery charger,
      scanner upgrade UI; confirm these panels keep their default dimensions and remain VISIBLE
- [ ] Return from any unmodified grid to locker; do 10 cycles: confirm no blank panels/no UI lag
- [ ] Move items in and out with both clients; save/reload/rejoin; confirm IDs and counts
- [ ] Verify 7x15 host metadata across standard locker, fridge, lifepod, Seatruck, waterproof,
      quantum and Exosuit; ensure aquarium/reactor/planter are NOT treated as bulk storage
- [ ] Confirm 3x3 scanner module grid still works after 7x15 bulk-storage changes
- [ ] Do not test changing normal vehicle equipment lists until a separate protocol-aware
      nine-module upgrade feature is implemented. These are not ItemsContainers.
- [ ] Back up and reduce backpack inventory to <=70 objects before changing 8x10 to 7x10;
      otherwise existing items may be displaced/inaccessible by the game

**Root cause of previously invisible grids:** `uGUI_Inventory.storage` reuses one
`uGUI_ItemsContainer` instance. Previous `GridScroll.Close` disabled the parent's
ScrollRect, but `GridScroll.Ensure` returned early for an unmodified container
without detaching the grid. The entire UI grid therefore remained under an
inactive GameObject while server-side transfer still accepted items.
v0.1.4 reparents the grid to its original parent and recomputes vanilla geometry;
when an expanded storage is reopened it reuses its previous ScrollRect.

## v0.1.5 regression: planter PDA coordinates

- The real user screenshot showed a 3x3 planter grid shifted left, overlapping the
  backpack after a 7x15 storage view. Root cause: ScrollRect initialization overwrote
  uGUI_ItemsContainer.rectTransform anchorMin/anchorMax and anchoredPosition with
  centered values, while v0.1.4 only restored its original parent and dimensions.
- Capture the original three RectTransform properties before creating the scroll
  wrapper and restore them **after** the vanilla OnResize call when returning to
  garden, planter, battery charger, or any unmodified grid.
- The Harmony test harness initializes the storage PDA at the right-side anchor
  (0.75, 0.5) and offset (-17.75, -71), switches through equipment/ordinary
  grids and three repeated locker cycles, and verifies exact anchor and offset
  restoration each time, with existing items retained.
- Manual game verification: open 7x15 locker, close, open ordinary planter and
  confirm it remains underneath DONICA on the RIGHT, not over the backpack;
  repeat for upgrades and charger panels. No item data should be modified by
  this UI-only repair.
