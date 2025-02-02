﻿/*
 * Copyright (c) 2020 Proton Technologies AG
 *
 * This file is part of ProtonVPN.
 *
 * ProtonVPN is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * ProtonVPN is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with ProtonVPN.  If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FluentAssertions.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ProtonVPN.Common.Logging;
using ProtonVPN.Common.Threading;
using ProtonVPN.Core.Abstract;
using ProtonVPN.Core.Api;
using ProtonVPN.Core.Api.Contracts;
using ProtonVPN.Core.Api.Handlers;
using ProtonVPN.Core.Settings;
using ProtonVPN.Test.Common.Breakpoints;
using RichardSzalay.MockHttp;

namespace ProtonVPN.Core.Test.Api.Handlers
{
    [TestClass]
    public class UnauthorizedResponseHandlerTest
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
        private readonly Uri _baseAddress = new Uri("https://api.protonvpn.ch");

        private ITokenClient _tokenClient;
        private ITokenStorage _tokenStorage;
        private IUserStorage _userStorage;
        private ILogger _logger;
        private MockHttpMessageHandler _innerHandler;

        [TestInitialize]
        public void TestInitialize()
        {
            _tokenClient = Substitute.For<ITokenClient>();
            _tokenClient.RefreshTokenAsync(Arg.Any<CancellationToken>())
                .Returns(ApiResponseResult<RefreshTokenResponse>.Ok(new RefreshTokenResponse()));

            _tokenStorage = Substitute.For<ITokenStorage>();
            _tokenStorage.AccessToken.Returns("Access token");
            _tokenStorage.RefreshToken.Returns("Refresh token");
            _tokenStorage.Uid.Returns("User ID");

            _userStorage = Substitute.For<IUserStorage>();
            _userStorage.User().Returns(new Core.Models.User {Username = "test"});

            _logger = Substitute.For<ILogger>();

            _innerHandler = new MockHttpMessageHandler();
        }

        [TestMethod]
        public async Task SendAsync_ShouldBe_InnerHandlerSendAsync()
        {
            // Arrange
            UnauthorizedResponseHandler handler = new UnauthorizedResponseHandler(_tokenClient, _tokenStorage, _userStorage, _logger) { InnerHandler = _innerHandler };
            HttpClient client = new HttpClient(handler) { BaseAddress = _baseAddress };

            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK);
            _innerHandler.Expect(HttpMethod.Get, "https://api.protonvpn.ch/logicals")
                .Respond(req => response);

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/logicals");

            // Act
            HttpResponseMessage result = await client.SendAsync(request);

            // Assert
            result.Should().BeSameAs(response);
            _innerHandler.VerifyNoOutstandingExpectation();
        }

        [TestMethod]
        public async Task SendAsync_ShouldCall_TokenClient_RefreshTokenAsync_WhenUnauthorized()
        {
            // Arrange
            UnauthorizedResponseHandler handler = new UnauthorizedResponseHandler(_tokenClient, _tokenStorage, _userStorage, _logger) { InnerHandler = _innerHandler };
            HttpClient client = new HttpClient(handler) { BaseAddress = _baseAddress };

            _innerHandler.Expect(HttpMethod.Get, "https://api.protonvpn.ch/logicals")
                .Respond(HttpStatusCode.Unauthorized);

            // Act
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/logicals");
            await client.SendAsync(request);

            // Assert
            await _tokenClient.Received(1).RefreshTokenAsync(Arg.Any<CancellationToken>());
        }

        [TestMethod]
        public async Task SendAsync_ShouldNotCall_TokenClient_RefreshTokenAsync_WhenRefreshTokenIsNull()
        {
            _tokenStorage.RefreshToken.Returns((string)null);

            await SendAsync_ShouldNotCall_TokenClient_RefreshTokenAsync_WhenCurrentTokenIsInvalid();
        }

        private async Task SendAsync_ShouldNotCall_TokenClient_RefreshTokenAsync_WhenCurrentTokenIsInvalid()
        {
            // Arrange
            UnauthorizedResponseHandler handler = new UnauthorizedResponseHandler(_tokenClient, _tokenStorage, _userStorage, _logger) { InnerHandler = _innerHandler };
            HttpClient client = new HttpClient(handler) { BaseAddress = _baseAddress };

            _innerHandler.Expect(HttpMethod.Get, "https://api.protonvpn.ch/logicals")
                .Respond(HttpStatusCode.Unauthorized);

            // Act
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/logicals");
            await client.SendAsync(request);

            // Assert
            await _tokenClient.Received(0).RefreshTokenAsync(Arg.Any<CancellationToken>());
        }

        [TestMethod]
        public async Task SendAsync_ShouldNotCall_TokenClient_RefreshTokenAsync_WhenRefreshTokenIsEmpty()
        {
            _tokenStorage.RefreshToken.Returns(string.Empty);

            await SendAsync_ShouldNotCall_TokenClient_RefreshTokenAsync_WhenCurrentTokenIsInvalid();
        }

        [TestMethod]
        public async Task SendAsync_ShouldNotCall_TokenClient_RefreshTokenAsync_WhenTokenUserIdIsNull()
        {
            _tokenStorage.Uid.Returns((string)null);

            await SendAsync_ShouldNotCall_TokenClient_RefreshTokenAsync_WhenCurrentTokenIsInvalid();
        }

        [TestMethod]
        public async Task SendAsync_ShouldNotCall_TokenClient_RefreshTokenAsync_WhenTokenUserIdIsEmpty()
        {
            _tokenStorage.Uid.Returns(string.Empty);

            await SendAsync_ShouldNotCall_TokenClient_RefreshTokenAsync_WhenCurrentTokenIsInvalid();
        }

        [TestMethod]
        public async Task SendAsync_ShouldNotCall_TokenClient_RefreshTokenAsync_WhenTokenClientRefreshTokenAsyncThrowsArgumentNullException()
        {
            // Arrange
            string exceptionMessage = "The RefreshToken in RefreshTokenData can't be null.";
            ArgumentNullException exception = new ArgumentNullException(exceptionMessage);
            _tokenClient.RefreshTokenAsync(Arg.Any<CancellationToken>())
                .Throws(exception);

            UnauthorizedResponseHandler handler = new UnauthorizedResponseHandler(_tokenClient, _tokenStorage, _userStorage, _logger) { InnerHandler = _innerHandler };
            HttpClient client = new HttpClient(handler) { BaseAddress = _baseAddress };

            _innerHandler.Expect(HttpMethod.Get, "https://api.protonvpn.ch/logicals")
                .Respond(HttpStatusCode.Unauthorized);

            // Act
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/logicals");
            await client.SendAsync(request);

            // Assert
            await _tokenClient.Received(1).RefreshTokenAsync(Arg.Any<CancellationToken>());
            _logger.Received(1).Error($"An error occurred when refreshing the auth token: {exceptionMessage}");
        }

        [TestMethod]
        public async Task SendAsync_ShouldRepeatRequest_WithRefreshedAccessToken()
        {
            // Arrange
            _tokenClient.RefreshTokenAsync(Arg.Any<CancellationToken>())
                .Returns(ApiResponseResult<RefreshTokenResponse>.Ok(
                    new RefreshTokenResponse { AccessToken = "New access token", RefreshToken = "New refresh token" }));
            UnauthorizedResponseHandler handler = new UnauthorizedResponseHandler(_tokenClient, _tokenStorage, _userStorage, _logger) { InnerHandler = _innerHandler };
            HttpClient client = new HttpClient(handler) { BaseAddress = _baseAddress };

            _innerHandler.Expect(HttpMethod.Get, "https://api.protonvpn.ch/logicals")
                .Respond(HttpStatusCode.Unauthorized);
            _innerHandler.Expect(HttpMethod.Get, "https://api.protonvpn.ch/logicals")
                .WithHeaders("Authorization", "Bearer New access token")
                .Respond(HttpStatusCode.OK);

            // Act
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/logicals");
            await client.SendAsync(request);

            // Assert
            _innerHandler.VerifyNoOutstandingExpectation();
        }

        [TestMethod]
        public async Task SendAsync_ShouldSet_TokenStorage_Tokens()
        {
            // Arrange
            _tokenClient.RefreshTokenAsync(Arg.Any<CancellationToken>())
                .Returns(ApiResponseResult<RefreshTokenResponse>.Ok(
                    new RefreshTokenResponse { AccessToken = "New access token", RefreshToken = "New refresh token" }));
            UnauthorizedResponseHandler handler = new UnauthorizedResponseHandler(_tokenClient, _tokenStorage, _userStorage, _logger) { InnerHandler = _innerHandler };
            HttpClient client = new HttpClient(handler) { BaseAddress = _baseAddress };

            _innerHandler.Expect(HttpMethod.Get, "https://api.protonvpn.ch/logicals")
                .Respond(HttpStatusCode.Unauthorized);

            // Act
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/logicals");
            await client.SendAsync(request);

            // Assert
            _tokenStorage.AccessToken.Should().Be("New access token");
            _tokenStorage.RefreshToken.Should().Be("New refresh token");
        }

        [TestMethod]
        public async Task SendAsync_ShouldBe_InnerHandlerSendAsync_WhenRepeatedRequest()
        {
            // Arrange
            UnauthorizedResponseHandler handler = new UnauthorizedResponseHandler(_tokenClient, _tokenStorage, _userStorage, _logger) { InnerHandler = _innerHandler };
            HttpClient client = new HttpClient(handler) { BaseAddress = _baseAddress };

            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK);
            _innerHandler.Expect(HttpMethod.Get, "https://api.protonvpn.ch/logicals")
                .Respond(HttpStatusCode.Unauthorized);
            _innerHandler.Expect(HttpMethod.Get, "https://api.protonvpn.ch/logicals")
                .Respond(req => response);

            // Act
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/logicals");
            HttpResponseMessage result = await client.SendAsync(request);

            // Assert
            result.Should().BeSameAs(response);
        }

        [TestMethod]
        public async Task SendAsync_ShouldRaise_SessionExpired_WhenRefreshFailed()
        {
            // Arrange
            _tokenClient.RefreshTokenAsync(Arg.Any<CancellationToken>())
                .Returns(ApiResponseResult<RefreshTokenResponse>.Fail(HttpStatusCode.BadRequest, "Refresh failed"));
            UnauthorizedResponseHandler handler = new UnauthorizedResponseHandler(_tokenClient, _tokenStorage, _userStorage, _logger) { InnerHandler = _innerHandler };
            HttpClient client = new HttpClient(handler) { BaseAddress = _baseAddress };

            _innerHandler.Expect(HttpMethod.Get, "https://api.protonvpn.ch/logicals")
                .Respond(HttpStatusCode.Unauthorized);

            using (IMonitor<UnauthorizedResponseHandler> monitoredSubject = handler.Monitor())
            {
                // Act
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/logicals");
                await client.SendAsync(request);

                // Assert
                monitoredSubject.Should().Raise(nameof(UnauthorizedResponseHandler.SessionExpired));
                _innerHandler.VerifyNoOutstandingExpectation();
            }
        }

        [TestMethod]
        public async Task SendAsync_ShouldLimit_RefreshRequests_ToOne()
        {
            // Arrange
            BreakpointHandler breakpointHandler = new BreakpointHandler { InnerHandler = _innerHandler};
            Breakpoint requestBreakpoint = breakpointHandler.Breakpoint;
            BreakpointTokenClient breakpointTokenClient = new BreakpointTokenClient(_tokenClient);
            Breakpoint tokenClientBreakpoint = breakpointTokenClient.Breakpoint;
            UnauthorizedResponseHandler handler = new UnauthorizedResponseHandler(breakpointTokenClient, _tokenStorage, _userStorage, _logger) { InnerHandler = breakpointHandler };
            HttpClient client = new HttpClient(handler) { BaseAddress = _baseAddress };
            
            _tokenClient.RefreshTokenAsync(Arg.Any<CancellationToken>())
                .Returns(ApiResponseResult<RefreshTokenResponse>.Ok(
                    new RefreshTokenResponse { AccessToken = "New access token", RefreshToken = "New refresh token" }));

            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK);

            // Act
            Task task1 = Task.CompletedTask;
            Task task2 = Task.CompletedTask;
            try
            {
                // Sending first request and pause it
                HttpRequestMessage request1 = new HttpRequestMessage(HttpMethod.Get, "/vpn");
                task1 = client.SendAsync(request1);
                BreakpointHit request1Hit = await requestBreakpoint.WaitForHit().TimeoutAfter(TestTimeout);

                // Sending second request and pause it
                HttpRequestMessage request2 = new HttpRequestMessage(HttpMethod.Get, "/vpn");
                task2 = client.SendAsync(request2);
                BreakpointHit request2Hit = await requestBreakpoint.WaitForHit().TimeoutAfter(TestTimeout);

                // Continue first and second requests and get Unauthorized
                _innerHandler.Expect(HttpMethod.Get, "https://api.protonvpn.ch/vpn")
                    .Respond(HttpStatusCode.Unauthorized);
                _innerHandler.Expect(HttpMethod.Get, "https://api.protonvpn.ch/vpn")
                    .Respond(HttpStatusCode.Unauthorized);
                request1Hit.Continue();
                request2Hit.Continue();

                // Token refresh
                await tokenClientBreakpoint.WaitForHitAndContinue().TimeoutAfter(TestTimeout);

                // First and second requests retried with new access token
                _innerHandler.Expect(HttpMethod.Get, "https://api.protonvpn.ch/vpn")
                    .WithHeaders("Authorization", "Bearer New access token")
                    .Respond(req => response);
                _innerHandler.Expect(HttpMethod.Get, "https://api.protonvpn.ch/vpn")
                    .WithHeaders("Authorization", "Bearer New access token")
                    .Respond(req => response);
                await requestBreakpoint.WaitForHitAndContinue().TimeoutAfter(TestTimeout);
                await requestBreakpoint.WaitForHitAndContinue().TimeoutAfter(TestTimeout);
            }
            finally
            {
                await task1.TimeoutAfter(TestTimeout);
                await task2.TimeoutAfter(TestTimeout);
            }

            // Assert
            await _tokenClient.Received(1).RefreshTokenAsync(Arg.Any<CancellationToken>());
            _innerHandler.VerifyNoOutstandingExpectation();
        }

        [TestMethod]
        public async Task SendAsync_ShouldSuppressRequest_WhenRefreshingTokens()
        {
            // Arrange
            BreakpointHandler breakpointHandler = new BreakpointHandler { InnerHandler = _innerHandler };
            Breakpoint requestBreakpoint = breakpointHandler.Breakpoint;
            BreakpointTokenClient breakpointTokenClient = new BreakpointTokenClient(_tokenClient);
            Breakpoint tokenClientBreakpoint = breakpointTokenClient.Breakpoint;
            UnauthorizedResponseHandler handler = new UnauthorizedResponseHandler(breakpointTokenClient, _tokenStorage, _userStorage, _logger) { InnerHandler = breakpointHandler };
            HttpClient client = new HttpClient(handler) { BaseAddress = _baseAddress };

            _tokenClient.RefreshTokenAsync(Arg.Any<CancellationToken>())
                .Returns(ApiResponseResult<RefreshTokenResponse>.Ok(
                    new RefreshTokenResponse { AccessToken = "New access token", RefreshToken = "New refresh token" }));

            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK);

            // Act
            Task task1 = Task.CompletedTask;
            Task task2 = Task.CompletedTask;
            try
            {
                // Sending first request
                HttpRequestMessage request1 = new HttpRequestMessage(HttpMethod.Get, "/vpn");
                task1 = client.SendAsync(request1);

                // First request continues and gets Unauthorized
                _innerHandler.Expect(HttpMethod.Get, "https://api.protonvpn.ch/vpn")
                    .Respond(HttpStatusCode.Unauthorized);
                await requestBreakpoint.WaitForHitAndContinue().TimeoutAfter(TestTimeout);
                
                // First request initiated token refresh, pausing it
                BreakpointHit tokenClientHit = await tokenClientBreakpoint.WaitForHit().TimeoutAfter(TestTimeout);

                // Sending second request, it is waiting for token refresh to finish
                HttpRequestMessage request2 = new HttpRequestMessage(HttpMethod.Get, "/vpn");
                task2 = client.SendAsync(request2);

                // Continue token refresh
                tokenClientHit.Continue();

                // First and second requests retried with new access token
                _innerHandler.Expect(HttpMethod.Get, "https://api.protonvpn.ch/vpn")
                    .WithHeaders("Authorization", "Bearer New access token")
                    .Respond(req => response);
                _innerHandler.Expect(HttpMethod.Get, "https://api.protonvpn.ch/vpn")
                    .WithHeaders("Authorization", "Bearer New access token")
                    .Respond(req => response);
                await requestBreakpoint.WaitForHitAndContinue().TimeoutAfter(TestTimeout);
                await requestBreakpoint.WaitForHitAndContinue().TimeoutAfter(TestTimeout);
            }
            finally
            {
                await task1.TimeoutAfter(TestTimeout);
                await task2.TimeoutAfter(TestTimeout);
            }

            // Assert
            await _tokenClient.Received(1).RefreshTokenAsync(Arg.Any<CancellationToken>());
            _innerHandler.VerifyNoOutstandingExpectation();
        }

        [TestMethod]
        public async Task SendAsync_ShouldRetryWithNewToken_WhenRefreshedWhileRequesting()
        {
            // Arrange
            BreakpointHandler breakpointHandler = new BreakpointHandler { InnerHandler = _innerHandler };
            Breakpoint requestBreakpoint = breakpointHandler.Breakpoint;
            BreakpointTokenClient breakpointTokenClient = new BreakpointTokenClient(_tokenClient);
            Breakpoint tokenClientBreakpoint = breakpointTokenClient.Breakpoint;
            UnauthorizedResponseHandler handler = new UnauthorizedResponseHandler(breakpointTokenClient, _tokenStorage, _userStorage, _logger) { InnerHandler = breakpointHandler };
            HttpClient client = new HttpClient(handler) { BaseAddress = _baseAddress };

            _tokenClient.RefreshTokenAsync(Arg.Any<CancellationToken>())
                .Returns(ApiResponseResult<RefreshTokenResponse>.Ok(
                    new RefreshTokenResponse { AccessToken = "New access token", RefreshToken = "New refresh token" }));

            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK);

            // Act
            Task task1 = Task.CompletedTask;
            Task task2 = Task.CompletedTask;
            try
            {
                // Sending first request and pausing it
                HttpRequestMessage request1 = new HttpRequestMessage(HttpMethod.Get, "/vpn");
                task1 = client.SendAsync(request1);
                BreakpointHit request1Hit = await requestBreakpoint.WaitForHit().TimeoutAfter(TestTimeout);

                // Sending second request and pausing it
                HttpRequestMessage request2 = new HttpRequestMessage(HttpMethod.Get, "/profiles");
                task2 = client.SendAsync(request2);
                BreakpointHit request2Hit = await requestBreakpoint.WaitForHit().TimeoutAfter(TestTimeout);

                // Continue first request and get Unauthorized
                _innerHandler.Expect(HttpMethod.Get, "https://api.protonvpn.ch/vpn")
                    .Respond(HttpStatusCode.Unauthorized);
                request1Hit.Continue();

                // First request initiated token refresh
                await tokenClientBreakpoint.WaitForHitAndContinue().TimeoutAfter(TestTimeout);

                // First request retried with new tokens
                request1Hit = await requestBreakpoint.WaitForHit().TimeoutAfter(TestTimeout);
                _innerHandler.Expect(HttpMethod.Get, "https://api.protonvpn.ch/vpn")
                    .WithHeaders("Authorization", "Bearer New access token")
                    .Respond(req => response);
                request1Hit.Continue();
                await task1.TimeoutAfter(TestTimeout);

                // Second request continues and gets Unauthorized
                _innerHandler.Expect(HttpMethod.Get, "https://api.protonvpn.ch/profiles")
                    .Respond(HttpStatusCode.Unauthorized);
                request2Hit.Continue();

                // Second request retried with new access token
                request2Hit = await requestBreakpoint.WaitForHit().TimeoutAfter(TestTimeout);
                _innerHandler.Expect(HttpMethod.Get, "https://api.protonvpn.ch/profiles")
                    .WithHeaders("Authorization", "Bearer New access token")
                    .Respond(req => response);
                request2Hit.Continue();
            }
            finally
            {
                await task1.TimeoutAfter(TestTimeout);
                await task2.TimeoutAfter(TestTimeout);
            }

            // Assert
            await _tokenClient.Received(1).RefreshTokenAsync(Arg.Any<CancellationToken>());
            _innerHandler.VerifyNoOutstandingExpectation();
        }

        #region Helpers

        private class BreakpointTokenClient : ITokenClient
        {
            private readonly ITokenClient _origin;
            public BreakpointTokenClient(ITokenClient origin)
            {
                _origin = origin;
                Breakpoint = new Breakpoint();
            }

            public Breakpoint Breakpoint { get; }

            public event EventHandler<ActionableFailureApiResultEventArgs> OnActionableFailureResult;

            public async Task<ApiResponseResult<RefreshTokenResponse>> RefreshTokenAsync(CancellationToken token)
            {
                await Breakpoint.Hit().WaitForContinue();
                return await _origin.RefreshTokenAsync(token);
            }
        }

        private class BreakpointHandler : DelegatingHandler
        {
            public BreakpointHandler()
            {
                Breakpoint = new Breakpoint();
            }

            public Breakpoint Breakpoint { get; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                await Breakpoint.Hit().WaitForContinue();
                return await base.SendAsync(request, cancellationToken);
            }

            protected override void Dispose(bool disposing)
            {
                Breakpoint.Dispose();
            }
        }

        #endregion
    }
}
