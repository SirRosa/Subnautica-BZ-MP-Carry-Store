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
            // Loader enumeration alone is not evidence that the gameplay patches have been activated.
            Diagnostic.Info("BOOT: native plugin OnEnabled invoked. v0.1.1-dev, runtime process=" +
                System.Diagnostics.Process.GetCurrentProcess().Id);
            Diagnostic.Info("CONFIG_PATH: " + Configuration.ConfigPath);
            if (!Configuration.ExperimentalEnabled())
            {
                Diagnostic.Info("STATUS=CONFIG_DISABLED. enable_experimental=false. No gameplay patches installed.");
                return;
            }
            Diagnostic.Info("STATUS=CONFIG_ENABLED. Checking game and multiplayer DLLs.");
            try
            {
                BinaryCompatibility.RequireExactBuild();
                Diagnostic.Info("STATUS=DLL_MATCH. Installing Harmony hooks.");
                RuntimeHooks.Install();
                Diagnostic.Info("STATUS=PATCHES_INSTALLED. This is NOT a live two-client certification.");
            }
            catch (Exception error)
            {
                RuntimeHooks.Uninstall();
                Diagnostic.Info("STATUS=PATCH_FAILED: " + error);
            }
        }

        public override void OnDisabled()
        {
            Diagnostic.Info("STATUS=PLUGIN_DISABLED. Unpatching own Harmony hooks.");
            RuntimeHooks.Uninstall();
        }
    }

    internal static class Configuration
    {
        internal static string ConfigDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".botbenson", "Subnautica Below Zero", "Game", "Config");

        internal static string ConfigPath => Path.Combine(ConfigDirectory, "CarryStore.ini");

        internal static bool ParseExplicitOptIn(string text)
        {
            int enableLines = 0;
            bool enabled = false;
            foreach (string sourceLine in text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
            {
                string line = sourceLine.Trim();
                if (line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal)
                    || line.Length == 0) continue;
                var parts = line.Split(new[] { '=' }, 2);
                if (parts.Length != 2 ||
                    !parts[0].Trim().Equals("enable_experimental", StringComparison.OrdinalIgnoreCase))
                    continue;
                enableLines++;
                enabled = parts[1].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            // Ambiguous/duplicate flags fail closed, never silently enable.
            return enableLines == 1 && enabled;
        }

        internal static bool ExperimentalEnabled()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectory);
                if (!File.Exists(ConfigPath))
                {
                    File.WriteAllLines(ConfigPath, new[] {
                        "# Carry & Store dev build. OFF until deliberately enabled on a disposable test save.",
                        "# Set enable_experimental=true, then RESTART BOTH GAME CLIENTS.",
                        "# Diagnostic status is written to CarryStore-status.log in this same folder.",
                        "enable_experimental=false" });
                    Diagnostic.Info("CONFIG_CREATED: disabled by default until explicitly changed.");
                }
                var enabled = ParseExplicitOptIn(File.ReadAllText(ConfigPath));
                Diagnostic.Info("CONFIG_READ: enable_experimental=" + (enabled ? "true" : "false") +
                    " (changes require restarting the game).");
                return enabled;
            }
            catch (Exception error)
            {
                Diagnostic.Info("CONFIG_ERROR (disabled): " + error);
                return false;
            }
        }
    }

    internal static class Diagnostic
    {
        internal static string StatusPath => Path.Combine(Configuration.ConfigDirectory, "CarryStore-status.log");

        internal static void Info(string message)
        {
            // Write our own diagnostic file: native plugin may initialize before regular game logging.
            try
            {
                Directory.CreateDirectory(Configuration.ConfigDirectory);
                File.AppendAllText(StatusPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                    " [CarryStore] " + message + Environment.NewLine);
            }
            catch { /* Failure to write a diagnostic must not mutate save data or crash the game. */ }

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
