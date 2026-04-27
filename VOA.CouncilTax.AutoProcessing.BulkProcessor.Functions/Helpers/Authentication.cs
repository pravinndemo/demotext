using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.Identity.Client;

namespace VOA.CouncilTax.AutoProcessing.Helpers;

internal static class Authentication
{
    public static Task<AuthenticationResult> GenerateAuthentication()
    {
        return GenerateClientCredentialAuthentication("DataAccessLayerScope");
    }

    public static Task<AuthenticationResult> GenerateAuthenticationForAssessmentApp()
    {
        return GenerateClientCredentialAuthentication("DataAccessLayerScopeForAssessmentApp");
    }

    public static async Task<string> GenerateUmiAuthentication(string scope)
    {
        var userAssignedClientId = Environment.GetEnvironmentVariable("UserManagedIdentityClientId");

        var credential = string.IsNullOrWhiteSpace(userAssignedClientId)
            ? new DefaultAzureCredential()
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = userAssignedClientId,
            });

        var accessToken = await credential.GetTokenAsync(
            new TokenRequestContext(new[] { scope }),
            CancellationToken.None);

        return accessToken.Token;
    }

    private static async Task<AuthenticationResult> GenerateClientCredentialAuthentication(string scopeEnvironmentVariable)
    {
        var tenantId = Environment.GetEnvironmentVariable("DataAccessLayerTenantId");
        var clientId = Environment.GetEnvironmentVariable("DataAccessLayerClientId");
        var clientSecret = Environment.GetEnvironmentVariable("DataAccessLayerClientSecret");
        var scope = Environment.GetEnvironmentVariable(scopeEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret) ||
            string.IsNullOrWhiteSpace(scope))
        {
            throw new InvalidOperationException(
                $"Missing one or more auth environment variables for '{scopeEnvironmentVariable}'.");
        }

        var authority = $"https://login.microsoftonline.com/{tenantId}";
        var app = ConfidentialClientApplicationBuilder
            .Create(clientId)
            .WithClientSecret(clientSecret)
            .WithAuthority(authority)
            .Build();

        return await app
            .AcquireTokenForClient(new[] { scope })
            .ExecuteAsync()
            .ConfigureAwait(false);
    }
}
