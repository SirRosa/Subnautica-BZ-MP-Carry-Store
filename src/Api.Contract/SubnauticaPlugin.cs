// ABI-only contract extracted from released BOT Benson 3.0.1 Subnautica.API.dll.
// NEVER PACKAGE this file's compiled Assembly; game provides Subnautica.API.dll.
namespace Subnautica.API.Enums
{
    public enum SubnauticaPluginPriority { First, Medium, Last }
    public enum SubnauticaPluginType { Client, Server, ServerClient }
}
namespace Subnautica.API.Features
{
    public abstract class SubnauticaPlugin
    {
        public virtual string Name { get; } = string.Empty;
        public virtual Subnautica.API.Enums.SubnauticaPluginPriority Priority { get; } = Subnautica.API.Enums.SubnauticaPluginPriority.Medium;
        public virtual Subnautica.API.Enums.SubnauticaPluginType PluginType { get; } = Subnautica.API.Enums.SubnauticaPluginType.Client;
        public virtual void OnEnabled() { }
        public virtual void OnDisabled() { }
    }
}
