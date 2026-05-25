using Dalamud.Configuration;
using Dalamud.Game.ClientState.Keys;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Dagobert;

public enum UndercutMode
{
  FixedAmount,
  Percentage
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
  public int Version { get; set; } = 2;

  public bool HQ { get; set; } = true;

  public int GetMBPricesDelayMS { get; set; } = 3000;

  public int MarketBoardKeepOpenMS { get; set; } = 1000;

  public bool ShowErrorsInChat { get; set; } = true;

  public bool EnablePinchKey { get; set; } = false;

  public VirtualKey PinchKey { get; set; } = VirtualKey.Q;

  public bool EnablePostPinchkey { get; set; } = true;

  public VirtualKey PostPinchKey { get; set; } = VirtualKey.SHIFT;

  public UndercutMode UndercutMode { get; set; } = UndercutMode.FixedAmount;

  public int UndercutAmount { get; set; } = 1;

  public float UndercutAmountPercentage { get; set; } = 1.0f;

  public float MaxUndercutPercentage { get; set; } = 100.0f;

  public bool UndercutSelf { get; set; } = false;

  public bool EnableBaitGuard { get; set; } = true;

  public float BaitGuardFloorPercent { get; set; } = 30.0f;

  public int BaitGuardSampleListings { get; set; } = 5;

  public float BaitGuardGapPercent { get; set; } = 50.0f;

  public int BaitGuardMinQuantity { get; set; } = 1;

  public float BaitGuardSaleMedianFloorPercent { get; set; } = 50.0f;

  public int BaitGuardLowClusterListings { get; set; } = 3;

  public int BaitGuardLowClusterQuantity { get; set; } = 20;

  public float BaitGuardLowClusterPriceTolerancePercent { get; set; } = 5.0f;

  public int BaitGuardSaleReferenceMinRecentSales { get; set; } = 3;

  public int BaitGuardSaleReferenceMaxSaleAgeDays { get; set; } = 30;

  public bool EnableThinMarketSaleReferenceFallback { get; set; } = true;

  public int ThinMarketMaxListings { get; set; } = 2;

  public int ThinMarketMinRecentSales { get; set; } = 3;

  public int ThinMarketMaxSaleAgeDays { get; set; } = 30;

  public float ThinMarketSaleReferenceTolerancePercent { get; set; } = 40.0f;

  [JsonProperty("EnableThinMarketAverageFallback")]
  public bool? LegacyEnableThinMarketAverageFallback { get; set; }

  [JsonProperty("ThinMarketAverageTolerancePercent")]
  public float? LegacyThinMarketAverageTolerancePercent { get; set; }

  public bool ShowPriceAdjustmentsMessages { get; set; } = true;

  /// <summary>
  /// Shows why a price was selected or skipped, including Universalis inputs.
  /// </summary>
  public bool ShowPricingDebug { get; set; } = false;

  public bool ShowRetainerNames { get; set; } = true;

  public bool TTSWhenAllDone { get; set; } = false;

  public string TTSWhenAllDoneMsg { get; set; } = "Finished auto pinching all retainers";

  public bool TTSWhenEachDone { get; set; } = false;

  public string TTSWhenEachDoneMsg { get; set; } = "Auto Pinch done";

  public int TTSVolume { get; set; } = 20;

  public bool DontUseTTS { get; set; } = false;

  public List<ulong> SeenRetainers { get; set; } = [];

  /// <summary>
  /// Set of retainer names that are enabled for auto pinch.
  /// If empty or null, all retainers are enabled by default.
  /// If contains ALL_DISABLED_SENTINEL, all retainers are disabled.
  /// </summary>
  public const string ALL_DISABLED_SENTINEL = "__ALL_DISABLED__";
  
  public HashSet<string> EnabledRetainerNames { get; set; } = [];

  /// <summary>
  /// List of retainer names that were last fetched from the game.
  /// Used to display retainer selection even when the retainer list is not open.
  /// </summary>
  public List<string> LastKnownRetainerNames { get; set; } = [];

  internal bool HasLegacyThinMarketSaleReferenceSettings =>
    LegacyEnableThinMarketAverageFallback.HasValue ||
    LegacyThinMarketAverageTolerancePercent.HasValue;

  internal void MigrateThinMarketSaleReferenceSettings()
  {
    if (LegacyEnableThinMarketAverageFallback.HasValue)
      EnableThinMarketSaleReferenceFallback = LegacyEnableThinMarketAverageFallback.Value;

    if (LegacyThinMarketAverageTolerancePercent.HasValue)
      ThinMarketSaleReferenceTolerancePercent = LegacyThinMarketAverageTolerancePercent.Value;

    LegacyEnableThinMarketAverageFallback = null;
    LegacyThinMarketAverageTolerancePercent = null;
  }

  /// <summary>
  /// Keeps legacy thin market fallback fields readable for migration without writing them back to disk.
  /// </summary>
  [System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1822:Mark members as static",
    Justification = "Json.NET discovers ShouldSerialize hooks as instance methods.")]
  public bool ShouldSerializeLegacyEnableThinMarketAverageFallback() => false;

  /// <summary>
  /// Keeps legacy thin market tolerance fields readable for migration without writing them back to disk.
  /// </summary>
  [System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1822:Mark members as static",
    Justification = "Json.NET discovers ShouldSerialize hooks as instance methods.")]
  public bool ShouldSerializeLegacyThinMarketAverageTolerancePercent() => false;

  public void Save()
  {
    Plugin.PluginInterface.SavePluginConfig(this);
  }
}
