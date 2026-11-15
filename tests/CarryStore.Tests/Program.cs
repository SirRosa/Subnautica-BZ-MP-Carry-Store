using Sirro.BZ.MP.CarryStore;

var cases = new[] {
    (CapacityPolicy.Inventory, 80, 108),
    (CapacityPolicy.Locker, 80, 108),
    (CapacityPolicy.SmallLocker, 80, 108),
    (CapacityPolicy.ScannerUpgrades, 9, 33),
    (CapacityPolicy.ItemFootprint, 1, 11),
    (CapacityPolicy.PublicServerInventory, 48, 86),
    (CapacityPolicy.PublicServerLocker, 48, 86),
    (CapacityPolicy.PublicServerSmallLocker, 30, 65),
    (CapacityPolicy.PublicServerScanner, 4, 22),
};
var checks = 0;
static void Require(bool condition, string message) {
    if (!condition) throw new Exception(message);
}
foreach (var (size, slots, wire) in cases) {
    Require(size.Slots == slots, "Wrong slot count for " + size); checks++;
    Require(size.ToNetworkByte() == wire, "Wrong wire size for " + size); checks++;
    Require(GridSize.FromNetworkByte((byte)wire).Equals(size), "Wire roundtrip failed"); checks++;
}
foreach (var fail in new[] {
    (width: (byte)0, height: (byte)8),
    (width: (byte)10, height: (byte)8),
    (width: (byte)8, height: (byte)0),
    (width: (byte)9, height: (byte)25),
}) {
    try {
        _ = new GridSize(fail.width, fail.height);
        throw new Exception("Invalid grid accepted: " + fail);
    } catch (ArgumentOutOfRangeException) {
        checks++;
    }
}
for (int bits = 0; bits < 16; bits++) {
    var safe = CapacityPolicy.CanActivate((bits & 1) != 0, (bits & 2) != 0,
        (bits & 4) != 0, (bits & 8) != 0);
    Require(safe == (bits == 15), "Partial activation accepted: " + bits); checks++;
}
Console.WriteLine($"PASS {checks} protocol/guardrail assertions (NOT gameplay validation)");
