using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradingBot.Domain.Models;

public class OrderBook
{
    public decimal BID { get; set; }
    public decimal ASK { get; set; }
    public decimal BidDepth { get; set; }
    public decimal AskDepth { get; set; }
}
