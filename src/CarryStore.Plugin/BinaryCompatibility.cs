using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace Sirro.BZ.MP.CarryStore
{
    /// <summary>
    /// SHA256 and in-memory module-version allowlist for the exact 3.0.1 game DLLs.
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

        // Module version IDs extracted directly from the SAME six original 3.0.1 binaries.
        // When a launcher loads bytes via Assembly.Load(byte[]), Assembly.Location is empty
        // even though the assembly is valid. In that case compare the in-memory module MVID.
        private static readonly Dictionary<string, Guid> ExpectedMvid =
            new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
        {
            { "Assembly-CSharp", new Guid("dd06c891-a952-4a5c-8663-e89fb2868b5c") },
            { "Subnautica.API", new Guid("ec5ae426-d8d9-4444-a236-ac84d3e2f426") },
            { "Subnautica.Client", new Guid("b677faee-7813-48a0-87ed-429c9a71522d") },
            { "Subnautica.Server", new Guid("7bca8db2-1290-42e6-a690-928186c7b065") },
            { "Subnautica.Network", new Guid("e6fd323d-1a4a-4e79-b050-463870c0c57c") },
            { "Subnautica.Events", new Guid("0bd4f7b5-43b7-44b4-bc8a-ea4daf3f0588") },
        };

        /// <summary>
        /// Return the evidence used: SHA256 if the loaded assembly has a readable source file,
        /// otherwise MVID for the in-memory assembly. A present but wrong SHA256 is NEVER
        /// accepted on the strength of a matching MVID.
        /// </summary>
        internal static string VerifyBuildFingerprint(string assemblyName, Guid loadedMvid, string? actualSha256)
        {
            if (!Expected.TryGetValue(assemblyName, out var expectedHash) ||
                !ExpectedMvid.TryGetValue(assemblyName, out var expectedModuleId))
                throw new InvalidOperationException("Unknown assembly identity: " + assemblyName);

            if (actualSha256 != null)
            {
                if (!string.Equals(actualSha256, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Unverified " + assemblyName + " SHA256=" + actualSha256 +
                        "; CarryStore remains disabled to protect shared saves");
                return "SHA256";
            }

            if (loadedMvid != expectedModuleId)
                throw new InvalidOperationException("Unverified memory-loaded " + assemblyName +
                    " MVID=" + loadedMvid + " (expected " + expectedModuleId + "); CarryStore remains disabled");

            return "MVID";
        }

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
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Missing required 3.0.1 assembly " + item.Key, ex);
                }

                string? location = null;
                try
                {
                    // Mono / Unity can return an empty path for assemblies loaded from memory.
                    location = asm.Location;
                }
                catch (NotSupportedException) { }

                string? actualHash = null;
                if (!string.IsNullOrEmpty(location) && File.Exists(location))
                {
                    byte[] digest;
                    using (var stream = File.OpenRead(location))
                    using (var sha = SHA256.Create())
                        digest = sha.ComputeHash(stream);
                    actualHash = BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant();
                }

                Guid loadedMvid;
                try
                {
                    loadedMvid = asm.ManifestModule.ModuleVersionId;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Cannot identify in-memory module " + item.Key, ex);
                }

                var evidence = VerifyBuildFingerprint(item.Key, loadedMvid, actualHash);
                Diagnostic.Info("VERIFIED_" + evidence + ": " + item.Key +
                    (evidence == "MVID" ? " (launcher loaded from memory, module=" + loadedMvid + ")" : ""));
            }
            Diagnostic.Info("BINARY_COMPATIBILITY_PASS: six original 3.0.1 modules verified (SHA256 or in-memory MVID)");
        }
    }
}
