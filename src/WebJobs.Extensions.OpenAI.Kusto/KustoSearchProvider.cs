﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Data;
using System.Text;
using Kusto.Cloud.Platform.Data;
using Kusto.Data;
using Kusto.Data.Common;
using Kusto.Data.Net.Client;
using Kusto.Ingest;
using Microsoft.Azure.WebJobs.Extensions.OpenAI.Search;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Extensions.OpenAI.Kusto;

// IMPORTANT: This code assumes a particular table schema. A compliant table can be created using the following Kusto command:
// .create table Documents (Id:string, Title:string, Text:string, Embeddings:dynamic, Timestamp:datetime)

sealed class KustoSearchProvider : ISearchProvider, IDisposable
{
    // We create a separate client object for each connection string we see.
    readonly ConcurrentDictionary<string, (ICslQueryProvider, KustoConnectionStringBuilder)> kustoQueryClients = new();
    readonly ConcurrentDictionary<string, (IKustoIngestClient, KustoConnectionStringBuilder)> kustoIngestClients = new();
    readonly INameResolver nameResolver;
    readonly ILogger logger;

    public KustoSearchProvider(INameResolver nameResolver, ILoggerFactory loggerFactory)
    {
        // TODO: Use IConfiguration instead of INameResolver to get the Kusto connection string.
        //       Otherwise, we might have problems if users inject their own configuration source.
        this.nameResolver = nameResolver ?? throw new ArgumentNullException(nameof(nameResolver));

        if (loggerFactory == null)
        {
            throw new ArgumentNullException(nameof(loggerFactory));
        }

        this.logger = loggerFactory.CreateLogger<KustoSearchProvider>();
    }

    public void Dispose()
    {
        foreach ((ICslQueryProvider client, _) in this.kustoQueryClients.Values)
        {
            client.Dispose();
        }

        foreach ((IKustoIngestClient client, _) in this.kustoIngestClients.Values)
        {
            client.Dispose();
        }
    }

    public async Task AddDocumentAsync(SearchableDocument document, CancellationToken cancellationToken)
    {
        (IKustoIngestClient kustoIngestClient, KustoConnectionStringBuilder connectionStringBuilder) =
            this.kustoIngestClients.GetOrAdd(
                document.ConnectionInfo!.ConnectionName,
                name =>
                {
                    KustoConnectionStringBuilder connectionStringBuilder = this.GetKustoConnectionString(name);

                    // NOTE: There are some scalability limits with the ingest client. Some scenarios
                    //       may require a queued client.
                    IKustoIngestClient client = KustoIngestFactory.CreateDirectIngestClient(connectionStringBuilder);
                    return (client, connectionStringBuilder);
                });

        string databaseName = connectionStringBuilder.InitialCatalog;
        string tableName = document.ConnectionInfo.CollectionName;

        DataTable table = new(tableName);
        table.AppendColumn("Id", typeof(string));
        table.AppendColumn("Title", typeof(string));
        table.AppendColumn("Text", typeof(string));
        table.AppendColumn("Embeddings", typeof(object));
        table.AppendColumn("Timestamp", typeof(DateTime));

        for (int i = 0; i < document.Embeddings.Response.Value.Data.Count; i++)
        {
            table.Rows.Add(
                Guid.NewGuid().ToString("N"),
                Path.GetFileNameWithoutExtension(document.Title),
                document.Embeddings.Request.Input![i],
                GetEmbeddingsString(document.Embeddings.Response.Value.Data[i].Embedding),
                DateTime.UtcNow);
        }

        using IDataReader tableReader = table.CreateDataReader();
        await kustoIngestClient.IngestFromDataReaderAsync(
            tableReader,
            new KustoIngestionProperties(databaseName, tableName));
    }

    public Task<SearchResponse> SearchAsync(SearchRequest request)
    {
        (ICslQueryProvider kustoQueryClient, KustoConnectionStringBuilder connectionStringBuilder) =
            this.kustoQueryClients.GetOrAdd(
                request.ConnectionInfo.ConnectionName,
                name =>
                {
                    KustoConnectionStringBuilder connectionStringBuilder = this.GetKustoConnectionString(name);
                    ICslQueryProvider client = KustoClientFactory.CreateCslQueryProvider(connectionStringBuilder);
                    return (client, connectionStringBuilder);
                });

        // TODO: Use query parameters to remove the possibility of KQL-injection:
        // https://learn.microsoft.com/azure/data-explorer/kusto/query/queryparametersstatement
        // NOTE: Vector similarity reference:
        // https://techcommunity.microsoft.com/t5/azure-data-explorer-blog/azure-data-explorer-for-vector-similarity-search/ba-p/3819626
        string embeddingsList = GetEmbeddingsString(request.Embeddings);
        string? tableName = request.ConnectionInfo.CollectionName?.Trim();
        if (string.IsNullOrEmpty(tableName) ||
            tableName.Contains('/') ||
            tableName.Contains(';') ||
            tableName.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException($"The table name '{tableName}' is invalid.");
        }

        string query = $$"""
            let series_cosine_similarity_fl=(vec1:dynamic, vec2:dynamic, vec1_size:real=double(null), vec2_size:real=double(null))
            {
                let dp = series_dot_product(vec1, vec2);
                let v1l = iff(isnull(vec1_size), sqrt(series_dot_product(vec1, vec1)), vec1_size);
                let v2l = iff(isnull(vec2_size), sqrt(series_dot_product(vec2, vec2)), vec2_size);
                dp/(v1l*v2l)
            };
            let search_vector=pack_array({{embeddingsList}});
            {{tableName}}
            | extend similarity = series_cosine_similarity_fl(search_vector, Embeddings)
            | top {{request.MaxResults}} by similarity desc
            | project similarity, Id, Title, Text, Embeddings, Timestamp
            """;

        this.logger.LogDebug("Executing Kusto query: {query}", query);
        using IDataReader reader = kustoQueryClient.ExecuteQuery(
            databaseName: connectionStringBuilder.InitialCatalog,
            query,
            new ClientRequestProperties());

        List<SearchResult> results = new(capacity: request.MaxResults);
        while (reader.Read())
        {
            string subject = (string)reader["Title"];
            string text = (string)reader["Text"];
            results.Add(new SearchResult(subject, text));
        }

        this.logger.LogDebug("Kusto similarity query returned {count} results", results.Count);

        SearchResponse response = new(results);
        return Task.FromResult(response);
    }

    KustoConnectionStringBuilder GetKustoConnectionString(string connectionName)
    {
        string connectionString = this.nameResolver.Resolve(connectionName);
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException($"""
                No Kusto connection string named '{connectionName}' was found.
                It's required to be specified as an app setting or environment variable.
                """);
        }

        return new KustoConnectionStringBuilder(connectionString);
    }

    static string GetEmbeddingsString(ReadOnlyMemory<float> embedding)
    {
        ReadOnlySpan<float> span = embedding.Span;
        StringBuilder builder = new(span.Length * 10); // 9 is the longest string length of a float + 1 for comma

        for (int i = 0; i < span.Length; i++)
        {
            builder.Append(span[i]).Append(',');
        }
        builder.Remove(builder.Length - 1, 1);

        return builder.ToString();
    }
}