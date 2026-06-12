using Dagobert;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using System.Net;
using System.Text;

namespace Dagobert.Tests;

internal static class Program
{
  private const string TestAllDisabledSentinel = "__ALL_DISABLED__";
  private const string TestSaleHistoryLabel = "View sale history.";
  private const string GuiPayloadGlyph = "\uE06F";

  public static async Task<int> Main()
  {
    var tests = new (string Name, Func<Task> Run)[]
    {
      ("No price reason explains bait guard skip", NoPriceReasonExplainsBaitGuardSkip),
      ("No price reason explains every thin market skip reason", NoPriceReasonExplainsEveryThinMarketSkipReason),
      ("Legacy thin market config migrates to sale reference names", LegacyThinMarketConfigMigratesToSaleReferenceNames),
      ("Missing legacy thin market config keeps sale reference defaults", MissingLegacyThinMarketConfigKeepsSaleReferenceDefaults),
      ("Open sale history config defaults to off", OpenSaleHistoryConfigDefaultsToOff),
      ("Open sale history config round-trips through serialization", OpenSaleHistoryConfigRoundTripsThroughSerialization),
      ("No price reason explains market board request failure", NoPriceReasonExplainsMarketBoardRequestFailure),
      ("No price reason explains missing eligible listings", NoPriceReasonExplainsMissingEligibleListings),
      ("No price reason explains duplicate response", NoPriceReasonExplainsDuplicateResponse),
      ("No price reason explains non skip pricing mappings", NoPriceReasonExplainsNonSkipPricingMappings),
      ("No price reason falls back without debug detail", NoPriceReasonFallsBackWithoutDebugDetail),
      ("Pricing debug includes context beyond no price reason", PricingDebugIncludesContextBeyondNoPriceReason),
      ("Pricing debug formats unknown listing count", PricingDebugFormatsUnknownListingCount),
      ("Pricing debug formats sale age from fixed clock", PricingDebugFormatsSaleAgeFromFixedClock),
      ("Pricing debug formats singular sale age units", PricingDebugFormatsSingularSaleAgeUnits),
      ("Bait guard keeps listing-only target without sale reference", BaitGuardKeepsListingOnlyTargetWithoutSaleReference),
      ("Bait guard skips tiny cluster below sale median floor", BaitGuardSkipsTinyClusterBelowSaleMedianFloor),
      ("Bait guard accepts below-floor cluster with enough listings", BaitGuardAcceptsBelowFloorClusterWithEnoughListings),
      ("Bait guard accepts below-floor cluster with enough quantity", BaitGuardAcceptsBelowFloorClusterWithEnoughQuantity),
      ("Bait guard applies gap promotion before sale reference", BaitGuardAppliesGapPromotionBeforeSaleReference),
      ("NQ sale reference uses lower-middle matching sale median", NqSaleReferenceUsesLowerMiddleMatchingSaleMedian),
      ("Sale reference median ignores one extreme high sale", SaleReferenceMedianIgnoresOneExtremeHighSale),
      ("Sale reference rejects too few matching sales", SaleReferenceRejectsTooFewMatchingSales),
      ("Sale reference rejects stale newest matching sale", SaleReferenceRejectsStaleNewestMatchingSale),
      ("Sale reference skips invalid sale rows", SaleReferenceSkipsInvalidSaleRows),
      ("Sale reference rejects fractional price rows", SaleReferenceRejectsFractionalPriceRows),
      ("Thin market holds own price outside sale reference tolerance", ThinMarketHoldsOwnPriceOutsideSaleReferenceTolerance),
      ("Thin market moves own price within sale reference tolerance", ThinMarketMovesOwnPriceWithinSaleReferenceTolerance),
      ("Thin market uses sale reference for empty board", ThinMarketUsesSaleReferenceForEmptyBoard),
      ("Thin market undercuts floor within sale reference tolerance", ThinMarketUndercutsFloorWithinSaleReferenceTolerance),
      ("Post pinch workflow starts with prepare before wait and set", PostPinchWorkflowStartsWithPrepareBeforeWaitAndSet),
      ("Post pinch workflow forces fresh compare on sell addon setup", PostPinchWorkflowForcesFreshCompareOnSellAddonSetup),
      ("Post pinch workflow ignores sell addon setup with busy task manager", PostPinchWorkflowIgnoresSellAddonSetupWithBusyTaskManager),
      ("Post pinch workflow ignores non button press events", PostPinchWorkflowIgnoresNonButtonPressEvents),
      ("Post pinch workflow ignores missing key press", PostPinchWorkflowIgnoresMissingKeyPress),
      ("Post pinch workflow ignores busy task manager", PostPinchWorkflowIgnoresBusyTaskManager),
      ("Post pinch workflow ignores disabled feature", PostPinchWorkflowIgnoresDisabledFeature),
      ("Post pinch workflow ignores unavailable sell addon", PostPinchWorkflowIgnoresUnavailableSellAddon),
      ("AutoRetainer suppression restores unsuppressed state", AutoRetainerSuppressionRestoresUnsuppressedState),
      ("AutoRetainer suppression restores already suppressed state", AutoRetainerSuppressionRestoresAlreadySuppressedState),
      ("AutoRetainer suppression skips inactive gateway", AutoRetainerSuppressionSkipsInactiveGateway),
      ("AutoRetainer suppression skips failed suppression write", AutoRetainerSuppressionSkipsFailedSuppressionWrite),
      ("AutoRetainer suppression cleanup is idempotent", AutoRetainerSuppressionCleanupIsIdempotent),
      ("AutoRetainer suppression retries failed restore", AutoRetainerSuppressionRetriesFailedRestore),
      ("AutoRetainer suppression idle cleanup waits for idle", AutoRetainerSuppressionIdleCleanupWaitsForIdle),
      ("AutoPinch timeout policy uses general timeout", AutoPinchTimeoutPolicyUsesGeneralTimeout),
      ("AutoPinch timeout policy uses market price timeout", AutoPinchTimeoutPolicyUsesMarketPriceTimeout),
      ("AutoPinch timeout policy pads legacy backstop", AutoPinchTimeoutPolicyPadsLegacyBackstop),
      ("AutoPinch delay task waits until delay elapses", AutoPinchDelayTaskWaitsUntilDelayElapses),
      ("AutoPinch delay task treats negative delay as elapsed", AutoPinchDelayTaskTreatsNegativeDelayAsElapsed),
      ("AutoPinch task guard reports timeout", AutoPinchTaskGuardReportsTimeout),
      ("AutoPinch task guard resets timing after timeout", AutoPinchTaskGuardResetsTimingAfterTimeout),
      ("AutoPinch task guard leaves active session after successful task", AutoPinchTaskGuardLeavesActiveSessionAfterSuccessfulTask),
      ("AutoPinch task guard restores suppression on task exception", AutoPinchTaskGuardRestoresSuppressionOnTaskException),
      ("AutoPinch task guard preserves original exception when abort fails", AutoPinchTaskGuardPreservesOriginalExceptionWhenAbortFails),
      ("AutoPinch task guard continues cleanup after failed suppression restore", AutoPinchTaskGuardContinuesCleanupAfterFailedSuppressionRestore),
      ("AutoPinch task guard ignores inactive suppression end", AutoPinchTaskGuardIgnoresInactiveSuppressionEnd),
      ("AutoPinch task guard records listener cleanup and log failures", AutoPinchTaskGuardRecordsListenerCleanupAndLogFailures),
      ("AutoPinch run planner selects all retainers when none configured", AutoPinchRunPlannerSelectsAllRetainersWhenNoneConfigured),
      ("AutoPinch run planner skips all retainers with sentinel", AutoPinchRunPlannerSkipsAllRetainersWithSentinel),
      ("AutoPinch run planner selects configured retainers in visible order", AutoPinchRunPlannerSelectsConfiguredRetainersInVisibleOrder),
      ("AutoPinch run planner returns no retainers when configured names are missing", AutoPinchRunPlannerReturnsNoRetainersWhenConfiguredNamesAreMissing),
      ("AutoPinch run planner detects sell list work", AutoPinchRunPlannerDetectsSellListWork),
      ("AutoPinch run planner treats empty sell list differently by entry point", AutoPinchRunPlannerTreatsEmptySellListDifferentlyByEntryPoint),
      ("Sale history plan is empty when toggle is off", SaleHistoryPlanIsEmptyWhenToggleIsOff),
      ("Sale history plan is empty without resolved label", SaleHistoryPlanIsEmptyWithoutResolvedLabel),
      ("Sale history plan lists visit steps in order", SaleHistoryPlanListsVisitStepsInOrder),
      ("Menu entry matcher finds exact entry", MenuEntryMatcherFindsExactEntry),
      ("Menu entry matcher returns null for missing entry", MenuEntryMatcherReturnsNullForMissingEntry),
      ("Menu entry matcher survives extra leading entries", MenuEntryMatcherSurvivesExtraLeadingEntries),
      ("Menu entry matcher falls back to trimmed comparison", MenuEntryMatcherFallsBackToTrimmedComparison),
      ("Menu entry matcher falls back to suffix for payload prefixes", MenuEntryMatcherFallsBackToSuffixForPayloadPrefixes),
      ("Menu entry matcher prefers exact over suffix match", MenuEntryMatcherPrefersExactOverSuffixMatch),
      ("Menu entry matcher tries candidate labels in order", MenuEntryMatcherTriesCandidateLabelsInOrder),
      ("Sale history step status skips before completion check", SaleHistoryStepStatusSkipsBeforeCompletionCheck),
      ("Sale history step status completes finished step", SaleHistoryStepStatusCompletesFinishedStep),
      ("Sale history step status waits before deadline", SaleHistoryStepStatusWaitsBeforeDeadline),
      ("Sale history step status expires at deadline", SaleHistoryStepStatusExpiresAtDeadline),
      ("Sale history step deadline stays below guard timeout", SaleHistoryStepDeadlineStaysBelowGuardTimeout),
      ("AutoRetainer IPC state skips missing plugin read", AutoRetainerIpcStateSkipsMissingPluginRead),
      ("AutoRetainer IPC state skips missing plugin write", AutoRetainerIpcStateSkipsMissingPluginWrite),
      ("AutoRetainer IPC state reports read failure", AutoRetainerIpcStateReportsReadFailure),
      ("AutoRetainer IPC state reports write failure", AutoRetainerIpcStateReportsWriteFailure),
      ("AutoRetainer IPC state reports successful calls", AutoRetainerIpcStateReportsSuccessfulCalls),
      ("AutoPinch cleanup plan cancels active run", AutoPinchCleanupPlanCancelsActiveRun),
      ("AutoPinch cleanup plan handles draw catch", AutoPinchCleanupPlanHandlesDrawCatch),
      ("AutoPinch cleanup plan disposes active run", AutoPinchCleanupPlanDisposesActiveRun),
      ("AutoPinch cleanup plan waits for idle", AutoPinchCleanupPlanWaitsForIdle),
      ("Price request state remains active until current request finishes", PriceRequestStateRemainsActiveUntilCurrentRequestFinishes),
      ("Price request state ignores stale request finish", PriceRequestStateIgnoresStaleRequestFinish)
    };

    var failures = 0;
    foreach (var test in tests)
    {
      try
      {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
      }
      catch (Exception ex)
      {
        failures++;
        Console.WriteLine($"FAIL {test.Name}: {ex.Message}");
      }
    }

    return failures == 0 ? 0 : 1;
  }

