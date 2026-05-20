namespace Dagobert;

internal interface IAutoRetainerSuppressionGateway
{
  bool TryGetSuppressed(out bool isSuppressed);

  bool TrySetSuppressed(bool isSuppressed);
}

internal sealed class AutoRetainerSuppressionSession
{
  private readonly IAutoRetainerSuppressionGateway _gateway;
  private readonly bool _previousSuppressed;
  private bool _hasRestored;

  internal AutoRetainerSuppressionSession(
    IAutoRetainerSuppressionGateway gateway,
    bool previousSuppressed)
  {
    _gateway = gateway;
    _previousSuppressed = previousSuppressed;
  }

  internal bool Restore()
  {
    if (_hasRestored)
      return true;

    if (!_gateway.TrySetSuppressed(_previousSuppressed))
      return false;

    _hasRestored = true;
    return true;
  }
}

internal sealed class AutoRetainerSuppressionCoordinator(IAutoRetainerSuppressionGateway gateway)
{
  private AutoRetainerSuppressionSession? _activeSession;

  internal bool HasActiveSession => _activeSession is not null;

  internal void BeginRun()
  {
    if (_activeSession is not null)
      return;

    if (!gateway.TryGetSuppressed(out var wasSuppressed))
      return;

    if (!gateway.TrySetSuppressed(true))
      return;

    _activeSession = new AutoRetainerSuppressionSession(gateway, wasSuppressed);
  }

  internal bool EndRun()
  {
    var session = _activeSession;
    if (session is null)
      return false;

    if (!session.Restore())
      return false;

    _activeSession = null;
    return true;
  }

  internal bool EndRunIfIdle(bool isTaskManagerBusy)
  {
    if (isTaskManagerBusy || _activeSession is null)
      return false;

    return EndRun();
  }
}
