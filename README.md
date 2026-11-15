# Carry & Store — Subnautica Below Zero Multiplayer 3.0.1

Native plugin project targeting BOT Benson multiplayer, not Nitrox. The full runtime mod is **not yet implemented or certified**.

## Requested behavior

- Player inventory: 8 × 10 (80 slots)
- All item footprints in inventory grids: 1 × 1
- Locker and SmallLocker: 8 × 10 (80 slots)
- Scanner Room upgrade storage: 3 × 3 (9 slots)

## Compatibility boundary

A client-only UI patch is insufficient: the multiplayer server validates container capacity and persists item footprint metadata. A safe release requires matching client and authoritative server behavior, verified against the actual 3.0.1 binaries and two-player runtime tests.

Development work lives on `test/carry-store-301`. See `docs/ARCHITECTURE.md` and `docs/TEST-PLAN.md` on that branch. Do not distribute or install development builds on a production save.
