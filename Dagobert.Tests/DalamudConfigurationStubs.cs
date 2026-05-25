namespace Dalamud.Configuration
{
  public interface IPluginConfiguration
  {
    int Version { get; set; }
  }
}

namespace Dalamud.Game.ClientState.Keys
{
  public enum VirtualKey
  {
    Q,
    SHIFT
  }
}

namespace Dalamud.Plugin
{
  public interface IDalamudPluginInterface
  {
    void SavePluginConfig(Dalamud.Configuration.IPluginConfiguration config);
  }
}

namespace Dagobert
{
  internal static class Plugin
  {
    internal static Dalamud.Plugin.IDalamudPluginInterface PluginInterface { get; set; } = null!;
  }
}