  private static Task LegacyThinMarketConfigMigratesToSaleReferenceNames()
  {
    const string oldEnableField = "EnableThinMarketAverageFallback";
    const string oldToleranceField = "ThinMarketAverageTolerancePercent";
    const string newEnableField = "EnableThinMarketSaleReferenceFallback";
    const string newToleranceField = "ThinMarketSaleReferenceTolerancePercent";
    var config = JsonConvert.DeserializeObject<Configuration>(
      $$"""
      {
        "Version": 1,
        "{{oldEnableField}}": false,
        "{{oldToleranceField}}": 12.5
      }
      """) ?? throw new InvalidOperationException("expected legacy config to deserialize");

    AssertEqual(true, config.EnableThinMarketSaleReferenceFallback, "sale reference fallback default before migration");
    AssertEqual(40.0f, config.ThinMarketSaleReferenceTolerancePercent, "sale reference tolerance default before migration");

    config.MigrateThinMarketSaleReferenceSettings();

    AssertEqual(false, config.EnableThinMarketSaleReferenceFallback, "migrated sale reference fallback enabled");
    AssertEqual(12.5f, config.ThinMarketSaleReferenceTolerancePercent, "migrated sale reference tolerance");

    var serialized = JsonConvert.SerializeObject(config);
    AssertEqual(false, serialized.Contains(oldEnableField, StringComparison.Ordinal), "serialized legacy enable field absent");
    AssertEqual(false, serialized.Contains(oldToleranceField, StringComparison.Ordinal), "serialized legacy tolerance field absent");
    AssertEqual(true, serialized.Contains(newEnableField, StringComparison.Ordinal), "serialized sale reference enable field present");
    AssertEqual(true, serialized.Contains(newToleranceField, StringComparison.Ordinal), "serialized sale reference tolerance field present");
    return Task.CompletedTask;
  }

  private static Task MissingLegacyThinMarketConfigKeepsSaleReferenceDefaults()
  {
    var config = JsonConvert.DeserializeObject<Configuration>(
      """
      {
        "Version": 1
      }
      """) ?? throw new InvalidOperationException("expected config to deserialize");

    config.MigrateThinMarketSaleReferenceSettings();

    AssertEqual(true, config.EnableThinMarketSaleReferenceFallback, "missing legacy fallback keeps default");
    AssertEqual(40.0f, config.ThinMarketSaleReferenceTolerancePercent, "missing legacy tolerance keeps default");
    AssertEqual(false, config.HasLegacyThinMarketSaleReferenceSettings, "missing legacy config leaves no legacy values");
    return Task.CompletedTask;
  }

  private static Task OpenSaleHistoryConfigDefaultsToOff()
  {
    var config = JsonConvert.DeserializeObject<Configuration>(
      """
      {
        "Version": 2
      }
      """) ?? throw new InvalidOperationException("expected config to deserialize");

    AssertEqual(false, config.OpenSaleHistoryDuringAutoPinch, "open sale history default");
    return Task.CompletedTask;
  }

  private static Task OpenSaleHistoryConfigRoundTripsThroughSerialization()
  {
    var config = new Configuration { OpenSaleHistoryDuringAutoPinch = true };

    var roundTripped = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(config))
      ?? throw new InvalidOperationException("expected config to round-trip");

