// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Testing;
using Microsoft.AspNetCore.Testing.xunit;
using Xunit;

namespace Microsoft.AspNetCore.Hosting.Internal
{
    public class HostingEventSourceTests
    {
        [Fact]
        public void MatchesNameAndGuid()
        {
            // Arrange & Act
            var eventSource = new HostingEventSource();

            // Assert
            Assert.Equal("Microsoft.AspNetCore.Hosting", eventSource.Name);
            Assert.Equal(Guid.Parse("9ded64a4-414c-5251-dcf7-1e4e20c15e70"), eventSource.Guid);
        }

        [Fact]
        public void HostStart()
        {
            // Arrange
            var expectedEventId = 1;
            var eventListener = new TestEventListener(expectedEventId);
            var hostingEventSource = GetHostingEventSource();
            eventListener.EnableEvents(hostingEventSource, EventLevel.Informational);

            // Act
            hostingEventSource.HostStart();

            // Assert
            var eventData = eventListener.EventData;
            Assert.NotNull(eventData);
            Assert.Equal(expectedEventId, eventData.EventId);
            Assert.Equal("HostStart", eventData.EventName);
            Assert.Equal(EventLevel.Informational, eventData.Level);
            Assert.Same(hostingEventSource, eventData.EventSource);
            Assert.Null(eventData.Message);
            Assert.Empty(eventData.Payload);
        }

        [Fact]
        public void HostStop()
        {
            // Arrange
            var expectedEventId = 2;
            var eventListener = new TestEventListener(expectedEventId);
            var hostingEventSource = GetHostingEventSource();
            eventListener.EnableEvents(hostingEventSource, EventLevel.Informational);

            // Act
            hostingEventSource.HostStop();

            // Assert
            var eventData = eventListener.EventData;
            Assert.NotNull(eventData);
            Assert.Equal(expectedEventId, eventData.EventId);
            Assert.Equal("HostStop", eventData.EventName);
            Assert.Equal(EventLevel.Informational, eventData.Level);
            Assert.Same(hostingEventSource, eventData.EventSource);
            Assert.Null(eventData.Message);
            Assert.Empty(eventData.Payload);
        }

        public static TheoryData<DefaultHttpContext, string[]> RequestStartData
        {
            get
            {
                var variations = new TheoryData<DefaultHttpContext, string[]>();

                var context = new DefaultHttpContext();
                context.Request.Method = "GET";
                context.Request.Path = "/Home/Index";
                variations.Add(
                    context,
                    new string[]
                    {
                        "GET",
                        "/Home/Index"
                    });

                context = new DefaultHttpContext();
                context.Request.Method = "POST";
                context.Request.Path = "/";
                variations.Add(
                    context,
                    new string[]
                    {
                        "POST",
                        "/"
                    });

                return variations;
            }
        }

