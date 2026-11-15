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
