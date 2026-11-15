using System;

namespace Sirro.BZ.MP.CarryStore
{
    /// <summary>Protocol dimensions used by the public BOT Benson multiplayer metadata model.</summary>
    public readonly struct GridSize : IEquatable<GridSize>
    {
        public byte Width { get; }
        public byte Height { get; }
        public int Slots => Width * Height;

        public GridSize(byte width, byte height)
        {
            if (width < 1 || width > 9)
                throw new ArgumentOutOfRangeException(nameof(width), "The upstream MP protocol uses Size % 10 for width.");
            if (height < 1 || width + height * 10 > byte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(height), "Packed dimensions must fit in one byte.");
            Width = width;
            Height = height;
        }

        public byte ToNetworkByte() => checked((byte)(Width + Height * 10));
        public static GridSize FromNetworkByte(byte size)
        {
            var width = (byte)(size % 10);
            var height = (byte)(size / 10);
            return new GridSize(width, height);
        }

        public bool Equals(GridSize other) => Width == other.Width && Height == other.Height;
        public override bool Equals(object? obj) => obj is GridSize other && Equals(other);
        public override int GetHashCode() => (Width << 8) | Height;
        public override string ToString() => Width + "x" + Height;
    }

    /// <summary>Desired dimensions; never modifies game state by itself.</summary>
    public static class CapacityPolicy
    {
        public static readonly GridSize Inventory = new GridSize(8, 10);
        public static readonly GridSize Locker = new GridSize(8, 10);
        public static readonly GridSize SmallLocker = new GridSize(8, 10);
        public static readonly GridSize ScannerUpgrades = new GridSize(3, 3);
        public static readonly GridSize ItemFootprint = new GridSize(1, 1);

        // Defaults verified only against the public multiplayer repository; v3.0.1 ABI needs confirmation.
        public static readonly GridSize PublicServerInventory = new GridSize(6, 8);
        public static readonly GridSize PublicServerLocker = new GridSize(6, 8);
        public static readonly GridSize PublicServerSmallLocker = new GridSize(5, 6);
        public static readonly GridSize PublicServerScanner = new GridSize(2, 2);

        /// <summary>Fail-closed release gate: no client-only or partially patched inventory expansion.</summary>
        public static bool CanActivate(bool clientHooksVerified, bool serverHooksVerified,
            bool compatibleConfigurationConfirmed, bool saveMigrationVerified) =>
            clientHooksVerified && serverHooksVerified &&
            compatibleConfigurationConfirmed && saveMigrationVerified;
    }
}
