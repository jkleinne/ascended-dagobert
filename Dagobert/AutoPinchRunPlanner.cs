using System;
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
internal enum SaleHistoryStep
{
  OpenSaleHistory,
  DelayAfterOpenSaleHistory,
  WaitForRetainerHistory,
  DwellOnSaleHistory,
  CloseSaleHistory,
  DelayAfterCloseSaleHistory
}

internal enum SaleHistoryStepStatus
{
  Skipped,
  Complete,
  KeepWaiting,
  DeadlineExceeded
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

  /// <summary>
  /// Selects retainers for a run while skipping ones whose full pinch completed
  /// within the freshness window. If skipping would leave nothing to do, the
  /// full enabled selection returns with nothing reported as skipped, so
  /// launching auto pinch is never a no-op.
  /// </summary>
  internal static AutoPinchResumeSelection SelectResumeRetainerIndexes(
    IReadOnlyList<string> retainerNames,
    IReadOnlySet<string> enabledRetainerNames,
    string allDisabledSentinel,
    IReadOnlySet<string> recentlyPinchedNames)
  {
    var enabledIndexes = SelectRetainerIndexes(retainerNames, enabledRetainerNames, allDisabledSentinel);

    var remainingIndexes = new List<int>();
    var skippedNames = new List<string>();
    foreach (var index in enabledIndexes)
    {
      if (recentlyPinchedNames.Contains(retainerNames[index]))
        skippedNames.Add(retainerNames[index]);
      else
        remainingIndexes.Add(index);
    }

    if (remainingIndexes.Count == 0)
      return new AutoPinchResumeSelection(enabledIndexes, []);

    return new AutoPinchResumeSelection(remainingIndexes, skippedNames);
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

  private static readonly SaleHistoryStep[] SaleHistoryVisitSteps =
  [
    SaleHistoryStep.OpenSaleHistory,
    SaleHistoryStep.DelayAfterOpenSaleHistory,
    SaleHistoryStep.WaitForRetainerHistory,
    SaleHistoryStep.DwellOnSaleHistory,
    SaleHistoryStep.CloseSaleHistory,
    SaleHistoryStep.DelayAfterCloseSaleHistory
  ];

  /// <summary>
  /// Composes the per-retainer sale history visit. Empty when the feature is off or the
  /// menu label could not be resolved, so the run queue stays byte-for-byte unchanged.
  /// </summary>
  internal static IReadOnlyList<SaleHistoryStep> PlanSaleHistorySteps(
    bool isSaleHistoryVisitEnabled,
    string? saleHistoryLabel)
  {
    if (!isSaleHistoryVisitEnabled || string.IsNullOrWhiteSpace(saleHistoryLabel))
      return [];

    return SaleHistoryVisitSteps;
  }

  /// <summary>
  /// Finds a menu entry by its sheet label. Indexes are unsafe because the retainer menu
  /// is dynamic (e.g. a completed venture inserts a report entry), so entries are matched
  /// by text: exact ordinal first, then trimmed, then suffix — evaluated SeString payloads
  /// (the &lt;Gui(...)/&gt; prefix on the sell items rows) can leave leading glyphs on live text.
  /// </summary>
  internal static int? FindMenuEntryIndex(IReadOnlyList<string> entryTexts, string label)
  {
    for (var index = 0; index < entryTexts.Count; index++)
    {
      if (string.Equals(entryTexts[index], label, StringComparison.Ordinal))
        return index;
    }

    var trimmedLabel = label.Trim();
    for (var index = 0; index < entryTexts.Count; index++)
    {
      if (string.Equals(entryTexts[index].Trim(), trimmedLabel, StringComparison.Ordinal))
        return index;
    }

    for (var index = 0; index < entryTexts.Count; index++)
    {
      if (entryTexts[index].Trim().EndsWith(trimmedLabel, StringComparison.Ordinal))
        return index;
    }

    return null;
  }

  /// <summary>
  /// Decides a sale history step's fate. Skipped wins so a failed visit costs nothing
  /// further; Complete wins over the deadline so a finished step is never retro-skipped;
  /// the deadline converts a stalled step into a soft skip before the run-killing
  /// TaskManager guard timeout can fire.
  /// </summary>
  internal static SaleHistoryStepStatus GetSaleHistoryStepStatus(
    bool isVisitSkipped,
    bool isStepComplete,
    long startedAtTicks,
    long nowTicks,
    int deadlineMs)
  {
    if (isVisitSkipped)
      return SaleHistoryStepStatus.Skipped;

    if (isStepComplete)
      return SaleHistoryStepStatus.Complete;

    if (nowTicks - startedAtTicks >= deadlineMs)
      return SaleHistoryStepStatus.DeadlineExceeded;

    return SaleHistoryStepStatus.KeepWaiting;
  }

  /// <summary>
  /// Tries each candidate label in order. The retainer menu's sell entry text varies by
  /// inventory source (Addon sheet rows 2380/2381), so callers pass every plausible label.
  /// First-match-wins is safe only because the menu shows a single sell entry per context:
  /// both rows share the suffix "inventory on the market.", so a menu showing both at once
  /// could suffix-match the wrong row. Re-check before reordering or adding candidates.
  /// </summary>
  internal static int? FindFirstMenuEntryIndex(
    IReadOnlyList<string> entryTexts,
    IReadOnlyList<string> labels)
  {
    foreach (var label in labels)
    {
      var index = FindMenuEntryIndex(entryTexts, label);
      if (index is not null)
        return index;
    }

    return null;
  }

  private static List<int> SelectAllIndexes(int count)
  {
    var indexes = new List<int>(count);
    for (var index = 0; index < count; index++)
      indexes.Add(index);

    return indexes;
  }
}
