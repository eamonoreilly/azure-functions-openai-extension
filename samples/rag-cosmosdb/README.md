# Semantic Search Demo

## Prerequisites

The sample is available in the following language stacks:

- [C# on the isolated worker](csharp-ooproc/)
- [TypeScript](typescript/)
- [JavaScript](javascript/)
- [PowerShell](powershell/)
- [Java](java/)
- [Python](python/)

Please refer to the [root README](../../README.md#requirements) for common prerequisites that apply to all samples.

## Running the sample

This sample requires creating an Azure Cosmos DB for MongoDB vCore. You can do this by following the [Cosmos DB for MongoDB vCore Quickstart](https://learn.microsoft.com/azure/cosmos-db/mongodb/vcore/quickstart-portal).

Once you have an Cosmos DB resource, you can run the sample by following these steps:

1. Update the `CosmosDBMongoVCoreConnectionString` value in `local.settings.json` to match your connection string from the Cosmos DB resource. You may obtain the connection string by following [these instructions](https://learn.microsoft.com/azure/cosmos-db/mongodb/vcore/quickstart-portal#get-cluster-credentials).
1. Always configure the search provider type in the `host.json` as shown in below snippet.
1. Use of Vector Search Dimensions and the number of clusers that the inverted file (IVF) index uses to group the vector data are configurable. You may configure the `host.json` file within the project and following example shows the default values:

   ```json
   "extensions": {
       "openai": {
           "searchProvider": {
               "type": "cosmosDBSearch",
               "applicationName": "functionsAppName",
               "textKey": "text",
               "embeddingKey": "embedding",
               "vectorSearchDimensions": 1536,
               "numLists":  1,
               "kind": "vector-hnsw",
               "similarity": "COS",
               "numberOfConnections": 16,
               "efConstruction": 64,
               "efSearch": 40
           }
       }
   }
   ```

   `ApplicationName` is the name of the user agent to track in diagnostics and telemetry.

   `TextKey` is the name of the field property which will contain the text which is embedded.

   `EmbeddingKey` is the name of the field property which will contain the embeddings

   `VectorSearchDimensions` is length of the embedding vector. [The dimensions attribute has a minimum of 2 and a maximum of 2000 floating point values each](https://learn.microsoft.com/azure/cosmos-db/mongodb/vcore/vector-search#create-an-vector-index-using-ivf). By default, the length of the embedding vector will be 1536 for text-embedding-ada-002.

   `NumLists` is the number of clusters that the inverted file (IVF) uses to group the vector data, as mentioned [here](https://learn.microsoft.com/azure/cosmos-db/mongodb/vcore/vector-search#create-an-vector-index-using-ivf). By default, the number of clusters will be 1.

   `Kind` is the Type of vector index to create. The options are vector-ivf, vector-hnsw and vector-diskann. Note vector-ivf is available on all cluster tiers and vector-hnsw/vector-diskann is available on M40 cluster tiers and higher.

   `Similarity` is the Similarity metric to use with the index. Possible options are COS (cosine distance), L2 (Euclidean distance), and IP (inner product).

   `NumberOfConnections` is the max number of connections per layer (16 by default, minimum value is 2, maximum value is 100). Higher m is suitable for datasets with high dimensionality and/or high accuracy requirements.

   `EfConstruction` is the size of the dynamic candidate list for constructing the graph (64 by default, minimum value is 4, maximum value is 1000). Higher efConstruction will result in better index quality and higher accuracy, but it will also increase the time required to build the index. efConstruction has to be at least 2 \* m

   `EfSearch` The size of the dynamic candidate list for search (40 by default). A higher value provides better recall at the cost of speed.

   `MaxDegree` Maximum number of edges per node in the graph. This parameter ranges from 20 to 2048 (default is 32). Higher maxDegree is suitable for datasets with high dimensionality and/or high accuracy requirements.

   `LBuild` Sets the number of candidate neighbors evaluated during DiskANN index construction. This parameter, which ranges from 10 to 500 (default is 50), balances accuracy and computational overhead: higher values improve index quality and accuracy but increase build time

   `LSearch` Specifies the size of the dynamic candidate list for search. The default value is 40, with a configurable range from 10 to 1000. Increasing the value enhances recall but may reduce search speed.

1. Use a terminal window to navigate to the sample directory

   ```sh
   cd samples/rag-cosmosdb/<language-stack>
   ```

1. If using the extensions.csproj with non-dotnet languages and refer the extension project

   ```sh
   dotnet build --output bin
   ```

1. If using python, run `pip install -r requirements.txt` to install the correct library version.
1. Build and start the app

   ```sh
   func start
   ```

1. Refer the [demo.http](demo.http) file for the format of requests.

### Vector Search Configuration

Mongo vCore has two vector index options:

- vector-ivf (default value) : available on all cluster tiers.
- vector-hnsw : available on M40 cluster tier and higher.
- vector-diskann : available on M40 cluster tier and higher.

Please read more about Vector Search in CosmosDB Mongo vCore [here](https://learn.microsoft.com/en-us/azure/cosmos-db/mongodb/vcore/vector-search).
