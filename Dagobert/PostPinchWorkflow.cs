using System;
using System.Collections.Generic;

namespace Dagobert;

internal enum PostPinchWorkflowAction
{
  PreparePriceRequest,
  WaitForMarketPrice,
  SetNewPrice
}

internal static class PostPinchWorkflow
{
  private static readonly IReadOnlyList<PostPinchWorkflowAction> NoActions =
    Array.Empty<PostPinchWorkflowAction>();

  private static readonly IReadOnlyList<PostPinchWorkflowAction> StartActions =
  [
    PostPinchWorkflowAction.PreparePriceRequest,
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

    return StartActions;
  }
}
