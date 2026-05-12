using System;

namespace Dagobert
{
  internal sealed class NewPriceEventArgs(int newPrice, PricingDebugDetail? debugDetail) : EventArgs
  {
    public int NewPrice { get; } = newPrice;

    public PricingDebugDetail? DebugDetail { get; } = debugDetail;
  }
}
