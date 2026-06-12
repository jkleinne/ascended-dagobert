using System;
using System.Collections.Generic;

namespace Dagobert;

/// <summary>
/// Identifies a retainer for pinch-recency tracking. Carries the owning
/// character's content id so same-named retainers on different characters
/// never collide.
/// </summary>
internal readonly record struct RetainerPinchKey(ulong CharacterContentId, string RetainerName);

/// <summary>
/// Session-scoped record of when each retainer last completed a full auto pinch,
/// so a run relaunched after a timeout abort can skip retainers that already
/// finished within the freshness window. In-memory by design: the window is
/// minutes, so surviving plugin reloads is not worth disk writes. All access
/// happens on the Dalamud framework thread, so the state is deliberately
/// unsynchronized.
/// </summary>
internal sealed class RecentPinchTracker(Func<long> getTickCount)
{
  private readonly Dictionary<RetainerPinchKey, long> _completionTicks = [];

  /// <summary>
  /// Converts the configured minutes into the skip window, treating negative
  /// values as disabled. Point-of-use sanitization: the config file can carry
  /// values the UI clamp never saw.
  /// </summary>
  internal static TimeSpan GetSkipWindow(int configuredMinutes)
  {
    return TimeSpan.FromMinutes(Math.Max(0, configuredMinutes));
  }

  internal void MarkPinched(RetainerPinchKey key)
  {
    _completionTicks[key] = getTickCount();
  }

  /// <summary>
  /// Returns the subset of <paramref name="retainerNames"/> whose full pinch
  /// completed within <paramref name="window"/> (inclusive) for the given
  /// character. A non-positive window always returns an empty set.
  /// </summary>
  internal IReadOnlySet<string> GetRecentlyPinchedNames(
    ulong characterContentId,
    IReadOnlyList<string> retainerNames,
    TimeSpan window)
  {
    var recentlyPinched = new HashSet<string>();
    if (window <= TimeSpan.Zero)
      return recentlyPinched;

    var now = getTickCount();
    foreach (var name in retainerNames)
    {
      if (_completionTicks.TryGetValue(new RetainerPinchKey(characterContentId, name), out var completedAt)
          && now - completedAt <= window.TotalMilliseconds)
      {
        recentlyPinched.Add(name);
      }
    }

    return recentlyPinched;
  }
}
