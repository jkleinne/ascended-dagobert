namespace Dagobert;

internal static class AutoPinchTimeoutPolicy
{
  internal const int GeneralTaskTimeoutMs = 30_000;
  internal const int MarketPriceWaitTimeoutMs = 45_000;
  internal const int LegacyTaskManagerBackstopPaddingMs = 5_000;

  /// <summary>
  /// Self-deadline for each sale history visit step. Must stay strictly below
  /// <see cref="GeneralTaskTimeoutMs"/> so the soft-skip path always wins over the
  /// run-killing guard timeout (invariant pinned by a unit test).
  /// </summary>
  internal const int SaleHistoryStepDeadlineMs = 10_000;

  internal static int GetLegacyBackstopTimeoutMs(int dagobertTimeoutMs)
  {
    return checked(dagobertTimeoutMs + LegacyTaskManagerBackstopPaddingMs);
  }
}
