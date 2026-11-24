using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;

namespace GovUk.OneLogin.AspNetCore.Tests;

public class CoreIdentityHelperTests
{
    private const string SampleDidJson = @"{
        ""id"": ""did:web:identity.integration.account.gov.uk"",
        ""assertionMethod"": [
            {
                ""id"": ""did:web:identity.integration.account.gov.uk#key-1"",
                ""type"": ""JsonWebKey"",
                ""controller"": ""did:web:identity.integration.account.gov.uk"",
                ""publicKeyJwk"": {
                    ""kty"": ""EC"",
                    ""crv"": ""P-256"",
                    ""x"": ""f83OJ3D2xFMTbKEGaAmAhEv86zdc9gmhuHnp2oMQ47L"",
                    ""y"": ""x_daQjjUz3WMGFLyZvnAoRgJEVIfqV30_87_J_svW18""
                }
            }
        ]
    }";

    [Fact]
    public async Task EnsureDidDocument_UsesHttpClientFactory_CanRefreshDidWithoutObjectDisposedException()
    {
        // Arrange
        var options = new OneLoginOptions
        {
            Environment = OneLoginEnvironments.Integration,
            ClientId = "test-client"
        };

        var handlerMock = new Mock<HttpMessageHandler>();
        var responseCount = 0;

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                responseCount++;
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SampleDidJson)
                };
                // Set max-age to 0 so it expires immediately to force refresh on next check
                response.Headers.CacheControl = new CacheControlHeaderValue { MaxAge = TimeSpan.FromMilliseconds(1) };
                return response;
            });

        var factoryMock = new Mock<IHttpClientFactory>();
        // Simulate disposing client or returning new client instances per call
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handlerMock.Object));

        var helper = new CoreIdentityHelper(factoryMock.Object, options, NullLogger<CoreIdentityHelper>.Instance);

        // Act 1: Initial load
        await helper.EnsureDidDocument();
        Assert.Equal(1, responseCount);

        // Simulate time passing past expiry
        await Task.Delay(20);

        // Act 2: Refresh load (in the buggy code, this threw ObjectDisposedException because the client was disposed)
        await helper.EnsureDidDocument();

        // Assert: Succeeded without ObjectDisposedException and fetched again
        Assert.Equal(2, responseCount);
    }

    [Fact]
    public async Task EnsureDidDocument_WhenRefreshFails_UsesCachedDocumentGracefully()
    {
        // Arrange
        var options = new OneLoginOptions
        {
            Environment = OneLoginEnvironments.Integration,
            ClientId = "test-client"
        };

        var handlerMock = new Mock<HttpMessageHandler>();
        var callCount = 0;

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(SampleDidJson)
                    };
                    response.Headers.CacheControl = new CacheControlHeaderValue { MaxAge = TimeSpan.FromMilliseconds(1) };
                    return response;
                }

                // Subsequent call fails with 500 Internal Server Error
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            });

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handlerMock.Object));

        var helper = new CoreIdentityHelper(factoryMock.Object, options, NullLogger<CoreIdentityHelper>.Instance);

        // Act 1: Initial load succeeds
        await helper.EnsureDidDocument();

        // Wait for max-age to elapse
        await Task.Delay(20);

        // Act 2: Refresh attempt fails, but should be caught and not bubble up
        var exception = await Record.ExceptionAsync(() => helper.EnsureDidDocument());

        // Assert: No exception thrown, cached DID document remains available
        Assert.Null(exception);
        Assert.Equal(2, callCount);
    }
}