    AssertEqual(true, roundTripped.OpenSaleHistoryDuringAutoPinch, "open sale history round trip");
    return Task.CompletedTask;
  }

  private static Task NoPriceReasonExplainsBaitGuardSkip()
  {
    var reason = PricingMessageFormatter.FormatNoPriceReason(
      new PricingDebugDetail(PricingDebugReason.NoCredibleListing)
      {
        ListingCount = 2
      },
      FixedPricingNow());

    AssertEqual("bait guard found no credible listing", reason, "bait guard no price reason");
    return Task.CompletedTask;
  }

  private static Task NoPriceReasonExplainsEveryThinMarketSkipReason()
  {
    var now = FixedPricingNow();
    var cases = new (ThinMarketPricingReason ThinMarketReason, PricingDebugDetail Detail, string Expected)[]
    {
      (
        ThinMarketPricingReason.FallbackDisabled,
        new PricingDebugDetail(PricingDebugReason.ThinMarketSkip)
        {
          ThinMarketReason = ThinMarketPricingReason.FallbackDisabled
        },
        "thin market skipped because sale reference fallback is disabled"),
      (
        ThinMarketPricingReason.TooManyListings,
        new PricingDebugDetail(PricingDebugReason.ThinMarketSkip)
        {
          ThinMarketReason = ThinMarketPricingReason.TooManyListings,
          ListingCount = 4
        },
        "thin market skipped because listing count is above the thin market limit"),
      (
        ThinMarketPricingReason.SaleReferenceMissingOrZero,
        new PricingDebugDetail(PricingDebugReason.ThinMarketSkip)
        {
          ThinMarketReason = ThinMarketPricingReason.SaleReferenceMissingOrZero
        },
        "thin market skipped because Universalis returned no usable sale reference"),
      (
        ThinMarketPricingReason.NotEnoughRecentSales,
        new PricingDebugDetail(PricingDebugReason.ThinMarketSkip)
        {
          ThinMarketReason = ThinMarketPricingReason.NotEnoughRecentSales,
          SaleReference = new SaleReference(5000, 1, now.AddHours(-2)),
          MinRecentSales = 3
        },
        "thin market skipped because Universalis returned 1 recent sale, minimum is 3"),
      (
        ThinMarketPricingReason.LatestSaleTooOld,
        new PricingDebugDetail(PricingDebugReason.ThinMarketSkip)
        {
          ThinMarketReason = ThinMarketPricingReason.LatestSaleTooOld,
          SaleReference = new SaleReference(5000, 3, now.AddDays(-31)),
          MaxSaleAgeDays = 30
        },
        "thin market skipped because newest Universalis sale is 31 days ago and max age is 30 days"),
      (
        ThinMarketPricingReason.EmptyBoardUseReference,
        new PricingDebugDetail(PricingDebugReason.ThinMarketSkip)
        {
          ThinMarketReason = ThinMarketPricingReason.EmptyBoardUseReference
        },
        "thin market skipped because thin market selected a sale reference for an empty board, but the result was not available to set"),
      (
        ThinMarketPricingReason.OwnPriceOutsideTolerance,
        new PricingDebugDetail(PricingDebugReason.ThinMarketSkip)
        {
          ThinMarketReason = ThinMarketPricingReason.OwnPriceOutsideTolerance,
          OwnLowestPrice = 19980,
          SaleReference = new SaleReference(23000, 3, now.AddHours(-2)),
          TolerancePercent = 10.0f
        },
        $"thin market skipped because own price {FormatExpectedGil(19980)} gil is outside 10% tolerance of Universalis sale reference {FormatExpectedGil(23000)} gil"),
      (
        ThinMarketPricingReason.FloorOutsideTolerance,
        new PricingDebugDetail(PricingDebugReason.ThinMarketSkip)
        {
          ThinMarketReason = ThinMarketPricingReason.FloorOutsideTolerance,
          FloorPrice = 10000,
          SaleReference = new SaleReference(5000, 3, now.AddHours(-2)),
          TolerancePercent = 40.0f
        },
        $"thin market skipped because floor {FormatExpectedGil(10000)} gil is outside 40% tolerance of Universalis sale reference {FormatExpectedGil(5000)} gil"),
      (
        ThinMarketPricingReason.OwnPriceWithinTolerance,
        new PricingDebugDetail(PricingDebugReason.ThinMarketSkip)
        {
          ThinMarketReason = ThinMarketPricingReason.OwnPriceWithinTolerance
        },
        "thin market skipped because thin market found an own price near the sale reference, but the result was not available to set"),
      (
        ThinMarketPricingReason.FloorWithinTolerance,
        new PricingDebugDetail(PricingDebugReason.ThinMarketSkip)
        {
          ThinMarketReason = ThinMarketPricingReason.FloorWithinTolerance
        },
        "thin market skipped because thin market found a valid floor, but the result was not available to set")
    };

    foreach (var testCase in cases)
    {
      var reason = PricingMessageFormatter.FormatNoPriceReason(testCase.Detail, now);

      AssertEqual(
        testCase.Expected,
        reason,
        $"thin market no price reason {testCase.ThinMarketReason}");
    }

    return Task.CompletedTask;
  }

  private static Task NoPriceReasonExplainsNonSkipPricingMappings()
  {
    var cases = new (PricingDebugReason DebugReason, PricingDebugDetail Detail, string Expected)[]
    {
      (
        PricingDebugReason.OwnPriceAlreadyLowest,
        new PricingDebugDetail(PricingDebugReason.OwnPriceAlreadyLowest),
        "your listing is already at or below the credible competitor"),
      (
        PricingDebugReason.UndercutCompetitor,
        new PricingDebugDetail(PricingDebugReason.UndercutCompetitor),
        "a price was calculated but was not available to set"),
      (
        PricingDebugReason.ThinMarketUseReference,
        new PricingDebugDetail(PricingDebugReason.ThinMarketUseReference),
        "thin market selected a Universalis sale reference, but the result was not available to set"),
      (
        PricingDebugReason.ThinMarketUndercutFloor,
        new PricingDebugDetail(PricingDebugReason.ThinMarketUndercutFloor),
        "thin market selected an undercut floor, but the result was not available to set"),
      (
        PricingDebugReason.CachedPrice,
        new PricingDebugDetail(PricingDebugReason.CachedPrice),
        "a cached price was selected, but the result was not available to set")
    };

    foreach (var testCase in cases)
    {
      var reason = PricingMessageFormatter.FormatNoPriceReason(testCase.Detail, FixedPricingNow());

      AssertEqual(testCase.Expected, reason, $"no price reason {testCase.DebugReason}");
    }

    return Task.CompletedTask;
  }

  private static Task NoPriceReasonExplainsMarketBoardRequestFailure()
  {
    var reason = PricingMessageFormatter.FormatNoPriceReason(
      new PricingDebugDetail(PricingDebugReason.MarketBoardRequestFailed)
      {
        RequestStatus = "Failed"
      },
      FixedPricingNow());

    AssertEqual("the market board request failed with status Failed", reason, "request failure reason");
    return Task.CompletedTask;
  }

  private static Task NoPriceReasonExplainsMissingEligibleListings()
  {
    var reason = PricingMessageFormatter.FormatNoPriceReason(
      new PricingDebugDetail(PricingDebugReason.NoEligibleListings)
      {
        ListingCount = 0
      },
      FixedPricingNow());

    AssertEqual("no eligible market board listings were found", reason, "missing eligible listings reason");
    return Task.CompletedTask;
  }

  private static Task NoPriceReasonExplainsDuplicateResponse()
  {
    var reason = PricingMessageFormatter.FormatNoPriceReason(
      new PricingDebugDetail(PricingDebugReason.DuplicateMarketBoardRequest),
      FixedPricingNow());

    AssertEqual("a duplicate market board response was ignored", reason, "duplicate response reason");
    return Task.CompletedTask;
  }

  private static Task NoPriceReasonFallsBackWithoutDebugDetail()
  {
    var reason = PricingMessageFormatter.FormatNoPriceReason(null, FixedPricingNow());

    AssertEqual("pricing did not return a usable price", reason, "missing debug detail reason");
    return Task.CompletedTask;
  }

  private static Task PricingDebugIncludesContextBeyondNoPriceReason()
  {
    var detail = new PricingDebugDetail(PricingDebugReason.ThinMarketSkip)
    {
      ThinMarketReason = ThinMarketPricingReason.NotEnoughRecentSales,
      ListingCount = 2,
      FloorPrice = 10000,
      OwnLowestPrice = 12000,
      SaleReference = new SaleReference(5000, 1, FixedPricingNow().AddHours(-2)),
      MinRecentSales = 3,
      MaxSaleAgeDays = 30,
      TolerancePercent = 40.0f
    };

    var reason = PricingMessageFormatter.FormatNoPriceReason(detail, FixedPricingNow());
    var debug = PricingMessageFormatter.FormatPricingDebug(detail, FixedPricingNow());

    AssertNotEqual(reason, debug, "debug text differs from normal reason");
    AssertContains("listings 2", debug, "debug listing count");
    AssertContains($"floor {FormatExpectedGil(10000)} gil", debug, "debug floor price");
    AssertContains($"own lowest {FormatExpectedGil(12000)} gil", debug, "debug own price");
    AssertContains("newest sale 2 hours ago", debug, "debug sale age");
    return Task.CompletedTask;
  }

  private static Task PricingDebugFormatsSaleAgeFromFixedClock()
  {
    var detail = new PricingDebugDetail(PricingDebugReason.ThinMarketSkip)
    {
      ThinMarketReason = ThinMarketPricingReason.LatestSaleTooOld,
      SaleReference = new SaleReference(5000, 3, FixedPricingNow().AddDays(-31)),
      MaxSaleAgeDays = 30
    };

    var debug = PricingMessageFormatter.FormatPricingDebug(detail, FixedPricingNow());

    AssertContains("newest Universalis sale is 31 days ago", debug, "debug stale sale reason");
    AssertContains("newest sale 31 days ago", debug, "debug stale sale context");
    return Task.CompletedTask;
  }

  private static Task PricingDebugFormatsUnknownListingCount()
  {
    var debug = PricingMessageFormatter.FormatPricingDebug(
      new PricingDebugDetail(PricingDebugReason.NoEligibleListings),
      FixedPricingNow());

    AssertContains("listings unknown", debug, "debug unknown listing count");
    return Task.CompletedTask;
  }

  private static Task PricingDebugFormatsSingularSaleAgeUnits()
  {
    var cases = new (DateTimeOffset LatestSaleAt, string Expected)[]
    {
      (FixedPricingNow().AddDays(-1), "1 day ago"),
      (FixedPricingNow().AddHours(-1), "1 hour ago"),
      (FixedPricingNow().AddMinutes(-1), "1 minute ago")
    };

    foreach (var testCase in cases)
    {
      var detail = new PricingDebugDetail(PricingDebugReason.ThinMarketSkip)
      {
        ThinMarketReason = ThinMarketPricingReason.LatestSaleTooOld,
        SaleReference = new SaleReference(5000, 3, testCase.LatestSaleAt),
        MaxSaleAgeDays = 30
      };

      var debug = PricingMessageFormatter.FormatPricingDebug(detail, FixedPricingNow());

      AssertContains(testCase.Expected, debug, $"debug sale age {testCase.Expected}");
    }

    return Task.CompletedTask;
  }

  private static Task BaitGuardKeepsListingOnlyTargetWithoutSaleReference()
  {
    var listings = CreateListings((100u, 1u), (200u, 1u), (300u, 1u));
    var target = BaitGuard.SelectTargetIndex(listings, [0, 1, 2], DefaultBaitOptions(), null);

    AssertEqual<int?>(0, target, "listing-only target");
    return Task.CompletedTask;
  }

  private static Task BaitGuardSkipsTinyClusterBelowSaleMedianFloor()
  {
    var listings = CreateListings((100u, 1u), (105u, 1u), (300u, 1u));
    var saleReference = new SaleReference(300, 3, DateTimeOffset.UtcNow);
    var target = BaitGuard.SelectTargetIndex(listings, [0, 1, 2], DefaultBaitOptions(), saleReference);

    AssertEqual<int?>(2, target, "tiny low cluster target");
    return Task.CompletedTask;
  }

  private static Task BaitGuardAcceptsBelowFloorClusterWithEnoughListings()
  {
    var listings = CreateListings((100u, 1u), (103u, 1u), (105u, 1u), (300u, 1u));
    var saleReference = new SaleReference(300, 3, DateTimeOffset.UtcNow);
    var target = BaitGuard.SelectTargetIndex(listings, [0, 1, 2, 3], DefaultBaitOptions(), saleReference);

    AssertEqual<int?>(0, target, "listing-backed low cluster target");
    return Task.CompletedTask;
  }

  private static Task BaitGuardAcceptsBelowFloorClusterWithEnoughQuantity()
  {
    var listings = CreateListings((100u, 9u), (104u, 12u), (300u, 1u));
    var saleReference = new SaleReference(300, 3, DateTimeOffset.UtcNow);
    var target = BaitGuard.SelectTargetIndex(listings, [0, 1, 2], DefaultBaitOptions(), saleReference);

    AssertEqual<int?>(0, target, "quantity-backed low cluster target");
    return Task.CompletedTask;
  }

  private static Task BaitGuardAppliesGapPromotionBeforeSaleReference()
  {
    var listings = CreateListings((100u, 100u), (300u, 1u), (305u, 1u));
    var saleReference = new SaleReference(600, 3, DateTimeOffset.UtcNow);
    var target = BaitGuard.SelectTargetIndex(listings, [0, 1, 2], DefaultBaitOptions(), saleReference);

    AssertEqual<int?>(1, target, "gap-promoted target");
    return Task.CompletedTask;
  }

  private static async Task NqSaleReferenceUsesLowerMiddleMatchingSaleMedian()
  {
    var now = DateTimeOffset.FromUnixTimeSeconds(1778600000);
    var provider = CreateProvider(
      $$"""
      {
        "recentHistory": [
          { "hq": false, "pricePerUnit": 400, "timestamp": {{now.ToUnixTimeSeconds()}} },
          { "hq": false, "pricePerUnit": 100, "timestamp": {{now.AddDays(-1).ToUnixTimeSeconds()}} },
          { "hq": false, "pricePerUnit": 300, "timestamp": {{now.AddDays(-2).ToUnixTimeSeconds()}} },
          { "hq": false, "pricePerUnit": 200, "timestamp": {{now.AddDays(-3).ToUnixTimeSeconds()}} },
          { "hq": true, "pricePerUnit": 1, "timestamp": {{now.ToUnixTimeSeconds()}} }
        ]
      }
      """);

    var saleReference = await provider.GetSaleReferenceAsync(34, 3920, false, 3, 30, now, CancellationToken.None);

    var reference = saleReference ?? throw new InvalidOperationException("expected an NQ sale reference");
    AssertEqual((uint)200, reference.MedianUnitPrice, "NQ sale median");
    AssertEqual(4, reference.RecentHistoryCount, "NQ sale count");
    AssertEqual(now, reference.LatestSaleAt, "NQ latest sale");
  }

  private static async Task SaleReferenceRejectsTooFewMatchingSales()
  {
    var now = DateTimeOffset.FromUnixTimeSeconds(1778600000);
    var provider = CreateProvider(
      $$"""
      {
        "recentHistory": [
          { "hq": false, "pricePerUnit": 100, "timestamp": {{now.ToUnixTimeSeconds()}} },
          { "hq": true, "pricePerUnit": 100, "timestamp": {{now.ToUnixTimeSeconds()}} }
        ]
      }
      """);

    var saleReference = await provider.GetSaleReferenceAsync(34, 3920, false, 2, 30, now, CancellationToken.None);

    AssertEqual<SaleReference?>(null, saleReference, "too few sale reference");
  }

  private static async Task SaleReferenceMedianIgnoresOneExtremeHighSale()
  {
    var now = DateTimeOffset.FromUnixTimeSeconds(1778600000);
    var provider = CreateProvider(
      $$"""
      {
        "recentHistory": [
          { "hq": false, "pricePerUnit": 20000, "timestamp": {{now.ToUnixTimeSeconds()}} },
          { "hq": false, "pricePerUnit": 19980, "timestamp": {{now.AddHours(-1).ToUnixTimeSeconds()}} },
          { "hq": false, "pricePerUnit": 20050, "timestamp": {{now.AddHours(-2).ToUnixTimeSeconds()}} },
          { "hq": false, "pricePerUnit": 20000000, "timestamp": {{now.AddHours(-3).ToUnixTimeSeconds()}} }
        ]
      }
      """);

    var saleReference = await provider.GetSaleReferenceAsync(34, 3920, false, 4, 30, now, CancellationToken.None);

    var reference = saleReference ?? throw new InvalidOperationException("expected a sale reference with an outlier");
    AssertEqual((uint)20000, reference.MedianUnitPrice, "sale median with high outlier");
    AssertEqual(4, reference.RecentHistoryCount, "sale count with high outlier");
    AssertEqual(now, reference.LatestSaleAt, "latest sale with high outlier");
  }

  private static async Task SaleReferenceRejectsStaleNewestMatchingSale()
  {
    var now = DateTimeOffset.FromUnixTimeSeconds(1778600000);
    var provider = CreateProvider(
      $$"""
      {
        "recentHistory": [
          { "hq": false, "pricePerUnit": 100, "timestamp": {{now.AddDays(-31).ToUnixTimeSeconds()}} },
          { "hq": false, "pricePerUnit": 200, "timestamp": {{now.AddDays(-32).ToUnixTimeSeconds()}} },
          { "hq": false, "pricePerUnit": 300, "timestamp": {{now.AddDays(-33).ToUnixTimeSeconds()}} }
        ]
      }
      """);

    var saleReference = await provider.GetSaleReferenceAsync(34, 3920, false, 3, 30, now, CancellationToken.None);

    AssertEqual<SaleReference?>(null, saleReference, "stale sale reference");
  }

  private static async Task SaleReferenceSkipsInvalidSaleRows()
  {
    var now = DateTimeOffset.FromUnixTimeSeconds(1778600000);
    var provider = CreateProvider(
      $$"""
      {
        "recentHistory": [
          7,
          { "hq": false, "pricePerUnit": 999, "timestamp": 999999999999999999 },
          { "hq": false, "pricePerUnit": 0, "timestamp": {{now.ToUnixTimeSeconds()}} },
          { "pricePerUnit": 999, "timestamp": {{now.ToUnixTimeSeconds()}} },
          { "hq": false, "pricePerUnit": 400, "timestamp": {{now.ToUnixTimeSeconds()}} },
          { "hq": false, "pricePerUnit": 100, "timestamp": {{now.AddDays(-1).ToUnixTimeSeconds()}} },
          { "hq": false, "pricePerUnit": 200, "timestamp": {{now.AddDays(-2).ToUnixTimeSeconds()}} }
        ]
      }
      """);

    var saleReference = await provider.GetSaleReferenceAsync(34, 3920, false, 3, 30, now, CancellationToken.None);

    var reference = saleReference ?? throw new InvalidOperationException("expected a sale reference");
    AssertEqual((uint)200, reference.MedianUnitPrice, "sale median with invalid rows");
    AssertEqual(3, reference.RecentHistoryCount, "valid sale count");
    AssertEqual(now, reference.LatestSaleAt, "latest valid sale");
  }

  private static async Task SaleReferenceRejectsFractionalPriceRows()
  {
    var now = DateTimeOffset.FromUnixTimeSeconds(1778600000);
    var provider = CreateProvider(
      $$"""
      {
        "recentHistory": [
          { "hq": false, "pricePerUnit": 0.5, "timestamp": {{now.ToUnixTimeSeconds()}} },
          { "hq": false, "pricePerUnit": 100, "timestamp": {{now.AddDays(-1).ToUnixTimeSeconds()}} },
          { "hq": false, "pricePerUnit": 200, "timestamp": {{now.AddDays(-2).ToUnixTimeSeconds()}} }
        ]
      }
      """);

    var saleReference = await provider.GetSaleReferenceAsync(34, 3920, false, 3, 30, now, CancellationToken.None);

    AssertEqual<SaleReference?>(null, saleReference, "fractional price row rejected");
  }

  private static Task ThinMarketHoldsOwnPriceOutsideSaleReferenceTolerance()
  {
    var now = FixedPricingNow();
    var decision = ThinMarketPricePolicy.Decide(
      0,
      null,
      19980,
      new SaleReference(20000000, 3, now),
      DefaultThinMarketOptions(),
      now);

    AssertEqual(ThinMarketPricingAction.Skip, decision.Action, "own outlier action");
    AssertEqual(ThinMarketPricingReason.OwnPriceOutsideTolerance, decision.Reason, "own outlier reason");
    AssertEqual((uint)0, decision.ReferencePrice, "own outlier reference price");
    return Task.CompletedTask;
  }

  private static Task ThinMarketMovesOwnPriceWithinSaleReferenceTolerance()
  {
    var now = FixedPricingNow();
    var decision = ThinMarketPricePolicy.Decide(
      0,
      null,
      19980,
      new SaleReference(23000, 3, now),
      DefaultThinMarketOptions(),
      now);

    AssertEqual(ThinMarketPricingAction.UseReference, decision.Action, "own nearby action");
    AssertEqual(ThinMarketPricingReason.OwnPriceWithinTolerance, decision.Reason, "own nearby reason");
    AssertEqual((uint)23000, decision.ReferencePrice, "own nearby reference price");
    return Task.CompletedTask;
  }

  private static Task ThinMarketUsesSaleReferenceForEmptyBoard()
  {
    var now = FixedPricingNow();
    var decision = ThinMarketPricePolicy.Decide(
      0,
      null,
      null,
      new SaleReference(23000, 3, now),
      DefaultThinMarketOptions(),
      now);

    AssertEqual(ThinMarketPricingAction.UseReference, decision.Action, "empty board action");
    AssertEqual(ThinMarketPricingReason.EmptyBoardUseReference, decision.Reason, "empty board reason");
    AssertEqual((uint)23000, decision.ReferencePrice, "empty board reference price");
    return Task.CompletedTask;
  }

  private static Task ThinMarketUndercutsFloorWithinSaleReferenceTolerance()
  {
    var now = FixedPricingNow();
    var decision = ThinMarketPricePolicy.Decide(
      1,
      21000,
      25000,
      new SaleReference(23000, 3, now),
      DefaultThinMarketOptions(),
      now);

    AssertEqual(ThinMarketPricingAction.UndercutFloor, decision.Action, "floor nearby action");
    AssertEqual(ThinMarketPricingReason.FloorWithinTolerance, decision.Reason, "floor nearby reason");
    AssertEqual((uint)21000, decision.ReferencePrice, "floor nearby reference price");
    return Task.CompletedTask;
  }

  private static Task PostPinchWorkflowStartsWithPrepareBeforeWaitAndSet()
  {
    var actions = PostPinchWorkflow.PlanActions(
      isCompareButtonPress: true,
      isFeatureEnabled: true,
      isPostPinchKeyHeld: true,
      isTaskManagerBusy: false,
      isSellAddonReady: true);

    AssertSequenceEqual(
      [
        PostPinchWorkflowAction.PreparePriceRequest,
        PostPinchWorkflowAction.WaitForMarketPrice,
        PostPinchWorkflowAction.SetNewPrice
      ],
      actions,
      "post pinch start actions");
    return Task.CompletedTask;
  }

  private static Task PostPinchWorkflowForcesFreshCompareOnSellAddonSetup()
  {
    var actions = PostPinchWorkflow.PlanSellAddonSetupActions(
      isFeatureEnabled: true,
      isPostPinchKeyHeld: true,
      isTaskManagerBusy: false,
      isSellAddonReady: true);

    AssertSequenceEqual(
      [
        PostPinchWorkflowAction.ForceComparePrice,
        PostPinchWorkflowAction.DelayForMarketBoard,
        PostPinchWorkflowAction.WaitForMarketPrice,
        PostPinchWorkflowAction.SetNewPrice
      ],
      actions,
      "post pinch sell addon setup actions");
    return Task.CompletedTask;
  }

  private static Task PostPinchWorkflowIgnoresSellAddonSetupWithBusyTaskManager()
  {
    var actions = PostPinchWorkflow.PlanSellAddonSetupActions(
      isFeatureEnabled: true,
      isPostPinchKeyHeld: true,
      isTaskManagerBusy: true,
      isSellAddonReady: true);

    AssertSequenceEqual([], actions, "post pinch sell addon setup busy actions");
    return Task.CompletedTask;
  }

  private static Task PostPinchWorkflowIgnoresNonButtonPressEvents()
  {
    var actions = PostPinchWorkflow.PlanActions(
      isCompareButtonPress: false,
      isFeatureEnabled: true,
      isPostPinchKeyHeld: true,
      isTaskManagerBusy: false,
      isSellAddonReady: true);

    AssertSequenceEqual([], actions, "post pinch actions for non button press");
    return Task.CompletedTask;
  }

  private static Task PostPinchWorkflowIgnoresMissingKeyPress()
  {
    var actions = PostPinchWorkflow.PlanActions(
      isCompareButtonPress: true,
      isFeatureEnabled: true,
      isPostPinchKeyHeld: false,
      isTaskManagerBusy: false,
      isSellAddonReady: true);

    AssertSequenceEqual([], actions, "post pinch actions without key press");
    return Task.CompletedTask;
  }

  private static Task PostPinchWorkflowIgnoresBusyTaskManager()
  {
    var actions = PostPinchWorkflow.PlanActions(
      isCompareButtonPress: true,
      isFeatureEnabled: true,
      isPostPinchKeyHeld: true,
      isTaskManagerBusy: true,
      isSellAddonReady: true);

    AssertSequenceEqual([], actions, "post pinch actions with busy task manager");
    return Task.CompletedTask;
  }

  private static Task PostPinchWorkflowIgnoresDisabledFeature()
  {
    var actions = PostPinchWorkflow.PlanActions(
      isCompareButtonPress: true,
      isFeatureEnabled: false,
      isPostPinchKeyHeld: true,
      isTaskManagerBusy: false,
      isSellAddonReady: true);

    AssertSequenceEqual([], actions, "post pinch actions with disabled feature");
    return Task.CompletedTask;
  }

  private static Task PostPinchWorkflowIgnoresUnavailableSellAddon()
  {
    var actions = PostPinchWorkflow.PlanActions(
      isCompareButtonPress: true,
      isFeatureEnabled: true,
      isPostPinchKeyHeld: true,
      isTaskManagerBusy: false,
      isSellAddonReady: false);

    AssertSequenceEqual([], actions, "post pinch actions with unavailable sell addon");
    return Task.CompletedTask;
  }

  private static Task AutoRetainerSuppressionRestoresUnsuppressedState()
  {
    var gateway = new FakeAutoRetainerSuppressionGateway(initialSuppressed: false);
    var coordinator = new AutoRetainerSuppressionCoordinator(gateway);

    coordinator.BeginRun();

    AssertEqual(true, coordinator.HasActiveSession, "active session after begin");
    AssertSequenceEqual([true], gateway.SetValues, "set values after begin");

    coordinator.EndRun();

    AssertEqual(false, coordinator.HasActiveSession, "active session after end");
    AssertSequenceEqual([true, false], gateway.SetValues, "set values after restore");
    return Task.CompletedTask;
  }

  private static Task AutoRetainerSuppressionRestoresAlreadySuppressedState()
  {
    var gateway = new FakeAutoRetainerSuppressionGateway(initialSuppressed: true);
    var coordinator = new AutoRetainerSuppressionCoordinator(gateway);

    coordinator.BeginRun();
    coordinator.EndRun();

    AssertSequenceEqual([true, true], gateway.SetValues, "set values after restore");
    return Task.CompletedTask;
  }

  private static Task AutoRetainerSuppressionSkipsInactiveGateway()
  {
    var gateway = new FakeAutoRetainerSuppressionGateway(initialSuppressed: false)
    {
      CanGet = false
    };
    var coordinator = new AutoRetainerSuppressionCoordinator(gateway);

    coordinator.BeginRun();
    coordinator.EndRun();

    AssertEqual(false, coordinator.HasActiveSession, "active session after failed begin");
    AssertSequenceEqual([], gateway.SetValues, "set values when gateway cannot read");
    return Task.CompletedTask;
  }

  private static Task AutoRetainerSuppressionSkipsFailedSuppressionWrite()
  {
    var gateway = new FakeAutoRetainerSuppressionGateway(initialSuppressed: false);
    gateway.SetResults.Enqueue(false);
    var coordinator = new AutoRetainerSuppressionCoordinator(gateway);

    coordinator.BeginRun();

    AssertEqual(false, coordinator.HasActiveSession, "active session after failed suppression write");
    AssertSequenceEqual([], gateway.SetValues, "set values after failed suppression write");
    return Task.CompletedTask;
  }

  private static Task AutoRetainerSuppressionCleanupIsIdempotent()
  {
    var gateway = new FakeAutoRetainerSuppressionGateway(initialSuppressed: false);
    var coordinator = new AutoRetainerSuppressionCoordinator(gateway);

    coordinator.BeginRun();
    coordinator.EndRun();
    coordinator.EndRun();

    AssertSequenceEqual([true, false], gateway.SetValues, "set values after duplicate cleanup");
    return Task.CompletedTask;
  }

  private static Task AutoRetainerSuppressionRetriesFailedRestore()
  {
    var gateway = new FakeAutoRetainerSuppressionGateway(initialSuppressed: false);
    gateway.SetResults.Enqueue(true);
    gateway.SetResults.Enqueue(false);
    gateway.SetResults.Enqueue(true);
    var coordinator = new AutoRetainerSuppressionCoordinator(gateway);

    coordinator.BeginRun();
    var restoredFirst = coordinator.EndRun();
    var restoredSecond = coordinator.EndRun();

    AssertEqual(false, restoredFirst, "first restore attempt");
    AssertEqual(true, restoredSecond, "second restore attempt");
    AssertEqual(false, coordinator.HasActiveSession, "active session after restore retry");
    AssertSequenceEqual([true, false], gateway.SetValues, "set values after restore retry");
    return Task.CompletedTask;
  }

  private static Task AutoRetainerSuppressionIdleCleanupWaitsForIdle()
  {
    var gateway = new FakeAutoRetainerSuppressionGateway(initialSuppressed: false);
    var coordinator = new AutoRetainerSuppressionCoordinator(gateway);

    coordinator.BeginRun();
    var endedWhileBusy = coordinator.EndRunIfIdle(isTaskManagerBusy: true);
    var endedWhenIdle = coordinator.EndRunIfIdle(isTaskManagerBusy: false);

    AssertEqual(false, endedWhileBusy, "idle cleanup while busy");
    AssertEqual(true, endedWhenIdle, "idle cleanup when idle");
    AssertSequenceEqual([true, false], gateway.SetValues, "set values after idle cleanup");
    return Task.CompletedTask;
  }

  private static Task AutoPinchTimeoutPolicyUsesGeneralTimeout()
  {
    AssertEqual(30_000, AutoPinchTimeoutPolicy.GeneralTaskTimeoutMs, "general auto pinch timeout");
    return Task.CompletedTask;
  }

  private static Task AutoPinchTimeoutPolicyUsesMarketPriceTimeout()
  {
    AssertEqual(45_000, AutoPinchTimeoutPolicy.MarketPriceWaitTimeoutMs, "market price wait timeout");
    AssertEqual(
      true,
      AutoPinchTimeoutPolicy.MarketPriceWaitTimeoutMs > AutoPinchTimeoutPolicy.GeneralTaskTimeoutMs,
      "market price timeout exceeds general timeout");
    return Task.CompletedTask;
  }

  private static Task AutoPinchTimeoutPolicyPadsLegacyBackstop()
  {
    var legacyBackstopTimeoutMs = AutoPinchTimeoutPolicy.GetLegacyBackstopTimeoutMs(
      AutoPinchTimeoutPolicy.MarketPriceWaitTimeoutMs);

    AssertEqual(50_000, legacyBackstopTimeoutMs, "legacy backstop timeout");
    return Task.CompletedTask;
  }

  private static Task AutoPinchDelayTaskWaitsUntilDelayElapses()
  {
    var now = 1_000L;
    var delayTask = AutoPinchDelayTask.Create(500, () => now);

    AssertEqual<bool?>(false, delayTask(), "initial delay result");
    now += 499;
    AssertEqual<bool?>(false, delayTask(), "delay result before elapsed");
    now += 1;
    AssertEqual<bool?>(true, delayTask(), "delay result after elapsed");
    return Task.CompletedTask;
  }

  private static Task AutoPinchDelayTaskTreatsNegativeDelayAsElapsed()
  {
    var delayTask = AutoPinchDelayTask.Create(-1, () => 1_000L);

    AssertEqual<bool?>(true, delayTask(), "negative delay result");
    return Task.CompletedTask;
  }

  private static AutoPinchTaskGuard CreateAutoPinchTaskGuard(
    AutoRetainerSuppressionCoordinator coordinator,
    Action? abortTasks = null,
    Action? removeTalkAddonListeners = null,
    Action<Exception, string>? logException = null,
    Action<TimeoutException, string, int>? reportTimeout = null,
    Func<long>? getTickCount = null)
  {
    return new AutoPinchTaskGuard(
      coordinator,
      abortTasks ?? (() => { }),
      removeTalkAddonListeners ?? (() => { }),
      logException ?? ((_, _) => { }),
      reportTimeout ?? ((_, _, _) => { }),
      getTickCount ?? (() => 0));
  }

  private static bool? RunGuardedTestTask(
    AutoPinchTaskGuard guard,
    Func<bool?> task,
    string taskName)
  {
    return guard.Run(task, taskName, AutoPinchTimeoutPolicy.GeneralTaskTimeoutMs);
  }

  private static Task AutoPinchTaskGuardReportsTimeout()
  {
    var now = 1_000L;
    var gateway = new FakeAutoRetainerSuppressionGateway(initialSuppressed: false);
    var coordinator = new AutoRetainerSuppressionCoordinator(gateway);
    var abortCount = 0;
    var listenerCleanupCount = 0;
    TimeoutException? reportedException = null;
    string? reportedTaskName = null;
    int? reportedTimeoutMs = null;
    var guard = CreateAutoPinchTaskGuard(
      coordinator,
      abortTasks: () => abortCount++,
      removeTalkAddonListeners: () => listenerCleanupCount++,
      reportTimeout: (exception, taskName, timeoutMs) =>
      {
        reportedException = exception;
        reportedTaskName = taskName;
        reportedTimeoutMs = timeoutMs;
      },
      getTickCount: () => now);

    coordinator.BeginRun();

    var firstResult = guard.Run(
      () => false,
      "SlowTask",
      AutoPinchTimeoutPolicy.GeneralTaskTimeoutMs);
    now += AutoPinchTimeoutPolicy.GeneralTaskTimeoutMs;
    var timeoutResult = guard.Run(
      () => false,
      "SlowTask",
      AutoPinchTimeoutPolicy.GeneralTaskTimeoutMs);

    AssertEqual<bool?>(false, firstResult, "first slow task result");
    AssertEqual<bool?>(null, timeoutResult, "timeout result");
    AssertEqual(false, coordinator.HasActiveSession, "active session after timeout");
    AssertSequenceEqual([true, false], gateway.SetValues, "set values after timeout");
    AssertEqual(0, abortCount, "direct abort count");
    AssertEqual(1, listenerCleanupCount, "listener cleanup count");
    AssertEqual("SlowTask", reportedTaskName, "reported task name");
    AssertEqual(AutoPinchTimeoutPolicy.GeneralTaskTimeoutMs, reportedTimeoutMs, "reported timeout");
    AssertEqual(true, reportedException is not null, "reported exception exists");
    AssertContains("SlowTask", reportedException!.Message, "timeout exception message");
    return Task.CompletedTask;
  }

  private static Task AutoPinchTaskGuardResetsTimingAfterTimeout()
  {
    var now = 0L;
    var gateway = new FakeAutoRetainerSuppressionGateway(initialSuppressed: false);
    var coordinator = new AutoRetainerSuppressionCoordinator(gateway);
    var reportCount = 0;
    var guard = CreateAutoPinchTaskGuard(
      coordinator,
      reportTimeout: (_, _, _) => reportCount++,
      getTickCount: () => now);

    coordinator.BeginRun();
    AssertEqual<bool?>(
      false,
      guard.Run(() => false, "RepeatTask", AutoPinchTimeoutPolicy.GeneralTaskTimeoutMs),
      "first repeat task result");
    now = AutoPinchTimeoutPolicy.GeneralTaskTimeoutMs;
    AssertEqual<bool?>(
      null,
      guard.Run(() => false, "RepeatTask", AutoPinchTimeoutPolicy.GeneralTaskTimeoutMs),
      "repeat task timeout result");

    coordinator.BeginRun();
    AssertEqual<bool?>(
      false,
      guard.Run(() => false, "RepeatTask", AutoPinchTimeoutPolicy.GeneralTaskTimeoutMs),
      "repeat task result after reset");

    AssertEqual(1, reportCount, "timeout report count");
    AssertSequenceEqual([true, false, true], gateway.SetValues, "set values after reset");
    return Task.CompletedTask;
  }

  private static Task AutoPinchTaskGuardLeavesActiveSessionAfterSuccessfulTask()
  {
    var gateway = new FakeAutoRetainerSuppressionGateway(initialSuppressed: false);
    var coordinator = new AutoRetainerSuppressionCoordinator(gateway);
    var guard = CreateAutoPinchTaskGuard(
      coordinator,
      abortTasks: () => throw new InvalidOperationException("abort should not run"),
      removeTalkAddonListeners: () => throw new InvalidOperationException("listener cleanup should not run"),
      logException: (_, _) => throw new InvalidOperationException("log should not run"));

    coordinator.BeginRun();

    var result = RunGuardedTestTask(guard, () => true, "SuccessfulTask");

    AssertEqual<bool?>(true, result, "guarded result");
    AssertEqual(true, coordinator.HasActiveSession, "active session after successful task");
    AssertSequenceEqual([true], gateway.SetValues, "set values after successful task");
    return Task.CompletedTask;
  }

  private static Task AutoPinchTaskGuardRestoresSuppressionOnTaskException()
  {
    var gateway = new FakeAutoRetainerSuppressionGateway(initialSuppressed: false);
    var coordinator = new AutoRetainerSuppressionCoordinator(gateway);
    var abortCount = 0;
    var listenerCleanupCount = 0;
    Exception? loggedException = null;
    string? loggedTaskName = null;
    var expectedException = new InvalidOperationException("task failed");
    var guard = CreateAutoPinchTaskGuard(
      coordinator,
      abortTasks: () => abortCount++,
      removeTalkAddonListeners: () => listenerCleanupCount++,
      logException: (exception, taskName) =>
      {
        loggedException = exception;
        loggedTaskName = taskName;
      });

    coordinator.BeginRun();

    try
    {
      RunGuardedTestTask(guard, () => throw expectedException, "FailingTask");
      throw new InvalidOperationException("expected guarded task to throw");
    }
    catch (InvalidOperationException ex) when (ReferenceEquals(ex, expectedException))
    {
    }

    AssertEqual(false, coordinator.HasActiveSession, "active session after exception");
    AssertSequenceEqual([true, false], gateway.SetValues, "set values after exception");
    AssertEqual(1, abortCount, "abort count");
    AssertEqual(1, listenerCleanupCount, "listener cleanup count");
    AssertEqual(expectedException, loggedException, "logged exception");
    AssertEqual("FailingTask", loggedTaskName, "logged task name");
    return Task.CompletedTask;
  }

  private static Task AutoPinchTaskGuardPreservesOriginalExceptionWhenAbortFails()
  {
    var gateway = new FakeAutoRetainerSuppressionGateway(initialSuppressed: false);
    var coordinator = new AutoRetainerSuppressionCoordinator(gateway);
    var listenerCleanupCount = 0;
    Exception? loggedException = null;
    string? loggedTaskName = null;
    var expectedException = new InvalidOperationException("task failed");
    var abortException = new InvalidOperationException("abort failed");
    var guard = CreateAutoPinchTaskGuard(
      coordinator,
      abortTasks: () => throw abortException,
      removeTalkAddonListeners: () => listenerCleanupCount++,
      logException: (exception, taskName) =>
      {
        loggedException = exception;
        loggedTaskName = taskName;
      });

    coordinator.BeginRun();

    try
    {
      RunGuardedTestTask(guard, () => throw expectedException, "FailingAbortTask");
      throw new InvalidOperationException("expected guarded task to throw");
    }
    catch (InvalidOperationException ex) when (ReferenceEquals(ex, expectedException))
    {
    }

    AssertEqual(false, coordinator.HasActiveSession, "active session after abort failure");
    AssertSequenceEqual([true, false], gateway.SetValues, "set values after abort failure");
    AssertEqual(1, listenerCleanupCount, "listener cleanup count");
    AssertEqual(expectedException, loggedException, "logged exception");
    AssertEqual("FailingAbortTask", loggedTaskName, "logged task name");
    AssertEqual(abortException, expectedException.Data[AutoPinchTaskGuard.AbortFailureDataKey], "recorded abort failure");
    return Task.CompletedTask;
  }

  private static Task AutoPinchTaskGuardContinuesCleanupAfterFailedSuppressionRestore()
  {
    var gateway = new FakeAutoRetainerSuppressionGateway(initialSuppressed: false);
    gateway.SetResults.Enqueue(true);
    gateway.SetResults.Enqueue(false);
    var coordinator = new AutoRetainerSuppressionCoordinator(gateway);
    var abortCount = 0;
    var listenerCleanupCount = 0;
    Exception? loggedException = null;
    var expectedException = new InvalidOperationException("task failed");
    var guard = CreateAutoPinchTaskGuard(
      coordinator,
      abortTasks: () => abortCount++,
      removeTalkAddonListeners: () => listenerCleanupCount++,
      logException: (exception, _) => loggedException = exception);

    coordinator.BeginRun();

    try
    {
      RunGuardedTestTask(guard, () => throw expectedException, "FailedRestoreTask");
      throw new InvalidOperationException("expected guarded task to throw");
    }
    catch (InvalidOperationException ex) when (ReferenceEquals(ex, expectedException))
    {
    }

    AssertEqual(true, coordinator.HasActiveSession, "active session after failed restore");
    AssertSequenceEqual([true], gateway.SetValues, "set values after failed restore");
    AssertEqual(1, abortCount, "abort count");
    AssertEqual(1, listenerCleanupCount, "listener cleanup count");
    AssertEqual(expectedException, loggedException, "logged exception");
    AssertEqual(true, expectedException.Data[AutoPinchTaskGuard.SuppressionFailureDataKey], "recorded suppression failure");
    return Task.CompletedTask;
  }

  private static Task AutoPinchTaskGuardIgnoresInactiveSuppressionEnd()
  {
    var gateway = new FakeAutoRetainerSuppressionGateway(initialSuppressed: false);
    var coordinator = new AutoRetainerSuppressionCoordinator(gateway);
    var expectedException = new InvalidOperationException("task failed");
    var guard = CreateAutoPinchTaskGuard(
      coordinator,
      abortTasks: () => { },
      removeTalkAddonListeners: () => { },
      logException: (_, _) => { });

    try
    {
      RunGuardedTestTask(guard, () => throw expectedException, "InactiveSuppressionTask");
      throw new InvalidOperationException("expected guarded task to throw");
    }
    catch (InvalidOperationException ex) when (ReferenceEquals(ex, expectedException))
    {
    }

    AssertEqual(false, expectedException.Data.Contains(AutoPinchTaskGuard.SuppressionFailureDataKey), "suppression failure record");
    AssertSequenceEqual([], gateway.SetValues, "set values without active session");
    return Task.CompletedTask;
  }

  private static Task AutoPinchTaskGuardRecordsListenerCleanupAndLogFailures()
  {
    var gateway = new FakeAutoRetainerSuppressionGateway(initialSuppressed: false);
    var coordinator = new AutoRetainerSuppressionCoordinator(gateway);
    var expectedException = new InvalidOperationException("task failed");
    var listenerException = new InvalidOperationException("listener cleanup failed");
    var logException = new InvalidOperationException("log failed");
    var guard = CreateAutoPinchTaskGuard(
      coordinator,
      abortTasks: () => { },
      removeTalkAddonListeners: () => throw listenerException,
      logException: (_, _) => throw logException);

    coordinator.BeginRun();

    try
    {
      RunGuardedTestTask(guard, () => throw expectedException, "CleanupFailureTask");
      throw new InvalidOperationException("expected guarded task to throw");
    }
    catch (InvalidOperationException ex) when (ReferenceEquals(ex, expectedException))
    {
    }

    AssertEqual(listenerException, expectedException.Data[AutoPinchTaskGuard.ListenerCleanupFailureDataKey], "recorded listener cleanup failure");
    AssertEqual(logException, expectedException.Data[AutoPinchTaskGuard.LogFailureDataKey], "recorded log failure");
    AssertSequenceEqual([true, false], gateway.SetValues, "set values after cleanup failures");
    return Task.CompletedTask;
  }

  private static Task AutoPinchRunPlannerSelectsAllRetainersWhenNoneConfigured()
  {
    var indexes = AutoPinchRunPlanner.SelectRetainerIndexes(
      ["Alpha", "Beta"],
      new HashSet<string>(),
      TestAllDisabledSentinel);

    AssertSequenceEqual([0, 1], indexes, "selected retainer indexes");
    return Task.CompletedTask;
  }

  private static Task AutoPinchRunPlannerSkipsAllRetainersWithSentinel()
  {
    var indexes = AutoPinchRunPlanner.SelectRetainerIndexes(
      ["Alpha", "Beta"],
      new HashSet<string> { TestAllDisabledSentinel },
      TestAllDisabledSentinel);

    AssertSequenceEqual([], indexes, "selected retainer indexes");
    return Task.CompletedTask;
  }

  private static Task AutoPinchRunPlannerSelectsConfiguredRetainersInVisibleOrder()
  {
    var indexes = AutoPinchRunPlanner.SelectRetainerIndexes(
      ["Alpha", "Beta", "Gamma"],
      new HashSet<string> { "Gamma", "Beta" },
      TestAllDisabledSentinel);

    AssertSequenceEqual([1, 2], indexes, "selected retainer indexes");
    return Task.CompletedTask;
  }

  private static Task AutoPinchRunPlannerReturnsNoRetainersWhenConfiguredNamesAreMissing()
  {
    var indexes = AutoPinchRunPlanner.SelectRetainerIndexes(
      ["Alpha", "Beta"],
      new HashSet<string> { "Gamma" },
      TestAllDisabledSentinel);

    AssertSequenceEqual([], indexes, "selected retainer indexes");
    return Task.CompletedTask;
  }

  private static Task AutoPinchRunPlannerDetectsSellListWork()
  {
    AssertEqual(false, AutoPinchRunPlanner.HasSellListItems(0), "empty sell list");
    AssertEqual(true, AutoPinchRunPlanner.HasSellListItems(1), "non empty sell list");
    return Task.CompletedTask;
  }

  private static Task AutoPinchRunPlannerTreatsEmptySellListDifferentlyByEntryPoint()
  {
    var unavailable = AutoPinchRunPlanner.GetSellListWorkState(
      isSellListAvailable: false,
      itemCount: 0);
    var empty = AutoPinchRunPlanner.GetSellListWorkState(
      isSellListAvailable: true,
      itemCount: 0);
    var hasItems = AutoPinchRunPlanner.GetSellListWorkState(
      isSellListAvailable: true,
      itemCount: 1);

    AssertEqual(false, AutoPinchRunPlanner.ShouldStartCurrentRetainerRun(unavailable), "unavailable current retainer start");
    AssertEqual(false, AutoPinchRunPlanner.ShouldStartCurrentRetainerRun(empty), "empty current retainer start");
    AssertEqual(true, AutoPinchRunPlanner.ShouldStartCurrentRetainerRun(hasItems), "non empty current retainer start");
    AssertEqual(false, AutoPinchRunPlanner.ShouldCompleteSelectedRetainerTask(unavailable), "unavailable selected retainer task");
    AssertEqual(true, AutoPinchRunPlanner.ShouldCompleteSelectedRetainerTask(empty), "empty selected retainer task");
    AssertEqual(true, AutoPinchRunPlanner.ShouldCompleteSelectedRetainerTask(hasItems), "non empty selected retainer task");
    return Task.CompletedTask;
  }

  private static Task SaleHistoryPlanIsEmptyWhenToggleIsOff()
  {
    var steps = AutoPinchRunPlanner.PlanSaleHistorySteps(
      isSaleHistoryVisitEnabled: false,
      saleHistoryLabel: TestSaleHistoryLabel);

    AssertSequenceEqual([], steps, "sale history steps");
    return Task.CompletedTask;
  }

  private static Task SaleHistoryPlanIsEmptyWithoutResolvedLabel()
  {
    AssertSequenceEqual(
      [],
      AutoPinchRunPlanner.PlanSaleHistorySteps(isSaleHistoryVisitEnabled: true, saleHistoryLabel: null),
      "sale history steps for null label");
    AssertSequenceEqual(
      [],
      AutoPinchRunPlanner.PlanSaleHistorySteps(isSaleHistoryVisitEnabled: true, saleHistoryLabel: "  "),
      "sale history steps for blank label");
    return Task.CompletedTask;
  }

  private static Task SaleHistoryPlanListsVisitStepsInOrder()
  {
    var steps = AutoPinchRunPlanner.PlanSaleHistorySteps(
      isSaleHistoryVisitEnabled: true,
      saleHistoryLabel: TestSaleHistoryLabel);

    AssertSequenceEqual(
      [
        SaleHistoryStep.OpenSaleHistory,
        SaleHistoryStep.DelayAfterOpenSaleHistory,
        SaleHistoryStep.WaitForRetainerHistory,
        SaleHistoryStep.DwellOnSaleHistory,
        SaleHistoryStep.CloseSaleHistory,
        SaleHistoryStep.DelayAfterCloseSaleHistory
      ],
      steps,
      "sale history steps");
    return Task.CompletedTask;
  }

  private static Task MenuEntryMatcherFindsExactEntry()
  {
    var index = AutoPinchRunPlanner.FindMenuEntryIndex(
      ["Entrust or withdraw items.", "Entrust or withdraw gil.", "Sell items in your inventory on the market.", TestSaleHistoryLabel, "Quit."],
      TestSaleHistoryLabel);

    AssertEqual<int?>(3, index, "matched entry index");
    return Task.CompletedTask;
  }

  private static Task MenuEntryMatcherReturnsNullForMissingEntry()
  {
    var index = AutoPinchRunPlanner.FindMenuEntryIndex(
      ["Entrust or withdraw items.", "Quit."],
      TestSaleHistoryLabel);

    AssertEqual<int?>(null, index, "matched entry index");
    return Task.CompletedTask;
  }

  private static Task MenuEntryMatcherSurvivesExtraLeadingEntries()
  {
    var index = AutoPinchRunPlanner.FindMenuEntryIndex(
      ["View venture report. (Complete)", "Entrust or withdraw items.", "Entrust or withdraw gil.", "Sell items in your inventory on the market.", TestSaleHistoryLabel, "Quit."],
      TestSaleHistoryLabel);

    AssertEqual<int?>(4, index, "matched entry index");
    return Task.CompletedTask;
  }

  private static Task MenuEntryMatcherFallsBackToTrimmedComparison()
  {
    var index = AutoPinchRunPlanner.FindMenuEntryIndex(
      ["Quit.", " View sale history. "],
      TestSaleHistoryLabel);

    AssertEqual<int?>(1, index, "matched entry index");
    return Task.CompletedTask;
  }

  private static Task MenuEntryMatcherFallsBackToSuffixForPayloadPrefixes()
  {
    // The leading character stands in for an evaluated <Gui(...)/> payload glyph left on the live entry text.
    var index = AutoPinchRunPlanner.FindMenuEntryIndex(
      [GuiPayloadGlyph + "Sell items in your inventory on the market.", "Quit."],
      "Sell items in your inventory on the market.");

    AssertEqual<int?>(0, index, "matched entry index");
    return Task.CompletedTask;
  }

  private static Task MenuEntryMatcherPrefersExactOverSuffixMatch()
  {
    // The leading glyph placeholder simulates a <Gui(63)/> SeString payload prefix that
    // makes the entry a suffix match only — the second entry is the exact match.
    var index = AutoPinchRunPlanner.FindMenuEntryIndex(
      [GuiPayloadGlyph + TestSaleHistoryLabel, TestSaleHistoryLabel],
      TestSaleHistoryLabel);

    AssertEqual<int?>(1, index, "matched entry index");
    return Task.CompletedTask;
  }

  private static Task MenuEntryMatcherTriesCandidateLabelsInOrder()
  {
    var entryTexts = new[] { "Entrust or withdraw items.", "Sell items in your retainer's inventory on the market." };

    var index = AutoPinchRunPlanner.FindFirstMenuEntryIndex(
      entryTexts,
      ["Sell items in your inventory on the market.", "Sell items in your retainer's inventory on the market."]);

    AssertEqual<int?>(1, index, "matched entry index");
    AssertEqual<int?>(
      null,
      AutoPinchRunPlanner.FindFirstMenuEntryIndex(entryTexts, []),
      "no labels yields no match");
    return Task.CompletedTask;
  }

  private static Task SaleHistoryStepStatusSkipsBeforeCompletionCheck()
  {
    var status = AutoPinchRunPlanner.GetSaleHistoryStepStatus(
      isVisitSkipped: true,
      isStepComplete: true,
      startedAtTicks: 0,
      nowTicks: 99_999,
      deadlineMs: 10_000);

    AssertEqual(SaleHistoryStepStatus.Skipped, status, "step status");
    return Task.CompletedTask;
  }

  private static Task SaleHistoryStepStatusCompletesFinishedStep()
  {
    var status = AutoPinchRunPlanner.GetSaleHistoryStepStatus(
      isVisitSkipped: false,
      isStepComplete: true,
      startedAtTicks: 0,
      nowTicks: 99_999,
      deadlineMs: 10_000);

    AssertEqual(SaleHistoryStepStatus.Complete, status, "step status");
    return Task.CompletedTask;
  }

  private static Task SaleHistoryStepStatusWaitsBeforeDeadline()
  {
    var status = AutoPinchRunPlanner.GetSaleHistoryStepStatus(
      isVisitSkipped: false,
      isStepComplete: false,
      startedAtTicks: 0,
      nowTicks: 9_999,
      deadlineMs: 10_000);

    AssertEqual(SaleHistoryStepStatus.KeepWaiting, status, "step status");
    return Task.CompletedTask;
  }

  private static Task SaleHistoryStepStatusExpiresAtDeadline()
  {
    var status = AutoPinchRunPlanner.GetSaleHistoryStepStatus(
      isVisitSkipped: false,
      isStepComplete: false,
      startedAtTicks: 0,
      nowTicks: 10_000,
      deadlineMs: 10_000);

    AssertEqual(SaleHistoryStepStatus.DeadlineExceeded, status, "step status");
    return Task.CompletedTask;
  }

  private static Task SaleHistoryStepDeadlineStaysBelowGuardTimeout()
  {
    AssertEqual(
      true,
      AutoPinchTimeoutPolicy.SaleHistoryStepDeadlineMs < AutoPinchTimeoutPolicy.GeneralTaskTimeoutMs,
      "sale history deadline below guard timeout");
    return Task.CompletedTask;
  }

  private static Task AutoRetainerIpcStateSkipsMissingPluginRead()
  {
    var state = new AutoRetainerIpcState(IsAutoRetainerLoaded: false);

    var result = state.TryReadSuppressed(
      readSuppressed: () => throw new InvalidOperationException("read should not run"),
      logWarning: _ => throw new InvalidOperationException("warning should not run"),
      out var isSuppressed);

    AssertEqual(false, result, "read result");
    AssertEqual(false, isSuppressed, "read suppressed fallback");
    return Task.CompletedTask;
  }

  private static Task AutoRetainerIpcStateSkipsMissingPluginWrite()
  {
    var state = new AutoRetainerIpcState(IsAutoRetainerLoaded: false);

    var result = state.TryWriteSuppressed(
      isSuppressed: true,
      writeSuppressed: _ => throw new InvalidOperationException("write should not run"),
      logWarning: _ => throw new InvalidOperationException("warning should not run"));

    AssertEqual(false, result, "write result");
    return Task.CompletedTask;
  }

  private static Task AutoRetainerIpcStateReportsReadFailure()
  {
    var state = new AutoRetainerIpcState(IsAutoRetainerLoaded: true);
    var warnings = new List<Exception>();
    var expectedException = new InvalidOperationException("read failed");

    var result = state.TryReadSuppressed(
      readSuppressed: () => throw expectedException,
      logWarning: warnings.Add,
      out var isSuppressed);

    AssertEqual(false, result, "read result");
    AssertEqual(false, isSuppressed, "read suppressed fallback");
    AssertSequenceEqual([expectedException], warnings, "warnings");
    return Task.CompletedTask;
  }

  private static Task AutoRetainerIpcStateReportsWriteFailure()
  {
    var state = new AutoRetainerIpcState(IsAutoRetainerLoaded: true);
    var warnings = new List<Exception>();
    var expectedException = new InvalidOperationException("write failed");

    var result = state.TryWriteSuppressed(
      isSuppressed: true,
      writeSuppressed: _ => throw expectedException,
      logWarning: warnings.Add);

    AssertEqual(false, result, "write result");
    AssertSequenceEqual([expectedException], warnings, "warnings");
    return Task.CompletedTask;
  }

  private static Task AutoRetainerIpcStateReportsSuccessfulCalls()
  {
    var state = new AutoRetainerIpcState(IsAutoRetainerLoaded: true);
    bool? writtenValue = null;

    var readResult = state.TryReadSuppressed(
      readSuppressed: () => true,
      logWarning: _ => throw new InvalidOperationException("warning should not run"),
      out var isSuppressed);
    var writeResult = state.TryWriteSuppressed(
      isSuppressed: false,
      writeSuppressed: value => writtenValue = value,
      logWarning: _ => throw new InvalidOperationException("warning should not run"));

    AssertEqual(true, readResult, "read result");
    AssertEqual(true, isSuppressed, "read suppressed value");
    AssertEqual(true, writeResult, "write result");
    AssertEqual<bool?>(false, writtenValue, "written value");
    return Task.CompletedTask;
  }

  private static Task AutoPinchCleanupPlanCancelsActiveRun()
  {
    var actions = AutoPinchCleanupPlan.PlanCancelActions(hasActiveSuppression: true);

    AssertSequenceEqual(
      [
        AutoPinchCleanupAction.AbortTasks,
        AutoPinchCleanupAction.EndSuppression,
        AutoPinchCleanupAction.RemoveTalkListeners
      ],
      actions,
      "cancel cleanup actions");
    return Task.CompletedTask;
  }

  private static Task AutoPinchCleanupPlanHandlesDrawCatch()
  {
    var actions = AutoPinchCleanupPlan.PlanDrawCatchActions(hasActiveSuppression: true);

    AssertSequenceEqual(
      [
        AutoPinchCleanupAction.AbortTasks,
        AutoPinchCleanupAction.EndSuppression,
        AutoPinchCleanupAction.LogException,
        AutoPinchCleanupAction.RemoveTalkListeners
      ],
      actions,
      "draw catch cleanup actions");
    return Task.CompletedTask;
  }

  private static Task AutoPinchCleanupPlanDisposesActiveRun()
  {
    var actions = AutoPinchCleanupPlan.PlanDisposeActions(
      hasActiveSuppression: true,
      hasTalkAddonListeners: true);
    var listenerOnlyActions = AutoPinchCleanupPlan.PlanDisposeActions(
      hasActiveSuppression: false,
      hasTalkAddonListeners: true);

    AssertSequenceEqual(
      [
        AutoPinchCleanupAction.EndSuppression,
        AutoPinchCleanupAction.RemoveTalkListeners
      ],
      actions,
      "dispose cleanup actions");
    AssertSequenceEqual([AutoPinchCleanupAction.RemoveTalkListeners], listenerOnlyActions, "listener only dispose cleanup actions");
    return Task.CompletedTask;
  }

  private static Task AutoPinchCleanupPlanWaitsForIdle()
  {
    var busyActions = AutoPinchCleanupPlan.PlanIdleActions(
      hasActiveSuppression: true,
      hasTalkAddonListeners: true,
      isTaskManagerBusy: true);
    var idleActions = AutoPinchCleanupPlan.PlanIdleActions(
      hasActiveSuppression: true,
      hasTalkAddonListeners: true,
      isTaskManagerBusy: false);
    var listenerOnlyActions = AutoPinchCleanupPlan.PlanIdleActions(
      hasActiveSuppression: false,
      hasTalkAddonListeners: true,
      isTaskManagerBusy: false);
    var inactiveActions = AutoPinchCleanupPlan.PlanIdleActions(
      hasActiveSuppression: false,
      hasTalkAddonListeners: false,
      isTaskManagerBusy: false);

    AssertSequenceEqual([], busyActions, "busy idle cleanup actions");
    AssertSequenceEqual(
      [
        AutoPinchCleanupAction.EndSuppression,
        AutoPinchCleanupAction.RemoveTalkListeners
      ],
      idleActions,
      "idle cleanup actions");
    AssertSequenceEqual([AutoPinchCleanupAction.RemoveTalkListeners], listenerOnlyActions, "listener only idle cleanup actions");
    AssertSequenceEqual([], inactiveActions, "inactive idle cleanup actions");
    return Task.CompletedTask;
  }

  private static Task PriceRequestStateRemainsActiveUntilCurrentRequestFinishes()
  {
    var state = new MarketBoardPriceRequestState();

    var version = state.BeginRequest();

    AssertEqual(true, state.IsActive, "active state after request starts");
    AssertEqual(version, state.Version, "current request version");

    state.FinishRequest(version);

    AssertEqual(false, state.IsActive, "active state after request finishes");
    return Task.CompletedTask;
  }

  private static Task PriceRequestStateIgnoresStaleRequestFinish()
  {
    var state = new MarketBoardPriceRequestState();

    var firstVersion = state.BeginRequest();
    var secondVersion = state.BeginRequest();

    state.FinishRequest(firstVersion);

    AssertEqual(true, state.IsActive, "active state after stale request finish");
    AssertEqual(secondVersion, state.Version, "current request version after stale finish");

    state.FinishRequest(secondVersion);

    AssertEqual(false, state.IsActive, "active state after current request finishes");
    return Task.CompletedTask;
  }

  private static BaitGuard.Options DefaultBaitOptions() => new(
    Enabled: true,
    FloorPercent: 30.0f,
    SampleListings: 5,
    GapPercent: 50.0f,
    MinQuantity: 1,
    SaleMedianFloorPercent: 50.0f,
    LowClusterListings: 3,
    LowClusterQuantity: 20,
    LowClusterPriceTolerancePercent: 5.0f);

  private static ThinMarketPricingOptions DefaultThinMarketOptions() => new(
    Enabled: true,
    MaxListings: 2,
    MinRecentSales: 3,
    MaxSaleAgeDays: 30,
    TolerancePercent: 40.0f);

  private static List<TestMarketBoardItemListing> CreateListings(
    params (uint PricePerUnit, uint Quantity)[] listings)
  {
    return listings
      .Select(listing => new TestMarketBoardItemListing(listing.PricePerUnit, listing.Quantity))
      .ToList();
  }

  private static UniversalisPriceProvider CreateProvider(string json)
  {
    var response = new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
    var httpClient = new HttpClient(new StaticResponseHandler(response))
    {
      BaseAddress = new Uri("https://universalis.test/api/v2/")
    };

    return new UniversalisPriceProvider(httpClient, new TestPluginLog());
  }

  [System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "Test providers use short-lived HttpClient instances that remain alive for the scenario duration.")]
  private static UniversalisPriceProvider CreateProvider(HttpMessageHandler handler)
  {
    var httpClient = new HttpClient(handler)
    {
      BaseAddress = new Uri("https://universalis.test/api/v2/")
    };

    return new UniversalisPriceProvider(httpClient, new TestPluginLog());
  }

  private static DateTimeOffset FixedPricingNow()
  {
    return DateTimeOffset.FromUnixTimeSeconds(1778600000);
  }

  private static string FormatExpectedGil(int gil)
  {
    return gil.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
  }

  private static void AssertEqual<T>(T expected, T actual, string label)
  {
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
      throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
  }

  private static void AssertContains(string expectedSubstring, string actual, string label)
  {
    if (!actual.Contains(expectedSubstring, StringComparison.Ordinal))
      throw new InvalidOperationException($"{label}: expected [{actual}] to contain [{expectedSubstring}]");
  }

  private static void AssertNotEqual<T>(T unexpected, T actual, string label)
  {
    if (EqualityComparer<T>.Default.Equals(unexpected, actual))
      throw new InvalidOperationException($"{label}: did not expect {actual}");
  }

  private static void AssertSequenceEqual<T>(
    IReadOnlyList<T> expected,
    IReadOnlyList<T> actual,
    string label)
  {
    if (!expected.SequenceEqual(actual))
    {
      throw new InvalidOperationException(
        $"{label}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
    }
  }
}

