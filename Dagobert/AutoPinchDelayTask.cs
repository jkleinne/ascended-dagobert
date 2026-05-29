using System;

namespace Dagobert;

internal static class AutoPinchDelayTask
{
  internal static Func<bool?> Create(int delayMs, Func<long> getTickCount)
  {
    ArgumentNullException.ThrowIfNull(getTickCount);

    var normalizedDelayMs = Math.Max(0, delayMs);
    long? startedAt = null;

    return () =>
    {
      var now = getTickCount();
      startedAt ??= now;
      return now - startedAt.Value >= normalizedDelayMs;
    };
  }
}
