using Dagobert;
using Dalamud.Plugin.Services;
using System.Net;
using System.Text;

namespace Dagobert.Tests;

internal static class Program
{
  public static async Task<int> Main()
  {
    var tests = new (string Name, Func<Task> Run)[]
    {
      ("NQ average uses only NQ history rows", NqAverageUsesOnlyNqHistoryRows),
      ("HQ average uses only HQ history rows", HqAverageUsesOnlyHqHistoryRows),
      ("No price reason explains bait guard skip", NoPriceReasonExplainsBaitGuardSkip),
      ("No price reason explains every thin market skip reason", NoPriceReasonExplainsEveryThinMarketSkipReason),
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
      ("Sale reference rejects too few matching sales", SaleReferenceRejectsTooFewMatchingSales),
      ("Sale reference rejects stale newest matching sale", SaleReferenceRejectsStaleNewestMatchingSale),
      ("Sale reference skips invalid sale rows", SaleReferenceSkipsInvalidSaleRows),
      ("Post pinch workflow starts with prepare before wait and set", PostPinchWorkflowStartsWithPrepareBeforeWaitAndSet),
      ("Post pinch workflow ignores non button press events", PostPinchWorkflowIgnoresNonButtonPressEvents),
      ("Post pinch workflow ignores missing key press", PostPinchWorkflowIgnoresMissingKeyPress),
      ("Post pinch workflow ignores busy task manager", PostPinchWorkflowIgnoresBusyTaskManager),
      ("Post pinch workflow ignores disabled feature", PostPinchWorkflowIgnoresDisabledFeature),
      ("Post pinch workflow ignores unavailable sell addon", PostPinchWorkflowIgnoresUnavailableSellAddon),
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

  private static async Task NqAverageUsesOnlyNqHistoryRows()
  {
    const long newestHqSale = 1778591298;
    const long onlyNqSale = 1777327167;
    const long olderHqSale = 1777327171;
    var provider = CreateProvider(
      $$"""
      {
        "averagePriceNQ": 8199,
        "averagePriceHQ": 9536,
        "recentHistory": [
          { "hq": true, "pricePerUnit": 9536, "timestamp": {{newestHqSale}} },
          { "hq": false, "pricePerUnit": 8199, "timestamp": {{onlyNqSale}} },
          { "hq": true, "pricePerUnit": 1000, "timestamp": {{olderHqSale}} }
        ]
      }
      """);

    var average = await provider.GetAverageSalePriceAsync(34, 3920, false, 3, CancellationToken.None);

    var price = average ?? throw new InvalidOperationException("expected an NQ average");
    AssertEqual((uint)8199, price.UnitPrice, "NQ average price");
    AssertEqual(1, price.RecentHistoryCount, "NQ recent history count");
    AssertEqual(DateTimeOffset.FromUnixTimeSeconds(onlyNqSale), price.LatestSaleAt, "NQ latest sale");
  }

  private static async Task HqAverageUsesOnlyHqHistoryRows()
  {
    const long newestNqSale = 1778600000;
    const long newestHqSale = 1778500000;
    const long olderHqSale = 1778400000;
    var provider = CreateProvider(
      $$"""
      {
        "averagePriceNQ": 1000,
        "averagePriceHQ": 2500,
        "recentHistory": [
          { "hq": false, "pricePerUnit": 1000, "timestamp": {{newestNqSale}} },
          { "hq": true, "pricePerUnit": 2500, "timestamp": {{newestHqSale}} },
          { "hq": true, "pricePerUnit": 2000, "timestamp": {{olderHqSale}} }
        ]
      }
      """);

    var average = await provider.GetAverageSalePriceAsync(34, 3920, true, 3, CancellationToken.None);

    var price = average ?? throw new InvalidOperationException("expected an HQ average");
    AssertEqual((uint)2500, price.UnitPrice, "HQ average price");
    AssertEqual(2, price.RecentHistoryCount, "HQ recent history count");
    AssertEqual(DateTimeOffset.FromUnixTimeSeconds(newestHqSale), price.LatestSaleAt, "HQ latest sale");
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
        "thin market skipped because average fallback is disabled"),
      (
        ThinMarketPricingReason.TooManyListings,
        new PricingDebugDetail(PricingDebugReason.ThinMarketSkip)
        {
          ThinMarketReason = ThinMarketPricingReason.TooManyListings,
          ListingCount = 4
        },
        "thin market skipped because listing count is above the thin market limit"),
      (
        ThinMarketPricingReason.AverageMissingOrZero,
        new PricingDebugDetail(PricingDebugReason.ThinMarketSkip)
        {
          ThinMarketReason = ThinMarketPricingReason.AverageMissingOrZero
        },
        "thin market skipped because Universalis returned no positive average"),
      (
        ThinMarketPricingReason.NotEnoughRecentSales,
        new PricingDebugDetail(PricingDebugReason.ThinMarketSkip)
        {
          ThinMarketReason = ThinMarketPricingReason.NotEnoughRecentSales,
          AveragePrice = new ThinMarketAveragePrice(5000, 1, now.AddHours(-2)),
          MinRecentSales = 3
        },
        "thin market skipped because Universalis returned 1 recent sale, minimum is 3"),
      (
        ThinMarketPricingReason.LatestSaleMissing,
        new PricingDebugDetail(PricingDebugReason.ThinMarketSkip)
        {
          ThinMarketReason = ThinMarketPricingReason.LatestSaleMissing
        },
        "thin market skipped because Universalis did not return a timestamped recent sale"),
      (
        ThinMarketPricingReason.LatestSaleTooOld,
        new PricingDebugDetail(PricingDebugReason.ThinMarketSkip)
        {
          ThinMarketReason = ThinMarketPricingReason.LatestSaleTooOld,
          AveragePrice = new ThinMarketAveragePrice(5000, 3, now.AddDays(-31)),
          MaxSaleAgeDays = 30
        },
        "thin market skipped because newest Universalis sale is 31 days ago and max age is 30 days"),
      (
        ThinMarketPricingReason.EmptyBoardUseAverage,
        new PricingDebugDetail(PricingDebugReason.ThinMarketSkip)
        {
          ThinMarketReason = ThinMarketPricingReason.EmptyBoardUseAverage
        },
        "thin market skipped because thin market selected an average for an empty board, but the result was not available to set"),
      (
        ThinMarketPricingReason.FloorMissing,
        new PricingDebugDetail(PricingDebugReason.ThinMarketSkip)
        {
          ThinMarketReason = ThinMarketPricingReason.FloorMissing
        },
        "thin market skipped because there is no competitor floor"),
      (
        ThinMarketPricingReason.FloorOutsideTolerance,
        new PricingDebugDetail(PricingDebugReason.ThinMarketSkip)
        {
          ThinMarketReason = ThinMarketPricingReason.FloorOutsideTolerance,
          FloorPrice = 10000,
          AveragePrice = new ThinMarketAveragePrice(5000, 3, now.AddHours(-2)),
          TolerancePercent = 40.0f
        },
        $"thin market skipped because floor {FormatExpectedGil(10000)} gil is outside 40% tolerance of Universalis average {FormatExpectedGil(5000)} gil"),
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
        PricingDebugReason.ThinMarketUseAverage,
        new PricingDebugDetail(PricingDebugReason.ThinMarketUseAverage),
        "thin market selected a Universalis average, but the result was not available to set"),
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
      AveragePrice = new ThinMarketAveragePrice(5000, 1, FixedPricingNow().AddHours(-2)),
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
      AveragePrice = new ThinMarketAveragePrice(5000, 3, FixedPricingNow().AddDays(-31)),
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
        AveragePrice = new ThinMarketAveragePrice(5000, 3, testCase.LatestSaleAt),
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
    var saleReference = new RecentSaleReference(300, 3, DateTimeOffset.UtcNow);
    var target = BaitGuard.SelectTargetIndex(listings, [0, 1, 2], DefaultBaitOptions(), saleReference);

    AssertEqual<int?>(2, target, "tiny low cluster target");
    return Task.CompletedTask;
  }

