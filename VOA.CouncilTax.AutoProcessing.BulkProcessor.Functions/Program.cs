using System;
using System.Threading;
using Azure.Core;
using Azure.Identity;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using System.Net.Http.Headers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using VOA.CouncilTax.AutoProcessing.Constants;
using VOA.CouncilTax.AutoProcessing.Helpers;

var host = new HostBuilder()
	.ConfigureFunctionsWorkerDefaults()
	.ConfigureAppConfiguration((_, config) =>
	{
		config.AddJsonFile("host.json", optional: true, reloadOnChange: true);
	})
	.ConfigureLogging((context, logging) =>
	{
		logging.AddConfiguration(context.Configuration.GetSection("Logging"));
	})
	.ConfigureServices(services =>
	{
		services.AddMemoryCache();
		services.AddScoped<HttpLoggingHandler>();
		services.AddLogging();

		services.AddApplicationInsightsTelemetryWorkerService(options =>
		{
			options.ConnectionString = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
		});

		services.AddSingleton<ITelemetryInitializer, TelemetryInitializer>();

		services.Configure<TelemetryConfiguration>(config =>
		{
			config.ConnectionString = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
		});

		services.AddSingleton<IOrganizationServiceAsync2>(_ =>
		{
			var connectionString = Environment.GetEnvironmentVariable("d365Connectionstring");
			if (!string.IsNullOrWhiteSpace(connectionString))
			{
				return new ServiceClient(connectionString);
			}

			var environmentUrl = Environment.GetEnvironmentVariable("d365EnvironmentUrl");
			if (string.IsNullOrWhiteSpace(environmentUrl))
			{
				throw new InvalidOperationException("Set either 'd365Connectionstring' or 'd365EnvironmentUrl' environment variable.");
			}

			var userManagedIdentityId = Environment.GetEnvironmentVariable("UserAssignedManagedIdentityId");

			TokenCredential identity = string.IsNullOrWhiteSpace(userManagedIdentityId)
				? new DefaultAzureCredential()
				: new ChainedTokenCredential(
					new VisualStudioCredential(),
					new ManagedIdentityCredential(userManagedIdentityId));

			return new ServiceClient(
				tokenProviderFunction: async _ =>
				{
					var tokenResult = await identity.GetTokenAsync(
						new TokenRequestContext(new[] { $"{environmentUrl}/.default" }),
						CancellationToken.None);
					return tokenResult.Token;
				},
				instanceUrl: new Uri(environmentUrl));
		});

		services
			.AddHttpClient(ConfigurationValues.DataAccessLayerPropertyAppName, (sp, httpClient) =>
			{
				ConfigureDataAccessClient(
					serviceProvider: sp,
					httpClient: httpClient,
					baseUrlEnvVar: "DataAccessLayerPropertyAppBaseUrl",
					scopeEnvVar: "DataAccessLayerScope",
					tokenCacheKey: "dataAccessLayerToken");
			})
			.AddHttpMessageHandler<HttpLoggingHandler>();

		services
			.AddHttpClient(ConfigurationValues.DataAccessLayerAssessmentAppName, (sp, httpClient) =>
			{
				ConfigureDataAccessClient(
					serviceProvider: sp,
					httpClient: httpClient,
					baseUrlEnvVar: "DataAccessLayerAssessmentAppBaseUrl",
					scopeEnvVar: "DataAccessLayerScopeForAssessmentApp",
					tokenCacheKey: "dataAccessLayerTokenAssessment");
			})
			.AddHttpMessageHandler<HttpLoggingHandler>();
	})
	.Build();

host.Run();

static void ConfigureDataAccessClient(
	IServiceProvider serviceProvider,
	HttpClient httpClient,
	string baseUrlEnvVar,
	string scopeEnvVar,
	string tokenCacheKey)
{
	var baseUrl = Environment.GetEnvironmentVariable(baseUrlEnvVar);
	var apimKey = Environment.GetEnvironmentVariable("DataAccessLayerAPIMKey");
	var token = GetOrCreateToken(serviceProvider, scopeEnvVar, tokenCacheKey);

	if (!string.IsNullOrWhiteSpace(baseUrl))
	{
		httpClient.BaseAddress = new Uri(baseUrl);
	}

	if (!string.IsNullOrWhiteSpace(apimKey))
	{
		httpClient.DefaultRequestHeaders.Remove("Ocp-Apim-Subscription-Key");
		httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apimKey);
	}

	if (!string.IsNullOrWhiteSpace(token))
	{
		httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
	}
}

static string GetOrCreateToken(IServiceProvider serviceProvider, string scopeEnvVar, string tokenCacheKey)
{
	var tokenCache = serviceProvider.GetRequiredService<IMemoryCache>();

	return tokenCache.GetOrCreate(tokenCacheKey, entry =>
	{
		// Buffer the cache window so we refresh before token expiry.
		entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(50);

		var scope = Environment.GetEnvironmentVariable(scopeEnvVar);
		if (string.IsNullOrWhiteSpace(scope))
		{
			return string.Empty;
		}

		return Authentication.GenerateUmiAuthentication(scope).GetAwaiter().GetResult();
	}) ?? string.Empty;
}

public sealed class TelemetryInitializer : ITelemetryInitializer
{
	public void Initialize(ITelemetry telemetry)
	{
		telemetry.Context.Cloud.RoleName = "BST.Dyn365.Functions";
	}
}
