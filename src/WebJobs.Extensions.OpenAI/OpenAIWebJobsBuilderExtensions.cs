﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure;
using Microsoft.Azure.WebJobs.Extensions.OpenAI.Agents;
using Microsoft.Azure.WebJobs.Extensions.OpenAI.Search;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using OpenAI.Extensions;

namespace Microsoft.Azure.WebJobs.Extensions.OpenAI;

/// <summary>
/// Extension methods for registering the OpenAI webjobs extension.
/// </summary>
public static class OpenAIWebJobsBuilderExtensions
{
    /// <summary>
    /// Registers OpenAI bindings with the WebJobs host.
    /// </summary>
    /// <param name="builder">The WebJobs builder.</param>
    /// <returns>Returns the <paramref name="builder"/> reference to support fluent-style configuration.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="builder"/> is <c>null</c>.</exception>
    public static IWebJobsBuilder AddOpenAIBindings(this IWebJobsBuilder builder)
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        // Register the Azure Open AI Client
        builder.Services.AddAzureClients(clientBuilder =>
        {
            // Use Azure OpenAI configuration
            string key = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
            var createUri = Uri.TryCreate(Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT"), UriKind.Absolute, out var uri);
            if (string.IsNullOrEmpty(key) || !createUri)
            {
                throw new InvalidOperationException("Must set AZURE_OPENAI_KEY and AZURE_OPENAI_ENDPOINT environment variables. Visit <insert troubleshooting link> for more.");
            }

            // ToDo: Check how to add support for non-Azure OpenAI Client through dependency injection and update above exception message.

            // ToDo: Add support for Azure Managed Identity
            clientBuilder.AddOpenAIClient(uri, new AzureKeyCredential(key));
        });

        // ToDo: Remove below registration after migration of all converters to use Azure OpenAI Client
        // Register the OpenAI service, which we depend on.
        builder.Services.AddOpenAIService(settings =>
        {
            // Common configuration.
            // The Betalgo SDK has a default API version, but we support overriding it using an environment variable.
            string? apiVersion = Environment.GetEnvironmentVariable("OPENAI_API_VERSION");
            if (!string.IsNullOrEmpty(apiVersion))
            {
                settings.ApiVersion = apiVersion;
            }

            // Try Azure connection first, which is preferred for privacy
            string? azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            if (!string.IsNullOrEmpty(azureOpenAIEndpoint))
            {
                // Azure OpenAI configuration
                settings.ApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY")!;
                settings.ProviderType = ProviderType.Azure;
                settings.BaseDomain = azureOpenAIEndpoint;
                settings.DeploymentId = "placeholder"; // dummy value - this will be replaced at runtime
            }
            else
            {
                // Public OpenAI configuration
                settings.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                settings.Organization = Environment.GetEnvironmentVariable("OPENAI_ORGANIZATION_ID");
            }
            if (string.IsNullOrEmpty(settings.ApiKey))
            {
                throw new InvalidOperationException("Must set OPENAI_API_KEY or AZURE_OPENAI_KEY environment variable.");
            }
        });

        // Register the WebJobs extension, which enables the bindings.
        builder.AddExtension<OpenAIExtension>();

        // Service objects that will be used by the extension
        builder.Services.AddSingleton<IOpenAIServiceProvider, DefaultOpenAIServiceProvider>();
        builder.Services.AddSingleton<TextCompletionConverter>();
        builder.Services.AddSingleton<EmbeddingsConverter>();
        builder.Services.AddSingleton<SemanticSearchConverter>();
        builder.Services.AddSingleton<ChatBotBindingConverter>();
        builder.Services.AddSingleton<IChatBotService, DefaultChatBotService>();

        return builder;
    }
}
