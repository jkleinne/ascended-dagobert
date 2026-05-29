namespace Dagobert;

internal static class AutoPinchTimeoutPolicy
{
  internal const int GeneralTaskTimeoutMs = 30_000;
  internal const int MarketPriceWaitTimeoutMs = 45_000;
  internal const int LegacyTaskManagerBackstopPaddingMs = 5_000;

  internal static int GetLegacyBackstopTimeoutMs(int dagobertTimeoutMs)
  {
    return checked(dagobertTimeoutMs + LegacyTaskManagerBackstopPaddingMs);
  }
}
