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

using IB = QuantConnect.Brokerages.InteractiveBrokers.Client;

namespace QuantConnect.Tests.Brokerages.InteractiveBrokers
{
    [TestFixture]
    public class BrokerageSideOrderFillTests
    {
        [Test]
        public void BrokerageSideFillEmitsMessageWithoutCreatingLeanOrder()
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

            var executionDetails = new IB.ExecutionDetailsEventArgs(
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
                    PermId = 1221610158,
                    ExecId = "external-execution",
                    Side = "SLD",
                    Shares = 100,
                    Price = 0.6109
                });

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

        private static void SetPrivateFieldValue(object instance, string name, object value)
        {
            instance.GetType()
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(instance, value);
        }
    }
}
