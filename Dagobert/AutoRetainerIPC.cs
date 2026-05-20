using System.Linq;

namespace Dagobert;

internal sealed class AutoRetainerIPC : IAutoRetainerSuppressionGateway
{
  internal const string AutoRetainerInternalName = "AutoRetainer";
  internal const string GetSuppressedIpc = "AutoRetainer.GetSuppressed";
  internal const string SetSuppressedIpc = "AutoRetainer.SetSuppressed";

  public bool TryGetSuppressed(out bool isSuppressed)
  {
    var state = new AutoRetainerIpcState(IsAutoRetainerLoaded());
    if (!state.IsAutoRetainerLoaded)
      Plugin.Log.Debug("AutoRetainer is not loaded, skipping suppression read");

    return state.TryReadSuppressed(
      () => Plugin.PluginInterface.GetIpcSubscriber<bool>(GetSuppressedIpc).InvokeFunc(),
      ex => Plugin.Log.Warning(ex, "AutoRetainer IPC read failed while checking suppression state"),
      out isSuppressed);
  }

  public bool TrySetSuppressed(bool isSuppressed)
  {
    var state = new AutoRetainerIpcState(IsAutoRetainerLoaded());
    if (!state.IsAutoRetainerLoaded)
      Plugin.Log.Debug("AutoRetainer is not loaded, skipping suppression write");

    return state.TryWriteSuppressed(
      isSuppressed,
      value => Plugin.PluginInterface.GetIpcSubscriber<bool, object>(SetSuppressedIpc).InvokeAction(value),
      ex => Plugin.Log.Warning(
        ex,
        "AutoRetainer IPC write failed while setting suppression state to {IsSuppressed}",
        isSuppressed));
  }

  private static bool IsAutoRetainerLoaded()
  {
    return Plugin.PluginInterface.InstalledPlugins.Any(plugin =>
      plugin.InternalName == AutoRetainerInternalName && plugin.IsLoaded);
  }
}
