# ToolbBoxTools — Code Review Agent with Azure AI Foundry Toolbox via MCP

A .NET 10 console application that demonstrates how to build an AI code review agent using **Azure AI Foundry**, **Microsoft Agents SDK**, and **Model Context Protocol (MCP)**. The agent connects to a Foundry Toolbox backed by the Roslyn Analyzers repository (via GitMCP), then generates a structured markdown report for a given C# file.

## How it works

1. Creates (or recreates) a Foundry Toolbox named `code_review_toolbox` with the Roslyn Analyzers MCP server.
2. Connects to that toolbox via Streamable HTTP.
3. Spins up an `AIAgent` with the available MCP tools and a senior code reviewer system prompt.
4. Feeds the contents of `sample.cs` to the agent and prints the report to the console.

## Prerequisites

| Requirement | Version |
|---|---|
| .NET SDK | 10.0+ |
| Azure Subscription | — |
| Azure AI Foundry project | — |
| Azure CLI / logged-in identity | for `DefaultAzureCredential` |

## Getting started

### 1. Clone the repository

```bash
git clone <repo-url>
cd maf-video-22
```

### 2. Configure environment variables

Copy the sample file and fill in your values:

```bash
cp src/ToolbBoxTools/.env.sample src/ToolbBoxTools/.env
```

Edit `.env`:

```env
AZURE_AI_PROJECT_ENDPOINT=https://<your-project>.services.ai.azure.com/api/projects/<project-name>
AZURE_AI_MODEL_DEPLOYMENT_NAME=gpt-4.1-mini
```

> `AZURE_AI_MODEL_DEPLOYMENT_NAME` defaults to `gpt-4.1-mini` if omitted.

### 3. Authenticate with Azure

The app uses `DefaultAzureCredential`. The easiest way locally is:

```bash
az login
```

### 4. Run

```bash
dotnet run --project src/ToolbBoxTools
```

The agent will print a structured code review report to stdout.

### Changing the file under review

By default the agent reviews `sample.cs` (located next to the binary). To review a different file, update the `codeFilePath` constant in [src/ToolbBoxTools/Program.cs](src/ToolbBoxTools/Program.cs#L38):

```csharp
const string codeFilePath = "sample.cs";
```

## NuGet packages

| Package | Version | Purpose |
|---|---|---|
| `Azure.AI.Projects` | 2.1.0-beta.3 | Azure AI Foundry project client and agent administration |
| `Microsoft.Agents.AI` | 1.9.0 | Core `AIAgent` abstraction and runner |
| `Microsoft.Agents.AI.Foundry` | 1.9.0-preview.260603.1 | Foundry-specific extensions (`AsAIAgent`, toolbox CRUD) |
| `ModelContextProtocol` | 1.4.0 | MCP client (`McpClient`, `HttpClientTransport`) |
| `dotenv.net` | 4.0.2 | Loads `.env` files into environment variables |

## Project structure

```
maf-video-22/
├── src/
│   └── ToolbBoxTools/
│       ├── Program.cs          # Entry point — agent setup and execution
│       ├── sample.cs           # Sample C# file with intentional bugs (reviewed by the agent)
│       ├── .env                # Local environment variables (not committed)
│       └── .env.sample         # Template for environment variables
└── maf-video-22.sln
```

## Contributing

1. Fork the repository and create a feature branch.
2. Make your changes and ensure the project builds:
   ```bash
   dotnet build src/ToolbBoxTools
   ```
3. Run the app end-to-end at least once against a real Azure AI Foundry project to confirm it works.
4. Open a pull request describing what changed and why.

## License

See [LICENSE](LICENSE).
