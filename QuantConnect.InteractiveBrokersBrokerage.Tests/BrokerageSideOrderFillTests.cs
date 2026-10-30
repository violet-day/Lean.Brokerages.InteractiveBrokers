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

using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using IBApi;
using NUnit.Framework;

using QuantConnect.Brokerages;
using QuantConnect.Brokerages.InteractiveBrokers;
using QuantConnect.Lean.Engine.TransactionHandlers;
using QuantConnect.Orders;

using IB = QuantConnect.Brokerages.InteractiveBrokers.Client;
using LeanOrder = QuantConnect.Orders.Order;

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
        public void RegisteredBrokerageSideOrderEmitsFillFromOrderStatusWithoutExecutionDetails()
        {
            using var brokerage = new InteractiveBrokersBrokerage();
            var orderProvider = new OrderProvider();
            SetPrivateFieldValue(brokerage, "_orderProvider", orderProvider);
            SetPrivateFieldValue(
                brokerage,
                "_symbolMapper",
                new InteractiveBrokersSymbolMapper(
                    new Dictionary<SecurityType, Dictionary<string, string>>()));

            LeanOrder registeredOrder = null;
            brokerage.NewBrokerageOrderNotification += (_, notification) =>
            {
                registeredOrder = notification.Order;
                registeredOrder.Status = OrderStatus.Submitted;
                orderProvider.Add(registeredOrder);
            };

            var orderEvents = new List<OrderEvent>();
            brokerage.OrdersStatusChanged += (_, events) => orderEvents.AddRange(events);

            RegisterBrokerageSideOpenOrder(
                brokerage,
                CreateOpenOrderEvent(0, 937983719, IB.ActionSide.Buy, 100));

            var orderStatus = new IB.OrderStatusEventArgs(
                0,
                IB.OrderStatus.Filled,
                100,
                0,
                3.55,
                937983719,
                0,
                3.55,
                0,
                string.Empty,
                0);

            var emitBrokerageSideOrderStatusFill = typeof(InteractiveBrokersBrokerage)
                .GetMethod("EmitBrokerageSideOrderStatusFill", BindingFlags.NonPublic | BindingFlags.Instance);
            emitBrokerageSideOrderStatusFill.Invoke(brokerage, new object[] { orderStatus, new List<LeanOrder> { registeredOrder } });
            emitBrokerageSideOrderStatusFill.Invoke(brokerage, new object[] { orderStatus, new List<LeanOrder> { registeredOrder } });

            var fill = orderEvents.Single();
            Assert.AreEqual(registeredOrder.Id, fill.OrderId);
            Assert.AreEqual(OrderStatus.Filled, fill.Status);
            Assert.AreEqual(100, fill.FillQuantity);
            Assert.AreEqual(3.55m, fill.FillPrice);

            var executionDetails = CreateExecutionDetails("DAVI", 937983719, IB.ActionSide.Buy, 100, 3.55);
            var matchedOrder = (LeanOrder)typeof(InteractiveBrokersBrokerage)
                .GetMethod("GetOrder", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(brokerage, new object[] { executionDetails });
            typeof(InteractiveBrokersBrokerage)
                .GetMethod("EmitOrderFill", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(brokerage, new object[]
                {
                    matchedOrder,
                    executionDetails,
                    new CommissionAndFeesReport { CommissionAndFees = 1, Currency = "USD" },
                    false
                });

            Assert.AreEqual(1, orderEvents.Count);
        }

        [Test]
        public void BrokerageSideOrdersWithReusedBrokerageOrderIdAreMatchedByPermanentId()
        {
            using var brokerage = new InteractiveBrokersBrokerage();
            var orderProvider = new OrderProvider();
            SetPrivateFieldValue(brokerage, "_orderProvider", orderProvider);
            SetPrivateFieldValue(
                brokerage,
                "_symbolMapper",
                new InteractiveBrokersSymbolMapper(
                    new Dictionary<SecurityType, Dictionary<string, string>>()));

            var registeredOrders = new List<LeanOrder>();
            brokerage.NewBrokerageOrderNotification += (_, notification) =>
            {
                notification.Order.Status = OrderStatus.Submitted;
                orderProvider.Add(notification.Order);
                registeredOrders.Add(notification.Order);
            };

            RegisterBrokerageSideOpenOrder(
                brokerage,
                CreateOpenOrderEvent(0, 937983719, IB.ActionSide.Buy, 100));

            var secondOrderLookupArguments = new object[]
            {
                0,
                nameof(BrokerageSideOrdersWithReusedBrokerageOrderIdAreMatchedByPermanentId),
                null,
                null,
                false,
                0,
                937983740L
            };
            var foundSecondOrderBeforeRegistration = (bool)typeof(InteractiveBrokersBrokerage)
                .GetMethod("TryGetLeanOrder", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(brokerage, secondOrderLookupArguments);
            Assert.IsFalse(foundSecondOrderBeforeRegistration);

            RegisterBrokerageSideOpenOrder(
                brokerage,
                CreateOpenOrderEvent(0, 937983740, IB.ActionSide.Sell, 100));

            var secondExecutionDetails = CreateExecutionDetails("DAVI", 937983740, IB.ActionSide.Sell, 100, 3.30);
            var matchedOrder = (LeanOrder)typeof(InteractiveBrokersBrokerage)
                .GetMethod("GetOrder", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(brokerage, new object[] { secondExecutionDetails });

            Assert.AreEqual(2, registeredOrders.Count);
            Assert.AreEqual(registeredOrders[1].Id, matchedOrder.Id);
            Assert.AreEqual(-100, matchedOrder.Quantity);
        }

        private static IB.ExecutionDetailsEventArgs CreateExecutionDetails()
        {
            return CreateExecutionDetails("PDSB", 1221610158, IB.ActionSide.Sell, 100, 0.6109);
        }

        private static IB.ExecutionDetailsEventArgs CreateExecutionDetails(
            string symbol,
            long permanentOrderId,
            string action,
            int quantity,
            double price)
        {
            return new IB.ExecutionDetailsEventArgs(
                0,
                new Contract
                {
                    Symbol = symbol,
                    SecType = IB.SecurityType.Stock,
                    Exchange = "SMART",
                    Currency = "USD"
                },
                new Execution
                {
                    OrderId = 0,
                    ClientId = 0,
                    PermId = permanentOrderId,
                    ExecId = "external-execution",
                    Side = action == IB.ActionSide.Sell ? "SLD" : "BOT",
                    Shares = quantity,
                    CumQty = quantity,
                    Price = price
                });
        }

        private static IB.OpenOrderEventArgs CreateOpenOrderEvent(
            int brokerageOrderId,
            long permanentOrderId,
            string action,
            int quantity)
        {
            return new IB.OpenOrderEventArgs(
                brokerageOrderId,
                new Contract
                {
                    Symbol = "DAVI",
                    SecType = IB.SecurityType.Stock,
                    Exchange = "SMART",
                    Currency = "USD"
                },
                new IBApi.Order
                {
                    OrderId = brokerageOrderId,
                    ClientId = 0,
                    PermId = permanentOrderId,
                    Action = action,
                    TotalQuantity = quantity,
                    OrderType = IB.OrderType.Limit,
                    LmtPrice = 3.55,
                    Tif = IB.TimeInForce.Day
                },
                new OrderState { Status = IB.OrderStatus.Submitted });
        }

        private static void RegisterBrokerageSideOpenOrder(
            InteractiveBrokersBrokerage brokerage,
            IB.OpenOrderEventArgs openOrderEvent)
        {
            typeof(InteractiveBrokersBrokerage)
                .GetMethod("RegisterBrokerageSideOpenOrders", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(brokerage, new object[] { openOrderEvent });
        }

        private static void SetPrivateFieldValue(object instance, string name, object value)
        {
            instance.GetType()
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(instance, value);
        }
    }
}