        [Theory]
        [MemberData(nameof(RequestStartData))]
        [Flaky("https://github.com/aspnet/AspNetCore-Internal/issues/2230", FlakyOn.All)]
        public void RequestStart(DefaultHttpContext httpContext, string[] expected)
        {
            // Arrange
            var expectedEventId = 3;
            var eventListener = new TestEventListener(expectedEventId);
            var hostingEventSource = GetHostingEventSource();
            eventListener.EnableEvents(hostingEventSource, EventLevel.Informational);

            // Act
            hostingEventSource.RequestStart(httpContext.Request.Method, httpContext.Request.Path);

            // Assert
            var eventData = eventListener.EventData;
            Assert.NotNull(eventData);
            Assert.Equal(expectedEventId, eventData.EventId);
            Assert.Equal("RequestStart", eventData.EventName);
            Assert.Equal(EventLevel.Informational, eventData.Level);
            Assert.Same(hostingEventSource, eventData.EventSource);
            Assert.Null(eventData.Message);

            var payloadList = eventData.Payload;
            Assert.Equal(expected.Length, payloadList.Count);
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], payloadList[i]);
            }
        }

        [Fact]
        public void RequestStop()
        {
            // Arrange
            var expectedEventId = 4;
            var eventListener = new TestEventListener(expectedEventId);
            var hostingEventSource = GetHostingEventSource();
            eventListener.EnableEvents(hostingEventSource, EventLevel.Informational);

            // Act
            hostingEventSource.RequestStop();

            // Assert
            var eventData = eventListener.EventData;
            Assert.Equal(expectedEventId, eventData.EventId);
            Assert.Equal("RequestStop", eventData.EventName);
            Assert.Equal(EventLevel.Informational, eventData.Level);
            Assert.Same(hostingEventSource, eventData.EventSource);
            Assert.Null(eventData.Message);
            Assert.Empty(eventData.Payload);
        }

        [Fact]
        public void UnhandledException()
        {
            // Arrange
            var expectedEventId = 5;
            var eventListener = new TestEventListener(expectedEventId);
            var hostingEventSource = GetHostingEventSource();
            eventListener.EnableEvents(hostingEventSource, EventLevel.Informational);

            // Act
            hostingEventSource.UnhandledException();

            // Assert
            var eventData = eventListener.EventData;
            Assert.Equal(expectedEventId, eventData.EventId);
            Assert.Equal("UnhandledException", eventData.EventName);
            Assert.Equal(EventLevel.Error, eventData.Level);
            Assert.Same(hostingEventSource, eventData.EventSource);
            Assert.Null(eventData.Message);
            Assert.Empty(eventData.Payload);
        }

        [Fact]
        public async Task VerifyCountersFireWithCorrectValues()
        {
            // Arrange
            var eventListener = new CounterListener(new[] {
                "requests-per-second",
                "total-requests",
                "current-requests",
                "failed-requests"
            });

            var hostingEventSource = GetHostingEventSource();

            using var timeoutTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var rpsValues = eventListener.GetCounterValues("requests-per-second", timeoutTokenSource.Token).GetAsyncEnumerator();
            var totalRequestValues = eventListener.GetCounterValues("total-requests", timeoutTokenSource.Token).GetAsyncEnumerator();
            var currentRequestValues = eventListener.GetCounterValues("current-requests", timeoutTokenSource.Token).GetAsyncEnumerator();
            var failedRequestValues = eventListener.GetCounterValues("failed-requests", timeoutTokenSource.Token).GetAsyncEnumerator();

            eventListener.EnableEvents(hostingEventSource, EventLevel.Informational, EventKeywords.None,
                new Dictionary<string, string>
                {
                    { "EventCounterIntervalSec", "1" }
                });

            hostingEventSource.RequestStart("GET", "/");

            Assert.Equal(1, await totalRequestValues.FirstOrDefault(v => v == 1));
            Assert.Equal(1, await rpsValues.FirstOrDefault(v => v == 1));
            Assert.Equal(1, await currentRequestValues.FirstOrDefault(v => v == 1));
            Assert.Equal(0, await failedRequestValues.FirstOrDefault(v => v == 0));

            hostingEventSource.RequestStop();

            Assert.Equal(1, await totalRequestValues.FirstOrDefault(v => v == 1));
            Assert.Equal(0, await rpsValues.FirstOrDefault(v => v == 0));
            Assert.Equal(0, await currentRequestValues.FirstOrDefault(v => v == 0));
            Assert.Equal(0, await failedRequestValues.FirstOrDefault(v => v == 0));

            hostingEventSource.RequestStart("POST", "/");

            Assert.Equal(2, await totalRequestValues.FirstOrDefault(v => v == 2));
            Assert.Equal(1, await rpsValues.FirstOrDefault(v => v == 1));
            Assert.Equal(1, await currentRequestValues.FirstOrDefault(v => v == 1));
            Assert.Equal(0, await failedRequestValues.FirstOrDefault(v => v == 0));

            hostingEventSource.RequestFailed();
            hostingEventSource.RequestStop();

            Assert.Equal(2, await totalRequestValues.FirstOrDefault(v => v == 2));
            Assert.Equal(0, await rpsValues.FirstOrDefault(v => v == 0));
            Assert.Equal(0, await currentRequestValues.FirstOrDefault(v => v == 0));
            Assert.Equal(1, await failedRequestValues.FirstOrDefault(v => v == 1));
        }

        private static HostingEventSource GetHostingEventSource()
        {
            return new HostingEventSource(Guid.NewGuid().ToString());
        }

        private class TestEventListener : EventListener
        {
            private readonly int _eventId;

            public TestEventListener(int eventId)
            {
                _eventId = eventId;
            }

            public EventWrittenEventArgs EventData { get; private set; }

            protected override void OnEventWritten(EventWrittenEventArgs eventData)
            {
                // The tests here run in parallel and since the single publisher instance (HostingEventingSource)
                // notifies all listener instances in these tests, capture the EventData that a test is explicitly
                // looking for and not give back other tests' data.
                if (eventData.EventId == _eventId)
                {
                    EventData = eventData;
                }
            }
        }

        private class CounterListener : EventListener
        {
            private readonly Dictionary<string, Channel<double>> _counters = new Dictionary<string, Channel<double>>();

            public CounterListener(string[] counterNames)
            {
                foreach (var item in counterNames)
                {
                    _counters[item] = Channel.CreateUnbounded<double>();
                }
            }

            public IAsyncEnumerable<double> GetCounterValues(string counterName, CancellationToken cancellationToken = default)
            {
                return _counters[counterName].Reader.ReadAllAsync(cancellationToken);
            }

            protected override void OnEventWritten(EventWrittenEventArgs eventData)
            {
                if (eventData.EventName == "EventCounters")
                {
                    var payload = (IDictionary<string, object>)eventData.Payload[0];
                    var counter = (string)payload["Name"];
                    payload.TryGetValue("Increment", out var increment);
                    payload.TryGetValue("Mean", out var mean);
                    var writer = _counters[counter].Writer;
                    writer.TryWrite((double)(increment ?? mean));
                }
            }
        }
    }
}
