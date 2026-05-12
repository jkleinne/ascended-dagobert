using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using System;
using System.IO;

namespace Dagobert;

internal readonly record struct MarketBoardRequestStartedEventArgs(
  uint Status,
  uint AmountToArrive)
{
  public bool Ok => Status == 0;
}

internal sealed class MarketBoardRequestTracker : IDisposable
{
  [Signature(
    "48 89 5C 24 ?? 57 48 83 EC 20 48 8B 0D ?? ?? ?? ?? 48 8B DA E8 ?? ?? ?? ??",
    DetourName = nameof(ItemRequestStartPacketDetour))]
  private readonly Hook<MarketBoardItemRequestStartPacketHandler>? _itemRequestStartPacketDetourHook = null;

  private readonly IPluginLog _log;

  private delegate void MarketBoardItemRequestStartPacketHandler(uint a1, nint packetRef);

  public event Action<MarketBoardRequestStartedEventArgs>? RequestStarted;

  public MarketBoardRequestTracker(IGameInteropProvider gameInteropProvider, IPluginLog log)
  {
    _log = log;
    try
    {
      gameInteropProvider.InitializeFromAttributes(this);
      _itemRequestStartPacketDetourHook?.Enable();
    }
    catch (Exception ex)
    {
      _log.Error(ex, "Failed to initialize market board request tracker");
    }
  }

  public void Dispose()
  {
    _itemRequestStartPacketDetourHook?.Dispose();
  }

  private void ItemRequestStartPacketDetour(uint a1, nint packetRef)
  {
    try
    {
      RequestStarted?.Invoke(ReadRequestStarted(packetRef));
    }
    catch (Exception ex)
    {
      _log.Error(ex, "Failed to read market board request start packet");
    }
    finally
    {
      _itemRequestStartPacketDetourHook!.Original(a1, packetRef);
    }
  }

  private static unsafe MarketBoardRequestStartedEventArgs ReadRequestStarted(nint packetRef)
  {
    const int requestHeaderBytes = 8;

    using var stream = new UnmanagedMemoryStream((byte*)packetRef, requestHeaderBytes);
    using var reader = new BinaryReader(stream);

    var status = reader.ReadUInt32();
    var amountToArrive = reader.ReadUInt32();
    return new MarketBoardRequestStartedEventArgs(status, amountToArrive);
  }
}
