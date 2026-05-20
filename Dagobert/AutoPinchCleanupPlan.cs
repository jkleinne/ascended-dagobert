using System.Collections.Generic;

namespace Dagobert;

internal enum AutoPinchCleanupAction
{
  AbortTasks,
  EndSuppression,
  LogException,
  RemoveTalkListeners
}

internal static class AutoPinchCleanupPlan
{
  internal static IReadOnlyList<AutoPinchCleanupAction> PlanCancelActions(bool hasActiveSuppression)
  {
    var actions = new List<AutoPinchCleanupAction> { AutoPinchCleanupAction.AbortTasks };
    AddEndSuppression(actions, hasActiveSuppression);
    actions.Add(AutoPinchCleanupAction.RemoveTalkListeners);
    return actions;
  }

  internal static IReadOnlyList<AutoPinchCleanupAction> PlanDrawCatchActions(bool hasActiveSuppression)
  {
    var actions = new List<AutoPinchCleanupAction> { AutoPinchCleanupAction.AbortTasks };
    AddEndSuppression(actions, hasActiveSuppression);
    actions.Add(AutoPinchCleanupAction.LogException);
    actions.Add(AutoPinchCleanupAction.RemoveTalkListeners);
    return actions;
  }

  internal static IReadOnlyList<AutoPinchCleanupAction> PlanDisposeActions(
    bool hasActiveSuppression,
    bool hasTalkAddonListeners)
  {
    var actions = new List<AutoPinchCleanupAction>();
    AddEndSuppression(actions, hasActiveSuppression);
    AddRemoveTalkListeners(actions, hasTalkAddonListeners);
    return actions;
  }

  internal static IReadOnlyList<AutoPinchCleanupAction> PlanIdleActions(
    bool hasActiveSuppression,
    bool hasTalkAddonListeners,
    bool isTaskManagerBusy)
  {
    if (isTaskManagerBusy)
      return [];

    var actions = new List<AutoPinchCleanupAction>();
    AddEndSuppression(actions, hasActiveSuppression);
    AddRemoveTalkListeners(actions, hasTalkAddonListeners);
    return actions;
  }

  private static void AddEndSuppression(
    List<AutoPinchCleanupAction> actions,
    bool hasActiveSuppression)
  {
    if (hasActiveSuppression)
      actions.Add(AutoPinchCleanupAction.EndSuppression);
  }

  private static void AddRemoveTalkListeners(
    List<AutoPinchCleanupAction> actions,
    bool hasTalkAddonListeners)
  {
    if (hasTalkAddonListeners)
      actions.Add(AutoPinchCleanupAction.RemoveTalkListeners);
  }
}