internal sealed class StaticResponseHandler(HttpResponseMessage response) : HttpMessageHandler
{
  protected override Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request,
    CancellationToken cancellationToken)
  {
    return Task.FromResult(response);
  }
}

internal sealed class RequestRecordingResponseHandler(string responseJson) : HttpMessageHandler
{
  internal List<string> RequestUris { get; } = [];

  protected override Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request,
    CancellationToken cancellationToken)
  {
    RequestUris.Add(request.RequestUri?.ToString() ?? string.Empty);
    var response = new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
    };

    return Task.FromResult(response);
  }
}

internal sealed class FakeAutoRetainerSuppressionGateway(bool initialSuppressed)
  : IAutoRetainerSuppressionGateway
{
  private bool _suppressed = initialSuppressed;

  internal bool CanGet { get; init; } = true;

  internal List<bool> SetValues { get; } = [];

  internal Queue<bool> SetResults { get; } = [];

  public bool TryGetSuppressed(out bool isSuppressed)
  {
    isSuppressed = _suppressed;
    return CanGet;
  }

  public bool TrySetSuppressed(bool isSuppressed)
  {
    if (SetResults.TryDequeue(out var canSet) && !canSet)
      return false;

    _suppressed = isSuppressed;
    SetValues.Add(isSuppressed);
    return true;
  }
}

internal sealed record TestMarketBoardItemListing(
  uint PricePerUnit,
  uint ItemQuantity) : Dalamud.Game.Network.Structures.IMarketBoardItemListing;

internal sealed class TestPluginLog : IPluginLog
{
  public void Debug(string messageTemplate, params object?[] propertyValues)
  {
  }

  public void Warning(string messageTemplate, params object?[] propertyValues)
  {
  }

  public void Warning(Exception exception, string messageTemplate, params object?[] propertyValues)
  {
  }
}
