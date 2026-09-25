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
using System.Reflection;
using IBApi;
using NUnit.Framework;
using QuantConnect.Brokerages.InteractiveBrokers;
using QuantConnect.Orders;
using IB = QuantConnect.Brokerages.InteractiveBrokers.Client;
using LeanOrder = QuantConnect.Orders.Order;

namespace QuantConnect.Tests.Brokerages.InteractiveBrokers
{
    [TestFixture]
    public class InteractiveBrokersImmediateOrCancelOrderTests
    {
        private static readonly FieldInfo AccountField =
            typeof(InteractiveBrokersBrokerage).GetField("_account", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo AgentDescriptionField =
            typeof(InteractiveBrokersBrokerage).GetField("_agentDescription", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo ConvertOrderMethod =
            typeof(InteractiveBrokersBrokerage).GetMethod(
                "ConvertOrder",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(List<LeanOrder>), typeof(Contract), typeof(int) },
                modifiers: null);

        [Test]
        public void LimitImmediateOrCancelOrderPreservesOutsideRegularTradingHours()
        {
            var properties = new InteractiveBrokersImmediateOrCancelOrderProperties
            {
                ImmediateOrCancel = true,
                OutsideRegularTradingHours = true
            };
            var order = new LimitOrder(Symbols.SPY, -10m, 100m, DateTime.UtcNow, properties: properties);

            var interactiveBrokersOrder = ConvertOrder(order);

            Assert.AreEqual(IB.TimeInForce.ImmediateOrCancel, interactiveBrokersOrder.Tif);
            Assert.IsTrue(interactiveBrokersOrder.OutsideRth);
        }

        [Test]
        public void MarketImmediateOrCancelOrderUsesImmediateOrCancelTimeInForce()
        {
            var properties = new InteractiveBrokersImmediateOrCancelOrderProperties
            {
                ImmediateOrCancel = true
            };
            var order = new MarketOrder(Symbols.SPY, -10m, DateTime.UtcNow, properties: properties);

            var interactiveBrokersOrder = ConvertOrder(order);

            Assert.AreEqual(IB.TimeInForce.ImmediateOrCancel, interactiveBrokersOrder.Tif);
            Assert.IsFalse(interactiveBrokersOrder.OutsideRth);
        }

        [Test]
        public void DisabledImmediateOrCancelUsesLeanTimeInForce()
        {
            var properties = new InteractiveBrokersImmediateOrCancelOrderProperties
            {
                ImmediateOrCancel = false,
                OutsideRegularTradingHours = true
            };
            var order = new LimitOrder(Symbols.SPY, -10m, 100m, DateTime.UtcNow, properties: properties);

            var interactiveBrokersOrder = ConvertOrder(order);

            Assert.AreEqual(IB.TimeInForce.GoodTillCancel, interactiveBrokersOrder.Tif);
            Assert.IsTrue(interactiveBrokersOrder.OutsideRth);
        }

        [Test]
        public void StandardInteractiveBrokersOrderPropertiesRemainUnchanged()
        {
            var properties = new InteractiveBrokersOrderProperties
            {
                OutsideRegularTradingHours = true
            };
            var order = new LimitOrder(Symbols.SPY, -10m, 100m, DateTime.UtcNow, properties: properties);

            var interactiveBrokersOrder = ConvertOrder(order);

            Assert.AreEqual(IB.TimeInForce.GoodTillCancel, interactiveBrokersOrder.Tif);
            Assert.IsTrue(interactiveBrokersOrder.OutsideRth);
        }

        [Test]
        public void ImmediateOrCancelRejectsUnsupportedOrderType()
        {
            var properties = new InteractiveBrokersImmediateOrCancelOrderProperties
            {
                ImmediateOrCancel = true
            };
            var order = new StopLimitOrder(Symbols.SPY, -10m, 100m, 99m, DateTime.UtcNow, properties: properties);

            var exception = Assert.Throws<TargetInvocationException>(() => ConvertOrder(order));

            Assert.That(exception.InnerException, Is.TypeOf<ArgumentException>());
            Assert.That(exception.InnerException.Message, Does.Contain("only supported for market and limit orders"));
        }

        [Test]
        public void ClonePreservesImmediateOrCancelSettingAndType()
        {
            var properties = new InteractiveBrokersImmediateOrCancelOrderProperties
            {
                ImmediateOrCancel = true,
                OutsideRegularTradingHours = true
            };

            var clone = properties.Clone();

            Assert.That(clone, Is.TypeOf<InteractiveBrokersImmediateOrCancelOrderProperties>());
            var clonedProperties = (InteractiveBrokersImmediateOrCancelOrderProperties)clone;
            Assert.IsTrue(clonedProperties.ImmediateOrCancel);
            Assert.IsTrue(clonedProperties.OutsideRegularTradingHours);
        }

        private static IBApi.Order ConvertOrder(LeanOrder order)
        {
            var brokerage = new InteractiveBrokersBrokerage();
            AccountField.SetValue(brokerage, "U1234567");
            AgentDescriptionField.SetValue(brokerage, "I");
            brokerage._contractSpecificationService = new IB.ContractSpecificationService(
                (contract, ticker, failIfNotFound) => new ContractDetails
                {
                    Contract = contract,
                    MinTick = 0.01
                });

            var contract = new Contract
            {
                Symbol = "SPY",
                SecType = IB.SecurityType.Stock,
                Exchange = "SMART",
                Currency = "USD"
            };

            return (IBApi.Order)ConvertOrderMethod.Invoke(
                brokerage,
                new object[] { new List<LeanOrder> { order }, contract, 1 });
        }
    }
}