  private static Task BaitGuardAcceptsBelowFloorClusterWithEnoughListings()
  {
    var listings = CreateListings((100u, 1u), (103u, 1u), (105u, 1u), (300u, 1u));
    var saleReference = new RecentSaleReference(300, 3, DateTimeOffset.UtcNow);
    var target = BaitGuard.SelectTargetIndex(listings, [0, 1, 2, 3], DefaultBaitOptions(), saleReference);

    AssertEqual<int?>(0, target, "listing-backed low cluster target");
    return Task.CompletedTask;
  }

  private static Task BaitGuardAcceptsBelowFloorClusterWithEnoughQuantity()
  {
    var listings = CreateListings((100u, 9u), (104u, 12u), (300u, 1u));
    var saleReference = new RecentSaleReference(300, 3, DateTimeOffset.UtcNow);
    var target = BaitGuard.SelectTargetIndex(listings, [0, 1, 2], DefaultBaitOptions(), saleReference);

    AssertEqual<int?>(0, target, "quantity-backed low cluster target");
    return Task.CompletedTask;
  }

  private static Task BaitGuardAppliesGapPromotionBeforeSaleReference()
  {
    var listings = CreateListings((100u, 100u), (300u, 1u), (305u, 1u));
    var saleReference = new RecentSaleReference(600, 3, DateTimeOffset.UtcNow);
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
        "averagePriceNQ": 1000,
        "averagePriceHQ": 2000,
        "recentHistory": [
          { "hq": false, "pricePerUnit": 400, "timestamp": {{now.ToUnixTimeSeconds()}} },
          { "hq": false, "pricePerUnit": 100, "timestamp": {{now.AddDays(-1).ToUnixTimeSeconds()}} },
          { "hq": false, "pricePerUnit": 300, "timestamp": {{now.AddDays(-2).ToUnixTimeSeconds()}} },
          { "hq": false, "pricePerUnit": 200, "timestamp": {{now.AddDays(-3).ToUnixTimeSeconds()}} },
          { "hq": true, "pricePerUnit": 1, "timestamp": {{now.ToUnixTimeSeconds()}} }
        ]
      }
      """);

    var saleReference = await provider.GetRecentSaleReferenceAsync(34, 3920, false, 3, 30, now, CancellationToken.None);

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
        "averagePriceNQ": 1000,
        "recentHistory": [
          { "hq": false, "pricePerUnit": 100, "timestamp": {{now.ToUnixTimeSeconds()}} },
          { "hq": true, "pricePerUnit": 100, "timestamp": {{now.ToUnixTimeSeconds()}} }
        ]
      }
      """);

    var saleReference = await provider.GetRecentSaleReferenceAsync(34, 3920, false, 2, 30, now, CancellationToken.None);

    AssertEqual<RecentSaleReference?>(null, saleReference, "too few sale reference");
  }

  private static async Task SaleReferenceRejectsStaleNewestMatchingSale()
  {
    var now = DateTimeOffset.FromUnixTimeSeconds(1778600000);
    var provider = CreateProvider(
      $$"""
      {
        "averagePriceNQ": 1000,
        "recentHistory": [
          { "hq": false, "pricePerUnit": 100, "timestamp": {{now.AddDays(-31).ToUnixTimeSeconds()}} },
          { "hq": false, "pricePerUnit": 200, "timestamp": {{now.AddDays(-32).ToUnixTimeSeconds()}} },
          { "hq": false, "pricePerUnit": 300, "timestamp": {{now.AddDays(-33).ToUnixTimeSeconds()}} }
        ]
      }
      """);

    var saleReference = await provider.GetRecentSaleReferenceAsync(34, 3920, false, 3, 30, now, CancellationToken.None);

    AssertEqual<RecentSaleReference?>(null, saleReference, "stale sale reference");
  }

  private static async Task SaleReferenceSkipsInvalidSaleRows()
  {
    var now = DateTimeOffset.FromUnixTimeSeconds(1778600000);
    var provider = CreateProvider(
      $$"""
      {
        "averagePriceNQ": 1000,
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

    var saleReference = await provider.GetRecentSaleReferenceAsync(34, 3920, false, 3, 30, now, CancellationToken.None);

    var reference = saleReference ?? throw new InvalidOperationException("expected a sale reference");
    AssertEqual((uint)200, reference.MedianUnitPrice, "sale median with invalid rows");
    AssertEqual(3, reference.RecentHistoryCount, "valid sale count");
    AssertEqual(now, reference.LatestSaleAt, "latest valid sale");
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

  private static List<TestMarketBoardItemListing> CreateListings(
    params (uint PricePerUnit, uint Quantity)[] listings)
  {
    return listings
      .Select(listing => new TestMarketBoardItemListing(listing.PricePerUnit, listing.Quantity))
      .ToList();
  }

  private static UniversalisAveragePriceProvider CreateProvider(string json)
  {
    var response = new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
    var httpClient = new HttpClient(new StaticResponseHandler(response))
    {
      BaseAddress = new Uri("https://universalis.test/api/v2/")
    };

    return new UniversalisAveragePriceProvider(httpClient, new TestPluginLog());
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
