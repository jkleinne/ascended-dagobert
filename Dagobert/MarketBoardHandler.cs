using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Network.Structures;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dagobert
{
  internal sealed class MarketBoardHandler : IDisposable
  {
    private readonly ISaleReferenceProvider _saleReferenceProvider;
    private readonly MarketBoardRequestTracker _marketBoardRequestTracker;
    private readonly MarketBoardPriceRequestState _priceRequestState = new();
    private readonly Lumina.Excel.ExcelSheet<Item> _items;
    private bool _isPricingDecisionPending;
    private bool _useHq;
    private bool _itemHq;
    private uint _currentItemId;
    private int _lastRequestId = -1;
    private int _thinFallbackRequestVersion = -1;
    private uint? _expectedListings;
    private CancellationTokenSource? _saleReferenceRequestCancellation;

    private int _bufferedRequestId = -1;
    private readonly List<IMarketBoardItemListing> _bufferedListings = new();

    public bool IsPricePending => _priceRequestState.IsActive;

    public event EventHandler<NewPriceEventArgs>? NewPriceReceived;

    public MarketBoardHandler(
      ISaleReferenceProvider saleReferenceProvider,
      MarketBoardRequestTracker marketBoardRequestTracker)
    {
      _saleReferenceProvider = saleReferenceProvider;
      _marketBoardRequestTracker = marketBoardRequestTracker;
      _items = Svc.Data.GetExcelSheet<Item>();

      Plugin.MarketBoard.OfferingsReceived += MarketBoardOnOfferingsReceived;
      _marketBoardRequestTracker.RequestStarted += MarketBoardRequestTrackerOnRequestStarted;

      Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerSell", AddonRetainerSellPostSetup);
      Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "ItemSearchResult", ItemSearchResultPostSetup);
    }

    public void Dispose()
    {
      _saleReferenceRequestCancellation?.Cancel();
      _saleReferenceRequestCancellation?.Dispose();
      Plugin.MarketBoard.OfferingsReceived -= MarketBoardOnOfferingsReceived;
      _marketBoardRequestTracker.RequestStarted -= MarketBoardRequestTrackerOnRequestStarted;
      Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "RetainerSell", AddonRetainerSellPostSetup);
      Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "ItemSearchResult", ItemSearchResultPostSetup);
    }

    public void PrepareForPriceRequest()
    {
      _saleReferenceRequestCancellation?.Cancel();
      _saleReferenceRequestCancellation?.Dispose();
      _saleReferenceRequestCancellation = null;

      _useHq = Plugin.Configuration.HQ && _itemHq;
      _expectedListings = null;
      _bufferedListings.Clear();
      _bufferedRequestId = -1;
      _thinFallbackRequestVersion = -1;
      _isPricingDecisionPending = false;
      CaptureCurrentItem();
      var requestVersion = _priceRequestState.BeginRequest();
      Svc.Log.Debug(
        "Prepared market board price request {RequestVersion} for item {ItemId}, use HQ {UseHq}, item HQ {ItemHq}",
        requestVersion,
        _currentItemId,
        _useHq,
        _itemHq);
    }

    private async void MarketBoardRequestTrackerOnRequestStarted(MarketBoardRequestStartedEventArgs request)
    {
      if (!_priceRequestState.IsActive)
        return;

      var requestVersion = _priceRequestState.Version;
      Svc.Log.Debug(
        "Market board request {RequestVersion} started for item {ItemId}, status {Status}, expected listings {ExpectedListings}",
        requestVersion,
        _currentItemId,
        request.Status,
        request.AmountToArrive);

      if (!request.Ok)
      {
        Svc.Log.Warning(
          "Market board request {RequestVersion} failed for item {ItemId} with status {Status}",
          requestVersion,
          _currentItemId,
          request.Status);
        FinishPriceRequest(NoPrice(new PricingDebugDetail(
          PricingDebugReason.MarketBoardRequestFailed)
        {
          RequestStatus = request.Status.ToString()
        }), requestVersion);
        return;
      }

      _expectedListings = request.AmountToArrive;
      if (request.AmountToArrive == 0)
      {
        var result = await DecideThinMarketPriceAsync(new ThinMarketListingContext(0, null, null), requestVersion);
        if (!_priceRequestState.IsCurrent(requestVersion))
          return;

        FinishPriceRequest(result ?? NoPrice(BuildThinMarketDebug(
          ThinMarketPricingAction.Skip,
          ThinMarketPricingReason.FallbackDisabled,
          new ThinMarketListingContext(0, null, null),
          null,
          BuildThinMarketOptions(),
          0)), requestVersion);
      }
    }

    private async void MarketBoardOnOfferingsReceived(IMarketBoardCurrentOfferings currentOfferings)
    {
      if (!_priceRequestState.IsActive)
        return;

      if (_isPricingDecisionPending)
        return;

      var requestVersion = _priceRequestState.Version;
      if (currentOfferings.RequestId == _lastRequestId)
      {
        FinishPriceRequest(NoPrice(new PricingDebugDetail(
          PricingDebugReason.DuplicateMarketBoardRequest)), requestVersion);
        return;
      }

      var batchListingCount = currentOfferings.ItemListings.Count();
      AccumulateBatch(currentOfferings);
      Svc.Log.Debug(
        "Market board request {RequestVersion} received offerings batch {RequestId} for item {ItemId}, batch listings {BatchListings}, buffered listings {BufferedListings}, expected listings {ExpectedListings}",
        requestVersion,
        currentOfferings.RequestId,
        _currentItemId,
        batchListingCount,
        _bufferedListings.Count,
        _expectedListings);
      if (_expectedListings is not null && _bufferedListings.Count < _expectedListings.Value)
        return;

      var hqEligible = BuildHqEligibleIndices();
      var thinMarket = BuildThinMarketListingContext(hqEligible);
      Svc.Log.Debug(
        "Market board request {RequestVersion} built pricing candidates for item {ItemId}, eligible listings {EligibleListings}, thin market listings {ThinMarketListings}, floor {FloorPrice}, own lowest {OwnLowestPrice}",
        requestVersion,
        _currentItemId,
        hqEligible.Count,
        thinMarket.ComparableListingCount,
        thinMarket.FloorPrice,
        thinMarket.OwnLowestPrice);
      if (_thinFallbackRequestVersion == requestVersion && _isPricingDecisionPending)
        return;

      if (thinMarket.ComparableListingCount <= Math.Max(0, Plugin.Configuration.ThinMarketMaxListings))
      {
        var thinResult = await DecideThinMarketPriceAsync(thinMarket, requestVersion);
        if (!_priceRequestState.IsCurrent(requestVersion))
          return;

        if (thinResult is not null)
        {
          FinishPriceRequest(thinResult.Value, requestVersion);
          return;
        }

        if (!Plugin.Configuration.EnableThinMarketSaleReferenceFallback && thinMarket.ComparableListingCount > 0)
        {
          FinishPriceRequest(NoPrice(BuildThinMarketDebug(
            ThinMarketPricingAction.Skip,
            ThinMarketPricingReason.FallbackDisabled,
            thinMarket,
            null,
            BuildThinMarketOptions(),
            0)), requestVersion);
          return;
        }
      }

      if (hqEligible.Count == 0)
      {
        if (thinMarket.OwnLowestPrice is null)
        {
          FinishPriceRequest(NoPrice(new PricingDebugDetail(
            PricingDebugReason.NoEligibleListings)
          {
            ListingCount = 0
          }), requestVersion);
          return;
        }

        FinishPriceRequest(new PricingDecisionResult(
          (int)thinMarket.OwnLowestPrice.Value,
          new PricingDebugDetail(PricingDebugReason.OwnPriceAlreadyLowest)
          {
            OwnLowestPrice = (int)thinMarket.OwnLowestPrice.Value,
            SelectedPrice = (int)thinMarket.OwnLowestPrice.Value
          }), requestVersion);
        return;
      }

      PricingDecisionResult? priceDecision;
      _isPricingDecisionPending = true;
      try
      {
        priceDecision = await DecidePriceAsync(hqEligible, requestVersion);
      }
      catch
      {
        if (_priceRequestState.IsCurrent(requestVersion))
          _isPricingDecisionPending = false;
        throw;
      }
      if (!_priceRequestState.IsCurrent(requestVersion))
        return;

      if (priceDecision is null)
      {
        FinishPriceRequest(NoPrice(new PricingDebugDetail(
          PricingDebugReason.NoCredibleListing)
        {
          ListingCount = hqEligible.Count
        }), requestVersion);
        return;
      }

      _lastRequestId = currentOfferings.RequestId;
      FinishPriceRequest(priceDecision.Value, requestVersion);
    }

    private async Task<PricingDecisionResult?> DecideThinMarketPriceAsync(
      ThinMarketListingContext thinMarket,
      int requestVersion)
    {
      var options = BuildThinMarketOptions();
      if (!options.Enabled)
        return null;

      if (thinMarket.ComparableListingCount > Math.Max(0, options.MaxListings))
        return null;

      if (_thinFallbackRequestVersion == requestVersion)
        return null;

      _thinFallbackRequestVersion = requestVersion;
      _isPricingDecisionPending = true;

      try
      {
        var saleReference = await GetThinMarketSaleReferenceAsync(requestVersion);
        if (!_priceRequestState.IsCurrent(requestVersion))
          return null;

        var decision = ThinMarketPricePolicy.Decide(
          thinMarket.ComparableListingCount,
          thinMarket.FloorPrice,
          thinMarket.OwnLowestPrice,
          saleReference,
          options,
          DateTimeOffset.UtcNow);

        return ApplyThinMarketDecision(decision, thinMarket, saleReference, options);
      }
      finally
      {
        if (_priceRequestState.IsCurrent(requestVersion))
          _isPricingDecisionPending = false;
      }
    }

    private Task<SaleReference?> GetThinMarketSaleReferenceAsync(int requestVersion)
      => GetSaleReferenceAsync(
        requestVersion,
        "thin market",
        Math.Clamp(Plugin.Configuration.ThinMarketMinRecentSales, 1, 20),
        Math.Clamp(Plugin.Configuration.ThinMarketMaxSaleAgeDays, 1, 90));

    private Task<SaleReference?> GetBaitGuardSaleReferenceAsync(int requestVersion)
      => GetSaleReferenceAsync(
        requestVersion,
        "bait guard",
        Math.Clamp(Plugin.Configuration.BaitGuardSaleReferenceMinRecentSales, 1, 20),
        Math.Clamp(Plugin.Configuration.BaitGuardSaleReferenceMaxSaleAgeDays, 1, 90));

    private async Task<SaleReference?> GetSaleReferenceAsync(
      int requestVersion,
      string requestSource,
      int minRecentSales,
      int maxSaleAgeDays)
    {
      if (!Plugin.PlayerState.IsLoaded || _currentItemId == 0)
      {
        Svc.Log.Debug(
          "Skipping Universalis {RequestSource} sale reference request {RequestVersion}, player loaded {PlayerLoaded}, item {ItemId}",
          requestSource,
          requestVersion,
          Plugin.PlayerState.IsLoaded,
          _currentItemId);
        return null;
      }

      var requestCancellation = new CancellationTokenSource();
      _saleReferenceRequestCancellation = requestCancellation;
      try
      {
        return await _saleReferenceProvider.GetSaleReferenceAsync(
          Plugin.PlayerState.HomeWorld.RowId,
          _currentItemId,
          _useHq,
          minRecentSales,
          maxSaleAgeDays,
          DateTimeOffset.UtcNow,
          requestCancellation.Token);
      }
      finally
      {
        if (_priceRequestState.IsCurrent(requestVersion))
        {
          requestCancellation.Dispose();
          if (ReferenceEquals(_saleReferenceRequestCancellation, requestCancellation))
            _saleReferenceRequestCancellation = null;
        }
      }
    }

    private static PricingDecisionResult ApplyThinMarketDecision(
      ThinMarketPricingDecision decision,
      ThinMarketListingContext thinMarket,
      SaleReference? saleReference,
      ThinMarketPricingOptions options)
    {
      var price = decision.Action switch
      {
        ThinMarketPricingAction.UseReference => (int)decision.ReferencePrice,
        ThinMarketPricingAction.UndercutFloor => PriceAgainstFloor(decision.ReferencePrice, thinMarket.OwnLowestPrice),
        _ => -1
      };

      var reason = decision.Action switch
      {
        ThinMarketPricingAction.UseReference => PricingDebugReason.ThinMarketUseReference,
        ThinMarketPricingAction.UndercutFloor => PricingDebugReason.ThinMarketUndercutFloor,
        _ => PricingDebugReason.ThinMarketSkip
      };

      return new PricingDecisionResult(
        price,
        BuildThinMarketDebug(
          decision.Action,
          decision.Reason,
          thinMarket,
          saleReference,
          options,
          price) with
        {
          Reason = reason
        });
    }

    private static int PriceAgainstFloor(uint floorPrice, uint? ownLowestPrice)
    {
      if (ownLowestPrice is not null && ownLowestPrice.Value <= floorPrice)
        return (int)ownLowestPrice.Value;

      return Undercut(floorPrice);
    }

    private async Task<PricingDecisionResult?> DecidePriceAsync(List<int> hqEligible, int requestVersion)
    {
      var opts = BuildOptions();

      if (Plugin.Configuration.UndercutSelf)
      {
        int? listingOnlyTarget = BaitGuard.SelectTargetIndex(_bufferedListings, hqEligible, opts);
        if (listingOnlyTarget is null)
          return null;

        SaleReference? selfSaleReference = null;
        if (opts.Enabled)
          selfSaleReference = await GetBaitGuardSaleReferenceAsync(requestVersion);

        if (!_priceRequestState.IsCurrent(requestVersion))
          return null;

        int? target = BaitGuard.SelectTargetIndex(_bufferedListings, hqEligible, opts, selfSaleReference);
        if (target is null)
          return null;

        var selfUndercutCompetitorPrice = _bufferedListings[target.Value].PricePerUnit;
        var selfUndercutSelectedPrice = Undercut(selfUndercutCompetitorPrice);
        return new PricingDecisionResult(
          selfUndercutSelectedPrice,
          BuildUndercutDebug(selfUndercutCompetitorPrice, selfUndercutSelectedPrice));
      }

      var competitors = hqEligible
        .Where(i => !IsOwnRetainer(_bufferedListings[i].RetainerId))
        .ToList();
      var ownIndices = hqEligible
        .Where(i => IsOwnRetainer(_bufferedListings[i].RetainerId))
        .ToList();
      uint? ownLowest = ownIndices.Count == 0
        ? null
        : ownIndices.Min(i => _bufferedListings[i].PricePerUnit);

      int? listingOnlyCompetitorIdx = BaitGuard.SelectTargetIndex(_bufferedListings, competitors, opts);

      if (listingOnlyCompetitorIdx is null)
      {
        if (ownLowest is null)
          return null;

        return new PricingDecisionResult(
          (int)ownLowest.Value,
          new PricingDebugDetail(PricingDebugReason.OwnPriceAlreadyLowest)
          {
            OwnLowestPrice = (int)ownLowest.Value,
            SelectedPrice = (int)ownLowest.Value,
            ListingCount = hqEligible.Count
          });
      }

      var listingOnlyCompetitorPrice = _bufferedListings[listingOnlyCompetitorIdx.Value].PricePerUnit;
      if (ownLowest is not null && ownLowest.Value <= listingOnlyCompetitorPrice)
      {
        return new PricingDecisionResult(
          (int)ownLowest.Value,
          new PricingDebugDetail(PricingDebugReason.OwnPriceAlreadyLowest)
          {
            OwnLowestPrice = (int)ownLowest.Value,
            CompetitorPrice = (int)listingOnlyCompetitorPrice,
            SelectedPrice = (int)ownLowest.Value,
            ListingCount = hqEligible.Count
          });
      }

      SaleReference? competitorSaleReference = null;
      if (opts.Enabled)
        competitorSaleReference = await GetBaitGuardSaleReferenceAsync(requestVersion);

      if (!_priceRequestState.IsCurrent(requestVersion))
        return null;

      int? competitorIdx = BaitGuard.SelectTargetIndex(_bufferedListings, competitors, opts, competitorSaleReference);
      if (competitorIdx is null)
      {
        if (ownLowest is null)
          return null;

        return new PricingDecisionResult(
          (int)ownLowest.Value,
          new PricingDebugDetail(PricingDebugReason.OwnPriceAlreadyLowest)
          {
            OwnLowestPrice = (int)ownLowest.Value,
            SelectedPrice = (int)ownLowest.Value,
            ListingCount = hqEligible.Count
          });
      }

      var competitorPrice = _bufferedListings[competitorIdx.Value].PricePerUnit;
      if (ownLowest is not null && ownLowest.Value <= competitorPrice)
      {
        return new PricingDecisionResult(
          (int)ownLowest.Value,
          new PricingDebugDetail(PricingDebugReason.OwnPriceAlreadyLowest)
          {
            OwnLowestPrice = (int)ownLowest.Value,
            CompetitorPrice = (int)competitorPrice,
            SelectedPrice = (int)ownLowest.Value,
            ListingCount = hqEligible.Count
          });
      }

      var selectedPrice = Undercut(competitorPrice);
      return new PricingDecisionResult(
        selectedPrice,
        BuildUndercutDebug(competitorPrice, selectedPrice) with
        {
          ListingCount = hqEligible.Count
        });
    }

    private static PricingDebugDetail BuildUndercutDebug(uint competitorPrice, int selectedPrice)
    {
      return new PricingDebugDetail(PricingDebugReason.UndercutCompetitor)
      {
        CompetitorPrice = (int)competitorPrice,
        SelectedPrice = selectedPrice,
        UndercutMode = Plugin.Configuration.UndercutMode,
        UndercutAmount = Plugin.Configuration.UndercutAmount,
        UndercutPercent = Plugin.Configuration.UndercutAmountPercentage
      };
    }

    private static PricingDebugDetail BuildThinMarketDebug(
      ThinMarketPricingAction action,
      ThinMarketPricingReason thinMarketReason,
      ThinMarketListingContext thinMarket,
      SaleReference? saleReference,
      ThinMarketPricingOptions options,
      int selectedPrice)
    {
      var reason = action switch
      {
        ThinMarketPricingAction.UseReference => PricingDebugReason.ThinMarketUseReference,
        ThinMarketPricingAction.UndercutFloor => PricingDebugReason.ThinMarketUndercutFloor,
        _ => PricingDebugReason.ThinMarketSkip
      };

      return new PricingDebugDetail(reason)
      {
        ListingCount = thinMarket.ComparableListingCount,
        FloorPrice = thinMarket.FloorPrice is null ? null : (int)thinMarket.FloorPrice.Value,
        OwnLowestPrice = thinMarket.OwnLowestPrice is null ? null : (int)thinMarket.OwnLowestPrice.Value,
        SelectedPrice = selectedPrice > 0 ? selectedPrice : null,
        SaleReference = saleReference,
        ThinMarketReason = thinMarketReason,
        MinRecentSales = options.MinRecentSales,
        MaxSaleAgeDays = options.MaxSaleAgeDays,
        TolerancePercent = options.TolerancePercent,
        UndercutMode = Plugin.Configuration.UndercutMode,
        UndercutAmount = Plugin.Configuration.UndercutAmount,
        UndercutPercent = Plugin.Configuration.UndercutAmountPercentage
      };
    }

    private static int Undercut(uint competitorPricePerUnit)
    {
      int competitor = (int)competitorPricePerUnit;
      if (Plugin.Configuration.UndercutMode == UndercutMode.FixedAmount)
        return Math.Max(competitor - Plugin.Configuration.UndercutAmount, 1);

      float percent = Plugin.Configuration.UndercutAmountPercentage;
      return Math.Max((int)((100f - percent) * competitor / 100f), 1);
    }

    private static BaitGuard.Options BuildOptions() => new(
      Enabled: Plugin.Configuration.EnableBaitGuard,
      FloorPercent: Plugin.Configuration.BaitGuardFloorPercent,
      SampleListings: Plugin.Configuration.BaitGuardSampleListings,
      GapPercent: Plugin.Configuration.BaitGuardGapPercent,
      MinQuantity: Plugin.Configuration.BaitGuardMinQuantity,
      SaleMedianFloorPercent: Math.Clamp(Plugin.Configuration.BaitGuardSaleMedianFloorPercent, 1.0f, 99.0f),
      LowClusterListings: Math.Clamp(Plugin.Configuration.BaitGuardLowClusterListings, 1, 10),
      LowClusterQuantity: Math.Clamp(Plugin.Configuration.BaitGuardLowClusterQuantity, 1, 999),
      LowClusterPriceTolerancePercent: Math.Clamp(Plugin.Configuration.BaitGuardLowClusterPriceTolerancePercent, 0.0f, 25.0f));

    private static ThinMarketPricingOptions BuildThinMarketOptions() => new(
      Enabled: Plugin.Configuration.EnableThinMarketSaleReferenceFallback,
      MaxListings: Plugin.Configuration.ThinMarketMaxListings,
      MinRecentSales: Plugin.Configuration.ThinMarketMinRecentSales,
      MaxSaleAgeDays: Plugin.Configuration.ThinMarketMaxSaleAgeDays,
      TolerancePercent: Plugin.Configuration.ThinMarketSaleReferenceTolerancePercent);

    private ThinMarketListingContext BuildThinMarketListingContext(List<int> hqEligible)
    {
      var eligibleForFloor = Plugin.Configuration.UndercutSelf
        ? hqEligible
        : hqEligible.Where(i => !IsOwnRetainer(_bufferedListings[i].RetainerId)).ToList();
      var ownIndices = Plugin.Configuration.UndercutSelf
        ? new List<int>()
        : hqEligible.Where(i => IsOwnRetainer(_bufferedListings[i].RetainerId)).ToList();

      uint? floorPrice = eligibleForFloor.Count == 0
        ? null
        : eligibleForFloor.Min(i => _bufferedListings[i].PricePerUnit);
      uint? ownLowest = ownIndices.Count == 0
        ? null
        : ownIndices.Min(i => _bufferedListings[i].PricePerUnit);

      return new ThinMarketListingContext(eligibleForFloor.Count, floorPrice, ownLowest);
    }

    private void AccumulateBatch(IMarketBoardCurrentOfferings currentOfferings)
    {
      if (_bufferedRequestId != currentOfferings.RequestId)
      {
        _bufferedListings.Clear();
        _bufferedRequestId = currentOfferings.RequestId;
      }

      foreach (var l in currentOfferings.ItemListings)
        _bufferedListings.Add(l);
    }

    private List<int> BuildHqEligibleIndices()
    {
      var indices = new List<int>(_bufferedListings.Count);
      bool requireHq = _useHq
                       && _currentItemId != 0
                       && _items.TryGetRow(_currentItemId, out var item)
                       && item.CanBeHq;

      for (int i = 0; i < _bufferedListings.Count; i++)
      {
        if (requireHq && !_bufferedListings[i].IsHq)
          continue;
        indices.Add(i);
      }

      return indices;
    }

    private static PricingDecisionResult NoPrice(PricingDebugDetail debugDetail)
    {
      return new PricingDecisionResult(-1, debugDetail);
    }

    private void FinishPriceRequest(PricingDecisionResult result, int requestVersion)
    {
      if (!_priceRequestState.IsCurrent(requestVersion))
      {
        Svc.Log.Debug(
          "Ignored stale market board price request {RequestVersion} finish for item {ItemId}, current request {CurrentRequestVersion}",
          requestVersion,
          _currentItemId,
          _priceRequestState.Version);
        return;
      }

      Svc.Log.Debug(
        "Finished market board price request {RequestVersion} for item {ItemId}, price {Price}, reason {Reason}",
        requestVersion,
        _currentItemId,
        result.Price,
        result.DebugDetail.Reason);
      _priceRequestState.FinishRequest(requestVersion);
      _isPricingDecisionPending = false;
      NewPriceReceived?.Invoke(this, new NewPriceEventArgs(result.Price, result.DebugDetail));
    }

    private void ItemSearchResultPostSetup(AddonEvent type, AddonArgs args)
    {
      if (!_priceRequestState.IsActive)
        PrepareForPriceRequest();
    }

    private unsafe void AddonRetainerSellPostSetup(AddonEvent type, AddonArgs args)
    {
      string nodeText = ((AddonRetainerSell*)args.Addon.Address)->ItemName->NodeText.ToString();
      _itemHq = nodeText.Contains('');
      CaptureCurrentItem();
    }

    private static unsafe uint GetSelectedItemId()
    {
      var selectedItem = InventoryManager.Instance()->GetInventorySlot(InventoryType.BlockedItems, 0);
      return selectedItem == null ? 0 : selectedItem->ItemId;
    }

    private void CaptureCurrentItem()
    {
      _currentItemId = GetSelectedItemId();
    }

    public unsafe void PopulateRetainerCache()
    {
      bool changed = false;
      var retainerManager = RetainerManager.Instance();

      for (uint i = 0; i < retainerManager->GetRetainerCount(); ++i)
      {
        if (!Plugin.Configuration.SeenRetainers.Contains(retainerManager->GetRetainerBySortedIndex(i)->RetainerId))
        {
          Plugin.Configuration.SeenRetainers.Add(retainerManager->GetRetainerBySortedIndex(i)->RetainerId);
          changed = true;
        }

      }

      if (changed)
        Plugin.Configuration.Save();
    }

    private static bool IsOwnRetainer(ulong retainerId) => Plugin.Configuration.SeenRetainers.Contains(retainerId);

    private readonly record struct ThinMarketListingContext(
      int ComparableListingCount,
      uint? FloorPrice,
      uint? OwnLowestPrice);
  }
}
