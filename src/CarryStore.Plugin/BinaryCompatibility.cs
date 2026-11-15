using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace Sirro.BZ.MP.CarryStore
{
    /// <summary>
    /// SHA256 allowlist for the exact 3.0.1 binaries the user supplied for ABI inspection.
    /// Fails closed when a runtime update or altered DLL changes the patch target implementation.
    /// The game/multiplayer assemblies are NEVER bundled with the plugin.
    /// </summary>
    internal static class BinaryCompatibility
    {
        private static readonly Dictionary<string, string> Expected =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Assembly-CSharp", "f5a9630cfd3f50c3a67e00170d96f2b48e05019e789443f61826ef4cf85c5b99" },
            { "Subnautica.API", "386b9e69df15cd1982dd35faba89f7ddb99f40b5f56f10b02c56d6fc7cec3908" },
            { "Subnautica.Client", "62b8f14287062e63b76e3e0b147dd9d1842231e5bc08728d6bd3c868238271db" },
            { "Subnautica.Server", "ebfb909990b2950e30bd14e646768959051c7b3e787676059c35dac5b86eff75" },
            { "Subnautica.Network", "e1da83bca889b77363d8b46c362fd1f2390016a76342a62f773cc792294b8f66" },
            { "Subnautica.Events", "b0a3abddcf9e4d97db66ca81c5032cc5fdd6737aff3ee0769bf72303b10f9a12" },
        };

        internal static void RequireExactBuild()
        {
            foreach (var item in Expected)
            {
                Assembly asm;
                try
                {
                    asm = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => string.Equals(a.GetName().Name, item.Key, StringComparison.OrdinalIgnoreCase))
                        ?? Assembly.Load(item.Key);
                }
                catch (Exception ex) { throw new InvalidOperationException("Missing required 3.0.1 assembly " + item.Key, ex); }

                if (string.IsNullOrEmpty(asm.Location) || !File.Exists(asm.Location))
                    throw new InvalidOperationException("Cannot hash required assembly " + item.Key);
                byte[] digest;
                using (var stream = File.OpenRead(asm.Location))
                using (var sha = SHA256.Create())
                    digest = sha.ComputeHash(stream);
                var actual = BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(actual, item.Value, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Unverified " + item.Key + " SHA256=" + actual +
                        "; CarryStore remains disabled to protect shared saves");
            }
            Diagnostic.Info("BINARY_COMPATIBILITY_PASS: verified all six 3.0.1 DLL hashes");
        }
    }
}
