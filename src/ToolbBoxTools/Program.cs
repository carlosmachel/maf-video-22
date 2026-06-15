// Copyright (c) Microsoft. All rights reserved.

// Agente de Revisão de Código com Foundry Toolbox via MCP (Streamable HTTP).
//
// Cenário: Um agente que recebe um trecho de código C# como entrada e usa
// ferramentas de um Foundry Toolbox (expostas via MCP) para:
//   1. Analisar possíveis problemas de segurança
//   2. Sugerir melhorias de performance
//   3. Gerar um relatório final de revisão
//
// Diferenças em relação ao exemplo original:
//   - Cenário de domínio diferente: revisão de código em vez de pesquisa web
//   - Leitura do código-fonte a revisar a partir de um arquivo local
//   - Saída formatada como relatório estruturado em markdown
//   - Toolbox configurado com ferramentas de análise estática via MCP

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http.Headers;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Core;
using Azure.Identity;
using dotenv.net;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using OpenAI.Responses;

#pragma warning disable OPENAI001
#pragma warning disable AAIP001

DotEnv.Load();

// ---------------------------------------------------------------------------
// Configuração
// ---------------------------------------------------------------------------
const string toolboxName  = "code_review_toolbox";
const string codeFilePath = "sample.cs";   // arquivo de código a revisar

string endpoint       = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_AI_PROJECT_ENDPOINT não definido.");
string deploymentName = Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME")
    ?? "gpt-4.1-mini";

if (!File.Exists(codeFilePath))
    throw new FileNotFoundException($"Arquivo '{codeFilePath}' não encontrado.");

string codeToReview = await File.ReadAllTextAsync(codeFilePath);

TokenCredential credential = new DefaultAzureCredential();

// Cria (ou recria) o toolbox de revisão de código antes de conectar.
var toolboxEndpoint = await CreateCodeReviewToolboxAsync(toolboxName, endpoint, credential);

// ---------------------------------------------------------------------------
// 1. Conectar ao toolbox via MCP
// ---------------------------------------------------------------------------
Console.WriteLine($"[INFO] Conectando ao toolbox MCP: {toolboxEndpoint}");

using var httpClient = new HttpClient(
    new BearerTokenHandler(credential, "https://ai.azure.com/.default")
    {
        InnerHandler = new HttpClientHandler(),
    });

await using McpClient mcpClient = await McpClient.CreateAsync(
    new HttpClientTransport(
        new HttpClientTransportOptions
        {
            Endpoint      = new Uri(toolboxEndpoint),
            Name          = "code_review_toolbox",
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["Foundry-Features"] = "Toolboxes=V1Preview",
            },
        },
        httpClient));

IList<McpClientTool> mcpTools = await mcpClient.ListToolsAsync();
Console.WriteLine($"[INFO] Ferramentas disponíveis: {string.Join(", ", mcpTools.Select(t => t.Name))}");

// ---------------------------------------------------------------------------
// 2. Criar o agente com as ferramentas do toolbox
// ---------------------------------------------------------------------------
AIProjectClient aiProjectClient = new(new Uri(endpoint), credential);

const string SystemPrompt = """
    Você é um revisor de código sênior especializado em C# e .NET.
    Ao receber um trecho de código, você DEVE:
    1. Usar as ferramentas disponíveis para analisar segurança e performance.
    2. Consolidar os resultados em um relatório estruturado com as seções:
       ## Resumo, ## Problemas de Segurança, ## Sugestões de Performance, ## Próximos Passos.
    Seja direto e objetivo. Use markdown.
    """;

AIAgent agent = aiProjectClient.AsAIAgent(
    model: deploymentName,
    instructions: SystemPrompt,
    name: "CodeReviewAgent",
    tools: [.. mcpTools]);

// ---------------------------------------------------------------------------
// 3. Executar a revisão
// ---------------------------------------------------------------------------
string userPrompt = $"""
    Por favor, revise o código C# abaixo e gere um relatório completo.

    ```csharp
    {codeToReview}
    ```
    """;

