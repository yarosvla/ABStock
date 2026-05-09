namespace ABStock.Agents.Models;

public class TradeProposal
{
    public TradeSide Side { get; set; }
    public decimal Price { get; set; }
    public int Quantity { get; set; }
}
