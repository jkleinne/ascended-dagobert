namespace Dagobert;

internal sealed class MarketBoardPriceRequestState
{
  private const int FirstRequestVersion = 1;

  private int _version;

  public int Version => _version;

  public bool IsActive { get; private set; }

  public int BeginRequest()
  {
    _version = _version == int.MaxValue
      ? FirstRequestVersion
      : _version + 1;
    IsActive = true;
    return _version;
  }

  public bool IsCurrent(int requestVersion)
  {
    return IsActive && requestVersion == _version;
  }

  public void FinishRequest(int requestVersion)
  {
    if (requestVersion != _version)
      return;

    IsActive = false;
  }
}
