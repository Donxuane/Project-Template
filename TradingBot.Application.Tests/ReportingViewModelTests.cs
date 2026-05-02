using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Models.GeneralApis;
using Xunit;

namespace TradingBot.Application.Tests;

public class ReportingViewModelTests
{
    [Fact]
    public void DecisionExecutionReportView_EnumModels_AreMapped()
    {
        var view = new DecisionExecutionReportView
        {
            Symbol = TradingSymbol.BNBUSDT,
            DecisionAction = TradeSignal.Buy,
            Side = OrderSide.BUY,
            RawSignal = TradeSignal.Buy,
            TradingMode = TradingMode.Spot,
            ExecutionIntent = TradeExecutionIntent.OpenLong,
            DecisionStatus = DecisionStatus.Executed,
            GuardStage = GuardStage.Execution,
            OrderStatus = OrderStatuses.FILLED,
            ProcessingStatus = ProcessingStatus.Completed,
            OrderSource = OrderSource.DecisionWorker,
            CloseReason = CloseReason.None
        };

        Assert.Equal("BNBUSDT", view.SymbolModel?.Name);
        Assert.Equal("Buy", view.DecisionActionModel?.Name);
        Assert.Equal("BUY", view.SideModel?.Name);
        Assert.Equal("Spot", view.TradingModeModel?.Name);
        Assert.Equal("OpenLong", view.ExecutionIntentModel?.Name);
        Assert.Equal("Executed", view.DecisionStatusModel?.Name);
        Assert.Equal("Execution", view.GuardStageModel?.Name);
        Assert.Equal("DecisionWorker", view.OrderSourceModel?.Name);
        Assert.Equal("None", view.CloseReasonModel?.Name);
    }

    [Fact]
    public void DecisionExecutionReportView_NullEnumModels_AreNullSafe()
    {
        var view = new DecisionExecutionReportView();

        Assert.Null(view.SymbolModel);
        Assert.Null(view.DecisionActionModel);
        Assert.Null(view.SideModel);
        Assert.Null(view.RawSignalModel);
        Assert.Null(view.TradingModeModel);
        Assert.Null(view.ExecutionIntentModel);
        Assert.Null(view.DecisionStatusModel);
        Assert.Null(view.GuardStageModel);
        Assert.Null(view.OrderStatusModel);
        Assert.Null(view.ProcessingStatusModel);
        Assert.Null(view.OrderSourceModel);
        Assert.Null(view.CloseReasonModel);
    }

    [Fact]
    public void DecisionExecutionReportView_SkippedDecision_CanExistWithoutOrder()
    {
        var view = new DecisionExecutionReportView
        {
            DecisionDbId = 10,
            DecisionId = "ABC123",
            DecisionStatus = DecisionStatus.Skipped,
            GuardStage = GuardStage.Cooldown,
            ExecutionSuccess = false,
            ExecutionError = "Execution skipped - cooldown active.",
            LocalOrderId = null,
            ExchangeOrderId = null,
            ExchangeTradeId = null
        };

        Assert.Null(view.LocalOrderId);
        Assert.Null(view.ExchangeOrderId);
        Assert.Null(view.ExchangeTradeId);
        Assert.Equal("Skipped", view.DecisionStatusModel?.Name);
        Assert.Equal("Cooldown", view.GuardStageModel?.Name);
    }

    [Fact]
    public void DecisionExecutionReportView_ExecutedDecision_CanContainOrderAndTradeFields()
    {
        var view = new DecisionExecutionReportView
        {
            DecisionDbId = 20,
            DecisionStatus = DecisionStatus.Executed,
            GuardStage = GuardStage.Execution,
            LocalOrderId = 1001,
            ExchangeOrderId = 2002,
            ExchangeTradeId = 3003,
            ExecutionPrice = 631.55m,
            ExecutedQuantity = 0.01m
        };

        Assert.Equal(1001, view.LocalOrderId);
        Assert.Equal(2002, view.ExchangeOrderId);
        Assert.Equal(3003, view.ExchangeTradeId);
        Assert.Equal("Executed", view.DecisionStatusModel?.Name);
        Assert.Equal("Execution", view.GuardStageModel?.Name);
    }
}
