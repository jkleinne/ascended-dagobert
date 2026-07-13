namespace Dagobert;

internal enum PriceApplicationFlow
{
  AutoPinchRun,
  PostPinch
}

internal enum PriceApplicationAction
{
  ApplyComputedPrice,
  RejectKeepDialogOpen,
  RejectDismissDialog
}

internal enum PriceApplicationReason
{
  Computed,
  ManualEdit,
  NoComputedPrice,
  CutAboveMax,
  RaiseAboveMax
}

internal readonly record struct PriceApplicationOptions(
  float MaxUndercutPercentage,
  bool EnableMaxRaiseGuard,
  float MaxRaisePercentage,
  PriceApplicationFlow Flow)
{
  private const float DefaultMaxUndercutPercentage = 100.0f;

  /// <summary>
  /// Builds options from raw configuration values. Hand-edited config files can carry
  /// non-finite floats, and NaN comparisons are always false in C#, which would silently
  /// neutralize the cut guard or half-enable the raise guard. Non-finite values therefore
  /// fall back to the config default (cut) or disable the guard entirely (raise).
  /// </summary>
  public static PriceApplicationOptions FromConfig(
    float maxUndercutPercentage,
    bool enableMaxRaiseGuard,
    float maxRaisePercentage,
    PriceApplicationFlow flow)
  {
    var sanitizedMaxUndercut = float.IsFinite(maxUndercutPercentage)
      ? maxUndercutPercentage
      : DefaultMaxUndercutPercentage;
    var raiseGuardUsable = enableMaxRaiseGuard
      && float.IsFinite(maxRaisePercentage)
      && maxRaisePercentage > 0f;

    return new PriceApplicationOptions(
      sanitizedMaxUndercut,
      raiseGuardUsable,
      raiseGuardUsable ? maxRaisePercentage : 0f,
      flow);
  }
}

internal readonly record struct PriceApplicationDecision(
  PriceApplicationAction Action,
  int PriceToSet,
  PriceApplicationReason Reason,
  float ChangePercent);

/// <summary>
/// Decides how a computed market board price is applied to the RetainerSell dialog.
/// A manually edited price field always wins: the plugin never overwrites or confirms
/// a price it did not compute, because a manual value may be mid-keystroke. Rejects
/// route per flow — the post-pinch flow leaves the dialog open for manual pricing,
/// the full auto-pinch run dismisses it so the run continues.
/// </summary>
internal static class PriceApplicationPolicy
{
  public static PriceApplicationDecision Decide(
    int? computedPrice,
    int? baselinePrice,
    int currentFieldValue,
    PriceApplicationOptions options)
  {
    if (baselinePrice is not null && currentFieldValue != baselinePrice.Value)
      return Reject(options.Flow, PriceApplicationReason.ManualEdit, 0f);

    if (computedPrice is null || computedPrice.Value <= 0)
      return Reject(options.Flow, PriceApplicationReason.NoComputedPrice, 0f);

    var referencePrice = baselinePrice ?? currentFieldValue;
    if (referencePrice <= 0)
      return Apply(computedPrice.Value, 0f);

    var changePercent = ((float)computedPrice.Value - referencePrice) / referencePrice * 100f;
    if (changePercent < 0f && -changePercent > options.MaxUndercutPercentage)
      return Reject(options.Flow, PriceApplicationReason.CutAboveMax, changePercent);

    if (changePercent > 0f && options.EnableMaxRaiseGuard && changePercent > options.MaxRaisePercentage)
      return Reject(options.Flow, PriceApplicationReason.RaiseAboveMax, changePercent);

    return Apply(computedPrice.Value, changePercent);
  }

  private static PriceApplicationDecision Apply(int price, float changePercent)
    => new(PriceApplicationAction.ApplyComputedPrice, price, PriceApplicationReason.Computed, changePercent);

  private static PriceApplicationDecision Reject(
    PriceApplicationFlow flow,
    PriceApplicationReason reason,
    float changePercent)
  {
    var action = flow == PriceApplicationFlow.PostPinch
      ? PriceApplicationAction.RejectKeepDialogOpen
      : PriceApplicationAction.RejectDismissDialog;
    return new PriceApplicationDecision(action, 0, reason, changePercent);
  }
}
