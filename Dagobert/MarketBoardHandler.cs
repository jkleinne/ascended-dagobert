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
    private readonly IAverageSalePriceProvider _averageSalePriceProvider;
    private readonly MarketBoardRequestTracker _marketBoardRequestTracker;
    private readonly Lumina.Excel.ExcelSheet<Item> _items;
    private bool _newRequest;
    private bool _useHq;
    private bool _itemHq;
    private uint _currentItemId;
    private int _requestVersion;
    private int _lastRequestId = -1;
    private int _thinFallbackRequestVersion = -1;
    private uint? _expectedListings;
    private CancellationTokenSource? _averagePriceRequestCancellation;

    private int _bufferedRequestId = -1;
    private readonly List<IMarketBoardItemListing> _bufferedListings = new();

    public bool IsPricePending { get; private set; }

    public event EventHandler<NewPriceEventArgs>? NewPriceReceived;

    public MarketBoardHandler(
      IAverageSalePriceProvider averageSalePriceProvider,
      MarketBoardRequestTracker marketBoardRequestTracker)
    {
      _averageSalePriceProvider = averageSalePriceProvider;
      _marketBoardRequestTracker = marketBoardRequestTracker;
      _items = Svc.Data.GetExcelSheet<Item>();

      Plugin.MarketBoard.OfferingsReceived += MarketBoardOnOfferingsReceived;
      _marketBoardRequestTracker.RequestStarted += MarketBoardRequestTrackerOnRequestStarted;

      Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerSell", AddonRetainerSellPostSetup);
      Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "ItemSearchResult", ItemSearchResultPostSetup);
    }

    public void Dispose()
    {
      _averagePriceRequestCancellation?.Cancel();
      _averagePriceRequestCancellation?.Dispose();
      Plugin.MarketBoard.OfferingsReceived -= MarketBoardOnOfferingsReceived;
      _marketBoardRequestTracker.RequestStarted -= MarketBoardRequestTrackerOnRequestStarted;
      Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "RetainerSell", AddonRetainerSellPostSetup);
      Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "ItemSearchResult", ItemSearchResultPostSetup);
    }

    public void PrepareForPriceRequest()
    {
      _averagePriceRequestCancellation?.Cancel();
      _averagePriceRequestCancellation?.Dispose();
      _averagePriceRequestCancellation = null;

      _newRequest = true;
      _useHq = Plugin.Configuration.HQ && _itemHq;
      _expectedListings = null;
      _bufferedListings.Clear();
      _bufferedRequestId = -1;
      _thinFallbackRequestVersion = -1;
      IsPricePending = false;
      _requestVersion++;
      CaptureCurrentItem();
    }

    private async void MarketBoardRequestTrackerOnRequestStarted(MarketBoardRequestStartedEventArgs request)
    {
      if (!_newRequest)
        return;

      if (!request.Ok)
      {
        Svc.Log.Warning($"Market board request failed with status {request.Status}");
        FinishPriceRequest(NoPrice(new PricingDebugDetail(
          PricingDebugReason.MarketBoardRequestFailed)
        {
          RequestStatus = request.Status.ToString()
        }));
        return;
      }

      _expectedListings = request.AmountToArrive;
      if (request.AmountToArrive == 0)
      {
        var requestVersion = _requestVersion;
        var result = await DecideThinMarketPriceAsync(new ThinMarketListingContext(0, null, null), requestVersion);
        if (requestVersion != _requestVersion)
          return;

        FinishPriceRequest(result ?? NoPrice(BuildThinMarketDebug(
          ThinMarketPricingAction.Skip,
          ThinMarketPricingReason.FallbackDisabled,
          new ThinMarketListingContext(0, null, null),
          null,
          BuildThinMarketOptions(),
          0)));
      }
    }

    private async void MarketBoardOnOfferingsReceived(IMarketBoardCurrentOfferings currentOfferings)
    {
      if (!_newRequest)
        return;

      if (currentOfferings.RequestId == _lastRequestId)
      {
        FinishPriceRequest(NoPrice(new PricingDebugDetail(
          PricingDebugReason.DuplicateMarketBoardRequest)));
        return;
      }

      var requestVersion = _requestVersion;
      AccumulateBatch(currentOfferings);
      if (_expectedListings is not null && _bufferedListings.Count < _expectedListings.Value)
        return;

      var hqEligible = BuildHqEligibleIndices();
      var thinMarket = BuildThinMarketListingContext(hqEligible);
      if (_thinFallbackRequestVersion == requestVersion && IsPricePending)
        return;

      if (thinMarket.ListingCount <= Math.Max(0, Plugin.Configuration.ThinMarketMaxListings))
      {
        var thinResult = await DecideThinMarketPriceAsync(thinMarket, requestVersion);
        if (thinResult is not null)
        {
          FinishPriceRequest(thinResult.Value);
          return;
        }

        if (!Plugin.Configuration.EnableThinMarketAverageFallback && thinMarket.ListingCount > 0)
        {
          FinishPriceRequest(NoPrice(BuildThinMarketDebug(
            ThinMarketPricingAction.Skip,
            ThinMarketPricingReason.FallbackDisabled,
            thinMarket,
            null,
            BuildThinMarketOptions(),
            0)));
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
          }));
          return;
        }

        FinishPriceRequest(new PricingDecisionResult(
          (int)thinMarket.OwnLowestPrice.Value,
          new PricingDebugDetail(PricingDebugReason.OwnPriceAlreadyLowest)
          {
            OwnLowestPrice = (int)thinMarket.OwnLowestPrice.Value,
            SelectedPrice = (int)thinMarket.OwnLowestPrice.Value
          }));
        return;
      }

      var priceDecision = DecidePrice(hqEligible);
      if (priceDecision is null)
      {
        FinishPriceRequest(NoPrice(new PricingDebugDetail(
          PricingDebugReason.NoCredibleListing)
        {
          ListingCount = hqEligible.Count
        }));
        return;
      }

      _lastRequestId = currentOfferings.RequestId;
      FinishPriceRequest(priceDecision.Value);
    }

    private async Task<PricingDecisionResult?> DecideThinMarketPriceAsync(
      ThinMarketListingContext thinMarket,
      int requestVersion)
    {
      var options = BuildThinMarketOptions();
      if (!options.Enabled)
        return null;

      if (thinMarket.ListingCount > Math.Max(0, options.MaxListings))
        return null;

      if (_thinFallbackRequestVersion == requestVersion)
        return null;

      _thinFallbackRequestVersion = requestVersion;
      IsPricePending = true;

      try
      {
        var averagePrice = await GetAverageSalePriceAsync(requestVersion);
        if (requestVersion != _requestVersion)
          return null;

        var decision = ThinMarketPricePolicy.Decide(
          thinMarket.ListingCount,
          thinMarket.FloorPrice,
          averagePrice,
          options,
          DateTimeOffset.UtcNow);

        return ApplyThinMarketDecision(decision, thinMarket, averagePrice, options);
      }
      finally
      {
        if (requestVersion == _requestVersion)
          IsPricePending = false;
      }
    }

    private async Task<ThinMarketAveragePrice?> GetAverageSalePriceAsync(int requestVersion)
    {
      if (!Plugin.PlayerState.IsLoaded || _currentItemId == 0)
        return null;

      _averagePriceRequestCancellation = new CancellationTokenSource();
      try
      {
        return await _averageSalePriceProvider.GetAverageSalePriceAsync(
          Plugin.PlayerState.HomeWorld.RowId,
          _currentItemId,
          _useHq,
          Plugin.Configuration.ThinMarketMinRecentSales,
          _averagePriceRequestCancellation.Token);
      }
      finally
      {
        if (requestVersion == _requestVersion)
        {
          _averagePriceRequestCancellation.Dispose();
          _averagePriceRequestCancellation = null;
        }
      }
    }

    private static PricingDecisionResult ApplyThinMarketDecision(
      ThinMarketPricingDecision decision,
      ThinMarketListingContext thinMarket,
      ThinMarketAveragePrice? averagePrice,
      ThinMarketPricingOptions options)
    {
      var price = decision.Action switch
      {
        ThinMarketPricingAction.UseAverage => (int)decision.ReferencePrice,
        ThinMarketPricingAction.UndercutFloor => PriceAgainstFloor(decision.ReferencePrice, thinMarket.OwnLowestPrice),
        _ => -1
      };

      var reason = decision.Action switch
      {
        ThinMarketPricingAction.UseAverage => PricingDebugReason.ThinMarketUseAverage,
        ThinMarketPricingAction.UndercutFloor => PricingDebugReason.ThinMarketUndercutFloor,
        _ => PricingDebugReason.ThinMarketSkip
      };

      return new PricingDecisionResult(
        price,
        BuildThinMarketDebug(
          decision.Action,
          decision.Reason,
          thinMarket,
          averagePrice,
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

    private PricingDecisionResult? DecidePrice(List<int> hqEligible)
    {
      var opts = BuildOptions();

      if (Plugin.Configuration.UndercutSelf)
      {
        int? target = BaitGuard.SelectTargetIndex(_bufferedListings, hqEligible, opts);
        if (target is null)
          return null;

        var competitorPrice = _bufferedListings[target.Value].PricePerUnit;
        var selectedPrice = Undercut(competitorPrice);
        return new PricingDecisionResult(
          selectedPrice,
          BuildUndercutDebug(competitorPrice, selectedPrice));
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

      int? competitorIdx = BaitGuard.SelectTargetIndex(_bufferedListings, competitors, opts);

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
      ThinMarketAveragePrice? averagePrice,
      ThinMarketPricingOptions options,
      int selectedPrice)
    {
      var reason = action switch
      {
        ThinMarketPricingAction.UseAverage => PricingDebugReason.ThinMarketUseAverage,
        ThinMarketPricingAction.UndercutFloor => PricingDebugReason.ThinMarketUndercutFloor,
        _ => PricingDebugReason.ThinMarketSkip
      };

      return new PricingDebugDetail(reason)
      {
        ListingCount = thinMarket.ListingCount,
        FloorPrice = thinMarket.FloorPrice is null ? null : (int)thinMarket.FloorPrice.Value,
        OwnLowestPrice = thinMarket.OwnLowestPrice is null ? null : (int)thinMarket.OwnLowestPrice.Value,
        SelectedPrice = selectedPrice > 0 ? selectedPrice : null,
        AveragePrice = averagePrice,
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
      MinQuantity: Plugin.Configuration.BaitGuardMinQuantity);

    private static ThinMarketPricingOptions BuildThinMarketOptions() => new(
      Enabled: Plugin.Configuration.EnableThinMarketAverageFallback,
      MaxListings: Plugin.Configuration.ThinMarketMaxListings,
      MinRecentSales: Plugin.Configuration.ThinMarketMinRecentSales,
      MaxSaleAgeDays: Plugin.Configuration.ThinMarketMaxSaleAgeDays,
      TolerancePercent: Plugin.Configuration.ThinMarketAverageTolerancePercent);

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

    private void FinishPriceRequest(PricingDecisionResult result)
    {
      NewPriceReceived?.Invoke(this, new NewPriceEventArgs(result.Price, result.DebugDetail));
      _newRequest = false;
      IsPricePending = false;
    }

    private void ItemSearchResultPostSetup(AddonEvent type, AddonArgs args)
    {
      if (!_newRequest)
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
      int ListingCount,
      uint? FloorPrice,
      uint? OwnLowestPrice);
  }
}
