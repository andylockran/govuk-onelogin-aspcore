using System.Security.Claims;
using Xunit;

namespace GovUk.OneLogin.AspNetCore.Tests;

public class ClaimsPrincipalExtensionsTests
{
    private const string SampleVcJson = @"{
        ""credentialSubject"": {
            ""name"": [
                {
                    ""validFrom"": ""2020-01-01"",
                    ""validUntil"": ""2022-01-01"",
                    ""nameParts"": [
                        { ""type"": ""GivenName"", ""value"": ""Jane"" },
                        { ""type"": ""FamilyName"", ""value"": ""Oldname"" }
                    ]
                },
                {
                    ""validFrom"": ""2022-01-01"",
                    ""nameParts"": [
                        { ""type"": ""GivenName"", ""value"": ""Jane"" },
                        { ""type"": ""FamilyName"", ""value"": ""Doe"" }
                    ]
                }
            ],
            ""birthDate"": [
                { ""value"": ""1990-05-15"" }
            ]
        }
    }";

    [Fact]
    public void GetCoreIdentityName_ReturnsCurrentActiveName()
    {
        // Arrange
        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim("vc", SampleVcJson, "JSON"));
        var principal = new ClaimsPrincipal(identity);

        // Act
        var currentName = principal.GetCoreIdentityName();

        // Assert: Name without validUntil (active name) should be returned
        Assert.NotNull(currentName);
        Assert.Equal("Jane Doe", currentName.FullName);
        Assert.Null(currentName.ValidUntil);
        Assert.Equal(new DateOnly(2022, 1, 1), currentName.ValidFrom);
    }

    [Fact]
    public void GetCoreIdentityBirthDate_ReturnsBirthDate()
    {
        // Arrange
        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim("vc", SampleVcJson, "JSON"));
        var principal = new ClaimsPrincipal(identity);

        // Act
        var birthDate = principal.GetCoreIdentityBirthDate();

        // Assert
        Assert.NotNull(birthDate);
        Assert.Equal(new DateOnly(1990, 5, 15), birthDate.Value);
        Assert.Equal("1990-05-15", birthDate.ToString());
    }
}
