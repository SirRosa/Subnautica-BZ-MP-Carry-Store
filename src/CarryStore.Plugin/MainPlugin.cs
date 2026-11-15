using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Subnautica.API.Enums;
using Subnautica.API.Features;

namespace Sirro.BZ.MP.CarryStore
{
    /// <summary>
    /// Native BOT Benson ServerClient plugin, no BepInEx or Nautilus.
    /// Experimental modifications require explicit opt-in until real-world testing completes.
    /// </summary>
    public sealed class MainPlugin : SubnauticaPlugin
    {
        public override string Name => "Sirro Carry & Store (experimental)";
        public override SubnauticaPluginPriority Priority => SubnauticaPluginPriority.Last;
        public override SubnauticaPluginType PluginType => SubnauticaPluginType.ServerClient;

        public override void OnEnabled()
        {
            if (!Configuration.ExperimentalEnabled())
            {
                Diagnostic.Info("INACTIVE: enable_experimental=false. World/storage not modified.");
                return;
            }
            try
            {
                BinaryCompatibility.RequireExactBuild();
                RuntimeHooks.Install();
                Diagnostic.Info("EXPERIMENTAL_HOOKS_INSTALLED: matched 3.0.1 binary hashes; still NOT a live two-client certification.");
            }
            catch (Exception error)
            {
                RuntimeHooks.Uninstall();
                Diagnostic.Info("DISABLED (ABI/patch gate): " + error);
            }
        }

        public override void OnDisabled()
        {
            RuntimeHooks.Uninstall();
        }
    }

    internal static class Configuration
    {
        internal static bool ExperimentalEnabled()
        {
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    ".botbenson", "Subnautica Below Zero", "Game", "Config");
                Directory.CreateDirectory(root);
                string path = Path.Combine(root, "CarryStore.ini");
                if (!File.Exists(path))
                    File.WriteAllLines(path, new[] {
                        "# DEVELOPMENT ONLY. Only use on a disposable backed-up world with both players and host.",
                        "# No version handshake or live persistence test has passed yet.",
                        "enable_experimental=false" });
                return File.ReadAllLines(path).Any(s =>
                {
                    string[] pair = s.Split(new[] { '=' }, 2);
                    return pair.Length == 2 &&
                        pair[0].Trim().Equals("enable_experimental", StringComparison.OrdinalIgnoreCase) &&
                        pair[1].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                });
            }
            catch (Exception error)
            {
                Diagnostic.Info("CONFIG_ERROR (disabled): " + error.Message);
                return false;
            }
        }
    }

    internal static class Diagnostic
    {
        internal static void Info(string message)
        {
            try
            {
                var log = typeof(SubnauticaPlugin).Assembly.GetType("Subnautica.API.Features.Log");
                var method = log?.GetMethod("Info", BindingFlags.Static | BindingFlags.Public,
                    null, new[] { typeof(string) }, null);
                method?.Invoke(null, new object[] { "[CarryStore] " + message });
            }
            catch { }
        }

        internal static void Error(string area, Exception e) =>
            Info("HOOK_ERROR " + area + ": " + e.GetType().Name + " " + e.Message);
    }
}
