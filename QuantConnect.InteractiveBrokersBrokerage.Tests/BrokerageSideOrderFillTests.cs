/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using IBApi;
using NUnit.Framework;

using QuantConnect.Algorithm;
using QuantConnect.Brokerages;
using QuantConnect.Brokerages.InteractiveBrokers;
using QuantConnect.Lean.Engine.TransactionHandlers;
using QuantConnect.Orders;
using QuantConnect.Orders.Fees;
using QuantConnect.Statistics;

using IB = QuantConnect.Brokerages.InteractiveBrokers.Client;

namespace QuantConnect.Tests.Brokerages.InteractiveBrokers
{
    [TestFixture]
    public class BrokerageSideOrderFillTests
    {
        [Test]
        public void BrokerageSideFillEmitsMessageWhenRegistrationIsRejected()
        {
            using var brokerage = new InteractiveBrokersBrokerage();
            SetPrivateFieldValue(brokerage, "_orderProvider", new OrderProvider());
            SetPrivateFieldValue(
                brokerage,
                "_symbolMapper",
                new InteractiveBrokersSymbolMapper(
                    new Dictionary<SecurityType, Dictionary<string, string>>()));

            var messages = new List<BrokerageMessageEvent>();
            brokerage.Message += (_, message) => messages.Add(message);

            var executionDetails = CreateExecutionDetails();

            var order = typeof(InteractiveBrokersBrokerage)
                .GetMethod("GetOrder", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(brokerage, new object[] { executionDetails });

            Assert.IsNull(order);
            var message = messages.Single();
            Assert.AreEqual(BrokerageMessageType.Information, message.Type);
            Assert.AreEqual("BrokerageSideOrderFill", message.Code);
            StringAssert.Contains("BrokerageOrderId: 0", message.Message);
            StringAssert.Contains("ExecutionId: external-execution", message.Message);
            StringAssert.Contains("Symbol: PDSB", message.Message);
            StringAssert.Contains("Side: SLD", message.Message);
            StringAssert.Contains("Quantity: 100", message.Message);
            StringAssert.Contains("Price: 0.6109", message.Message);
        }

        [Test]
        public void BrokerageSideExecutionRegistersLeanOrderBeforeFill()
        {
            using var brokerage = new InteractiveBrokersBrokerage();
            var orderProvider = new OrderProvider();
            SetPrivateFieldValue(brokerage, "_orderProvider", orderProvider);
            SetPrivateFieldValue(
                brokerage,
                "_symbolMapper",
                new InteractiveBrokersSymbolMapper(
                    new Dictionary<SecurityType, Dictionary<string, string>>()));

            var notifications = new List<NewBrokerageOrderNotificationEventArgs>();
            var messages = new List<BrokerageMessageEvent>();
            brokerage.NewBrokerageOrderNotification += (_, notification) =>
            {
                notifications.Add(notification);
                notification.Order.Status = OrderStatus.Submitted;
                orderProvider.Add(notification.Order);
            };
            brokerage.Message += (_, message) => messages.Add(message);

            var executionDetails = CreateExecutionDetails();
            var order = (QuantConnect.Orders.Order)typeof(InteractiveBrokersBrokerage)
                .GetMethod("GetOrder", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(brokerage, new object[] { executionDetails });

            Assert.IsNotNull(order);
            Assert.AreEqual(1, order.Id);
            Assert.AreEqual(OrderStatus.Submitted, order.Status);
            Assert.AreEqual(-100, order.Quantity);
            CollectionAssert.Contains(order.BrokerId, "0");
            Assert.AreEqual("Brokerage-Side Order", order.Tag);
            Assert.AreSame(order, notifications.Single().Order);
            Assert.IsEmpty(messages);
        }

        [Test]
        public void BrokerageSideOpenOrderRegistersOriginalOrderType()
        {
            using var brokerage = new InteractiveBrokersBrokerage();
            var orderProvider = new OrderProvider();
            SetPrivateFieldValue(brokerage, "_orderProvider", orderProvider);
            SetPrivateFieldValue(
                brokerage,
                "_symbolMapper",
                new InteractiveBrokersSymbolMapper(
                    new Dictionary<SecurityType, Dictionary<string, string>>()));

            var notifications = new List<NewBrokerageOrderNotificationEventArgs>();
            brokerage.NewBrokerageOrderNotification += (_, notification) =>
            {
                notifications.Add(notification);
                orderProvider.Add(notification.Order);
            };

            var openOrderEvent = new IB.OpenOrderEventArgs(
                42,
                new Contract
                {
                    Symbol = "SPY",
                    SecType = IB.SecurityType.Stock,
                    Exchange = "SMART",
                    Currency = "USD"
                },
                new IBApi.Order
                {
                    OrderId = 42,
                    ClientId = 0,
                    PermId = 123456,
                    Action = IB.ActionSide.Buy,
                    TotalQuantity = 10,
                    OrderType = IB.OrderType.Limit,
                    LmtPrice = 500,
                    Tif = IB.TimeInForce.Day
                },
                new OrderState { Status = IB.OrderStatus.Submitted });

            typeof(InteractiveBrokersBrokerage)
                .GetMethod("RegisterBrokerageSideOpenOrders", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(brokerage, new object[] { openOrderEvent });

            var order = notifications.Single().Order;
            Assert.IsInstanceOf<LimitOrder>(order);
            Assert.AreEqual(1, order.Id);
            Assert.AreEqual(OrderStatus.Submitted, order.Status);
            Assert.AreEqual(10, order.Quantity);
            Assert.AreEqual(500, ((LimitOrder)order).LimitPrice);
            CollectionAssert.Contains(order.BrokerId, "42");
        }

        [Test]
        public void BrokerageSideFillEmitsMessageFromOrderStatusWithoutExecutionDetails()
        {
            using var brokerage = new InteractiveBrokersBrokerage();
            var messages = new List<BrokerageMessageEvent>();
            brokerage.Message += (_, message) => messages.Add(message);

            var orderStatus = new IB.OrderStatusEventArgs(
                0,
                IB.OrderStatus.Filled,
                7,
                0,
                0.94,
                674826498,
                0,
                0.94,
                0,
                string.Empty,
                0);

            var reportOrderStatusFill = typeof(InteractiveBrokersBrokerage)
                .GetMethod(
                    "ReportBrokerageSideOrderStatusFill",
                    BindingFlags.NonPublic | BindingFlags.Instance);
            reportOrderStatusFill.Invoke(brokerage, new object[] { orderStatus });
            reportOrderStatusFill.Invoke(brokerage, new object[] { orderStatus });

            var message = messages.Single();
            Assert.AreEqual(BrokerageMessageType.Information, message.Type);
            Assert.AreEqual("BrokerageSideOrderFill", message.Code);
            StringAssert.Contains("BrokerageOrderId: 0", message.Message);
            StringAssert.Contains("PermanentId: 674826498", message.Message);
            StringAssert.Contains("ClientId: 0", message.Message);
            StringAssert.Contains("Status: Filled", message.Message);
            StringAssert.Contains("CumulativeQuantity: 7", message.Message);
            StringAssert.Contains("AverageFillPrice: 0.94", message.Message);
        }

        [Test]
        public void RestoredHoldingSeedsTradeBuilderOnce()
        {
            using var brokerage = new InteractiveBrokersBrokerage();
            var algorithm = new QCAlgorithm();
            SetPrivateFieldValue(brokerage, "_algorithm", algorithm);

            var symbol = new Symbol(
                SecurityIdentifier.GenerateEquity("XLAB", Market.USA, mapSymbol: false),
                "XLAB");
            var restoredAtUtc = new DateTime(2026, 10, 19, 14, 28, 18, DateTimeKind.Utc);
            var holdings = new List<Holding>
            {
                new()
                {
                    Symbol = symbol,
                    Quantity = -1153,
                    AveragePrice = 2.579m
                }
            };

            var seedTradeBuilderFromHoldings = typeof(InteractiveBrokersBrokerage)
                .GetMethod("SeedTradeBuilderFromHoldings", BindingFlags.NonPublic | BindingFlags.Instance);
            seedTradeBuilderFromHoldings.Invoke(brokerage, new object[] { holdings, restoredAtUtc });
            seedTradeBuilderFromHoldings.Invoke(brokerage, new object[] { holdings, restoredAtUtc });

            var closedAtUtc = restoredAtUtc.AddSeconds(22);
            algorithm.TradeBuilder.ProcessFill(
                new OrderEvent(
                    1,
                    symbol,
                    closedAtUtc,
                    OrderStatus.Filled,
                    OrderDirection.Buy,
                    2.55m,
                    1153,
                    OrderFee.Zero),
                1m,
                12.683m);

            var trade = algorithm.TradeBuilder.ClosedTrades.Single();
            Assert.AreEqual(TradeDirection.Short, trade.Direction);
            Assert.AreEqual(1153, trade.Quantity);
            Assert.AreEqual(2.579m, trade.EntryPrice);
            Assert.AreEqual(restoredAtUtc, trade.EntryTime);
            Assert.AreEqual(2.55m, trade.ExitPrice);
            Assert.AreEqual(closedAtUtc, trade.ExitTime);
            Assert.AreEqual(33.44m, trade.ProfitLoss);
            Assert.AreEqual(12.683m, trade.TotalFees);
            Assert.IsFalse(algorithm.TradeBuilder.HasOpenPosition(symbol));
        }

        private static IB.ExecutionDetailsEventArgs CreateExecutionDetails()
        {
            return new IB.ExecutionDetailsEventArgs(
                0,
                new Contract
                {
                    Symbol = "PDSB",
                    SecType = IB.SecurityType.Stock,
                    Exchange = "SMART",
                    Currency = "USD"
                },
                new Execution
                {
                    OrderId = 0,
                    ClientId = 0,
                    PermId = 1221610158,
                    ExecId = "external-execution",
                    Side = "SLD",
                    Shares = 100,
                    CumQty = 100,
                    Price = 0.6109
                });
        }

        private static void SetPrivateFieldValue(object instance, string name, object value)
        {
            instance.GetType()
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(instance, value);
        }
    }
}