Console.WriteLine("\n" + new string('─', 60));
Console.WriteLine("RELATÓRIO DE REVISÃO DE CÓDIGO");
Console.WriteLine(new string('─', 60) + "\n");

// RunAsync gerencia o loop de tool use internamente:
// o agente chama as ferramentas MCP quantas vezes precisar
// antes de retornar a resposta final.
var report = await agent.RunAsync(userPrompt);

Console.WriteLine(report.Text);
Console.WriteLine("\n" + new string('─', 60));
Console.WriteLine($"Revisão concluída em {DateTime.Now:HH:mm:ss}");

// ---------------------------------------------------------------------------
// Helper: cria (ou recria) o toolbox de revisão de código
// ---------------------------------------------------------------------------
static async Task<string> CreateCodeReviewToolboxAsync(
    string name, string endpoint, TokenCredential credential)
{
    // O header Foundry-Features é obrigatório para operações CRUD de toolbox.
    var options = new AgentAdministrationClientOptions();
    options.AddPolicy(new FoundryFeaturesPolicy("Toolboxes=V1Preview"), PipelinePosition.PerCall);

    var adminClient  = new AgentAdministrationClient(new Uri(endpoint), credential, options);
    var toolboxClient = adminClient.GetAgentToolboxes();

    // Remove toolbox anterior se existir (ignora 404).
    try
    {
        await toolboxClient.DeleteToolboxAsync(name);
        Console.WriteLine($"[INFO] Toolbox '{name}' anterior removido.");
    }
    catch (ClientResultException ex) when (ex.Status == 404)
    {
        // Ainda não existia — tudo bem.
    }

    // Configura o GitMCP apontando para o repositório oficial de analisadores
    // Roslyn do .NET — ferramentas de análise estática de código C# da Microsoft.
    // GitMCP expõe qualquer repositório público do GitHub como MCP server,
    // sem necessidade de infraestrutura própria.
    ProjectsAgentTool analysisTool = ProjectsAgentTool.AsProjectTool(ResponseTool.CreateMcpTool(
        serverLabel: "roslyn-analyzers",
        serverUri: new Uri("https://gitmcp.io/dotnet/roslyn-analyzers"),
        toolCallApprovalPolicy: new McpToolCallApprovalPolicy(
            GlobalMcpToolCallApprovalPolicy.NeverRequireApproval)));

    ToolboxVersion created = (await toolboxClient.CreateToolboxVersionAsync(
        name: name,
        tools: [analysisTool],
        description: "Toolbox de revisão de código C# via Roslyn Analyzers (GitMCP).")).Value;

    Console.WriteLine($"[INFO] Toolbox '{created.Name}' v{created.Version} criado ({created.Tools.Count} ferramenta(s)).");
    return $"{endpoint}/toolboxes/{created.Name}/mcp?api-version=v{created.Version}";
}

// ---------------------------------------------------------------------------
// Pipeline policy: adiciona o header Foundry-Features nas chamadas CRUD
// ---------------------------------------------------------------------------
internal sealed class FoundryFeaturesPolicy(string feature) : PipelinePolicy
{
    private const string FeatureHeader = "Foundry-Features";

    public override void Process(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        message.Request.Headers.Add(FeatureHeader, feature);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override ValueTask ProcessAsync(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        message.Request.Headers.Add(FeatureHeader, feature);
        return ProcessNextAsync(message, pipeline, currentIndex);
    }
}

// ---------------------------------------------------------------------------
// DelegatingHandler: injeta um bearer token renovado em cada requisição MCP
// ---------------------------------------------------------------------------
internal sealed class BearerTokenHandler(TokenCredential credential, string scope)
    : DelegatingHandler
{
    private readonly TokenRequestContext _ctx = new([scope]);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        AccessToken token = await credential
            .GetTokenAsync(_ctx, cancellationToken)
            .ConfigureAwait(false);

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token.Token);

        return await base.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
    }
}