using System;
using System.Collections.Generic;

namespace Dagobert;

internal enum PostPinchWorkflowAction
{
  PreparePriceRequest,
  ForceComparePrice,
  DelayForMarketBoard,
  WaitForMarketPrice,
  SetNewPrice
}

internal static class PostPinchWorkflow
{
  private static readonly IReadOnlyList<PostPinchWorkflowAction> NoActions =
    Array.Empty<PostPinchWorkflowAction>();

  private static readonly IReadOnlyList<PostPinchWorkflowAction> CompareButtonStartActions =
  [
    PostPinchWorkflowAction.PreparePriceRequest,
    PostPinchWorkflowAction.WaitForMarketPrice,
    PostPinchWorkflowAction.SetNewPrice
  ];

  private static readonly IReadOnlyList<PostPinchWorkflowAction> SellAddonSetupStartActions =
  [
    PostPinchWorkflowAction.ForceComparePrice,
    PostPinchWorkflowAction.DelayForMarketBoard,
    PostPinchWorkflowAction.WaitForMarketPrice,
    PostPinchWorkflowAction.SetNewPrice
  ];

  internal static IReadOnlyList<PostPinchWorkflowAction> PlanActions(
    bool isCompareButtonPress,
    bool isFeatureEnabled,
    bool isPostPinchKeyHeld,
    bool isTaskManagerBusy,
    bool isSellAddonReady)
  {
    if (!isCompareButtonPress || !isFeatureEnabled || !isPostPinchKeyHeld || isTaskManagerBusy || !isSellAddonReady)
      return NoActions;

    return CompareButtonStartActions;
  }

  internal static IReadOnlyList<PostPinchWorkflowAction> PlanSellAddonSetupActions(
    bool isFeatureEnabled,
    bool isPostPinchKeyHeld,
    bool isTaskManagerBusy,
    bool isSellAddonReady)
  {
    if (!isFeatureEnabled || !isPostPinchKeyHeld || isTaskManagerBusy || !isSellAddonReady)
      return NoActions;

    return SellAddonSetupStartActions;
  }
}
