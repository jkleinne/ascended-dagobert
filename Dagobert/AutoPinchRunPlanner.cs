using System.Collections.Generic;

namespace Dagobert;

internal enum AutoPinchSellListWorkState
{
  Unavailable,
  Empty,
  HasItems
}

/// <summary>
/// Result of resume-aware retainer selection: the indexes to pinch and the
/// names skipped because they completed within the freshness window.
/// </summary>
internal sealed record AutoPinchResumeSelection(
  IReadOnlyList<int> Indexes,
  IReadOnlyList<string> SkippedRetainerNames);

internal static class AutoPinchRunPlanner
{
  internal static List<int> SelectRetainerIndexes(
    IReadOnlyList<string> retainerNames,
    IReadOnlySet<string> enabledRetainerNames,
    string allDisabledSentinel)
  {
    if (enabledRetainerNames.Contains(allDisabledSentinel))
      return [];

    if (enabledRetainerNames.Count == 0)
      return SelectAllIndexes(retainerNames.Count);

    var indexes = new List<int>();
    for (var index = 0; index < retainerNames.Count; index++)
    {
      if (enabledRetainerNames.Contains(retainerNames[index]))
        indexes.Add(index);
    }

    return indexes;
  }

  /// <summary>
  /// Selects retainers for a run while skipping ones whose full pinch completed
  /// within the freshness window. If skipping would leave nothing to do, the
  /// click means a deliberate full re-run, so the unfiltered selection returns
  /// with nothing reported as skipped.
  /// </summary>
  internal static AutoPinchResumeSelection SelectResumeRetainerIndexes(
    IReadOnlyList<string> retainerNames,
    IReadOnlySet<string> enabledRetainerNames,
    string allDisabledSentinel,
    IReadOnlySet<string> recentlyPinchedNames)
  {
    var enabledIndexes = SelectRetainerIndexes(retainerNames, enabledRetainerNames, allDisabledSentinel);

    var staleIndexes = new List<int>();
    var skippedNames = new List<string>();
    foreach (var index in enabledIndexes)
    {
      if (recentlyPinchedNames.Contains(retainerNames[index]))
        skippedNames.Add(retainerNames[index]);
      else
        staleIndexes.Add(index);
    }

    if (staleIndexes.Count == 0)
      return new AutoPinchResumeSelection(enabledIndexes, []);

    return new AutoPinchResumeSelection(staleIndexes, skippedNames);
  }

  internal static bool HasSellListItems(int itemCount)
  {
    return itemCount > 0;
  }

  internal static AutoPinchSellListWorkState GetSellListWorkState(
    bool isSellListAvailable,
    int itemCount)
  {
    if (!isSellListAvailable)
      return AutoPinchSellListWorkState.Unavailable;

    return HasSellListItems(itemCount)
      ? AutoPinchSellListWorkState.HasItems
      : AutoPinchSellListWorkState.Empty;
  }

  internal static bool ShouldStartCurrentRetainerRun(AutoPinchSellListWorkState workState)
  {
    return workState == AutoPinchSellListWorkState.HasItems;
  }

  internal static bool ShouldCompleteSelectedRetainerTask(AutoPinchSellListWorkState workState)
  {
    return workState != AutoPinchSellListWorkState.Unavailable;
  }

  private static List<int> SelectAllIndexes(int count)
  {
    var indexes = new List<int>(count);
    for (var index = 0; index < count; index++)
      indexes.Add(index);

    return indexes;
  }
}
