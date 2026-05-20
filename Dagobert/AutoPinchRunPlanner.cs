using System.Collections.Generic;

namespace Dagobert;

internal enum AutoPinchSellListWorkState
{
  Unavailable,
  Empty,
  HasItems
}

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
