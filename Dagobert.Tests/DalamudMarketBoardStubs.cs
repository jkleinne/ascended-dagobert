namespace Dalamud.Game.Network.Structures;

internal interface IMarketBoardItemListing
{
  uint PricePerUnit { get; }

  uint ItemQuantity { get; }
}
