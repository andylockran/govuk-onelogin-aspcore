using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Moq;
using Moq.Protected;
using Xunit;

namespace GovUk.OneLogin.AspNetCore.Tests;

public class OneLoginOptionsTests
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
    public async Task OnTokenResponseReceived_WithoutCoreIdentityClaim_DoesNotInitializeCoreIdentityHelper()
    {
        // Arrange: Authentication-only configuration (no CoreIdentity claim)
        var options = new OneLoginOptions
        {
            Environment = OneLoginEnvironments.Integration,
            ClientId = "test-client",
            SignInScheme = "Cookies"
        };
        options.Claims.Clear(); // Ensure CoreIdentity is not requested

        var mockHttpHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        // Notice: mockHttpHandler has no setup, so if ANY HTTP call is made, it will throw!

        var services = new ServiceCollection();
        services.AddLogging();
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(mockHttpHandler.Object));
        services.AddSingleton(mockHttpClientFactory.Object);

        var serviceProvider = services.BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };

        var scheme = new AuthenticationScheme(OneLoginDefaults.AuthenticationScheme, OneLoginDefaults.DisplayName, typeof(OneLoginHandler));
        var authProperties = new AuthenticationProperties();
        var user = new ClaimsPrincipal(new ClaimsIdentity());
        var message = new OpenIdConnectMessage { IdToken = "fake-id-token" };
        var context = new TokenResponseReceivedContext(httpContext, scheme, options.OpenIdConnectOptions, user, authProperties)
        {
            TokenEndpointResponse = message
        };

        // Act: Callback receives token
        await options.OnTokenResponseReceived(context);

        // Assert: For authentication-only, CoreIdentityHelper should NOT be accessed or initialized,
        // and no HTTP requests should have been made to the DID endpoint.
        mockHttpHandler.Protected().Verify("SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task OnTokenResponseReceived_WithCoreIdentityClaim_InitializesCoreIdentityHelperAndFetchesDid()
    {
        // Arrange: Identity verification configuration
        var options = new OneLoginOptions
        {
            Environment = OneLoginEnvironments.Integration,
            ClientId = "test-client",
            SignInScheme = "Cookies"
        };
        options.Claims.Add(OneLoginClaimTypes.CoreIdentity);

        var mockHttpHandler = new Mock<HttpMessageHandler>();
        mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains(".well-known/did.json")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(SampleDidJson)
            });

        var services = new ServiceCollection();
        services.AddLogging();
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(mockHttpHandler.Object));
        services.AddSingleton(mockHttpClientFactory.Object);

        var serviceProvider = services.BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };

        var scheme = new AuthenticationScheme(OneLoginDefaults.AuthenticationScheme, OneLoginDefaults.DisplayName, typeof(OneLoginHandler));
        var authProperties = new AuthenticationProperties();
        var user = new ClaimsPrincipal(new ClaimsIdentity());
        var message = new OpenIdConnectMessage { IdToken = "fake-id-token" };
        var context = new TokenResponseReceivedContext(httpContext, scheme, options.OpenIdConnectOptions, user, authProperties)
        {
            TokenEndpointResponse = message
        };

        // Act
        await options.OnTokenResponseReceived(context);

        // Assert: HTTP request was made to fetch DID
        mockHttpHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains(".well-known/did.json")),
            ItExpr.IsAny<CancellationToken>());
    }
}
