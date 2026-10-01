// Coach deploy tool: publishes Swankers.Coach, uploads it to the shared Microsoft Foundry
// project as a hosted agent version (code bundle, no container image), waits for it to go
// active, and routes the agent endpoint at a chosen version. Thursday's rollback is `route`
// back to the previous version. Runs under the maintainer's `az login` identity.
//
//   publish   [--output <dir>]                                  dotnet publish Swankers.Coach
//   create    --label v1-owner --prompt v1 --credential-key Mcp:OwnerCredential
//             [--publish-dir <dir>] [--route] [--cpu 1] [--memory 2Gi] [--timeout-minutes 15]
//   route     --version <n>
//   delete    --version <n>                                     remove a version (never the routed one)
//   list
//   identity                                                    agent identity principal id
//
// Common options, or the settings in brackets from the process environment or the selected
// azd environment (read with `azd env get-values`, so a fresh shell works after azd up):
//   --name Coach  --project-endpoint [FOUNDRY_PROJECT_ENDPOINT]  --vault-uri [KEYVAULT_URI]
//   --mcp-endpoint [MCP_ENDPOINT]  --model [AZURE_AI_MODEL_DEPLOYMENT_NAME]
using Swankers.AgentDeploy;

var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var ct = cancellation.Token;

try
{
    var repoRoot = CoachPublisher.FindRepoRoot();
    var line = CommandLine.Parse(args, AzdEnvironment.Load(repoRoot));
    var agentName = line.Optional("name") ?? "Coach";
    var defaultPublishDir = CoachPublisher.DefaultOutputDirectory;

    switch (line.Command)
    {
        case "publish":
        {
            var output = line.Optional("output") ?? defaultPublishDir;
            await CoachPublisher.PublishAsync(repoRoot, output, ct);
            Console.WriteLine($"published={output}");
            return 0;
        }

        case "create":
        {
            var spec = new CoachVersionSpec(
                Label: line.Required("label"),
                PromptVersion: line.Optional("prompt") ?? "v1",
                McpCredentialKey: line.Optional("credential-key") ?? "Mcp:OwnerCredential",
                McpEndpoint: line.Required("mcp-endpoint", "MCP_ENDPOINT"),
                KeyVaultUri: line.Required("vault-uri", "KEYVAULT_URI"),
                ProjectEndpoint: line.Required("project-endpoint", "FOUNDRY_PROJECT_ENDPOINT"),
                ModelDeployment: line.Optional("model", "AZURE_AI_MODEL_DEPLOYMENT_NAME") ?? "gpt-5.4",
                Cpu: line.Optional("cpu") ?? "1",
                Memory: line.Optional("memory") ?? "2Gi");

            var publishDir = line.Optional("publish-dir");
            if (publishDir is null)
            {
                publishDir = defaultPublishDir;
                await CoachPublisher.PublishAsync(repoRoot, publishDir, ct);
            }
            else
            {
                CoachPublisher.Verify(publishDir);
            }

            var deployer = new AgentDeployer(spec.ProjectEndpoint, agentName);
            Console.WriteLine($"Uploading {publishDir} as {agentName} ({spec.Description})...");
            var created = await deployer.CreateVersionAsync(spec, publishDir, ct);
            Console.WriteLine($"Created {agentName} v{created.Version}; waiting for it to go active.");

            var timeout = TimeSpan.FromMinutes(int.Parse(line.Optional("timeout-minutes") ?? "15"));
            await deployer.WaitForActiveAsync(created.Version, timeout, Console.WriteLine, ct);

            if (line.Flag("route"))
            {
                await deployer.RouteAsync(created.Version, ct);
                Console.WriteLine($"Endpoint now routes 100% to v{created.Version}.");
            }

            Console.WriteLine($"version={created.Version}");
            return 0;
        }

        case "route":
        {
            // --version <n> or --label <stage>; the label resolves to the newest version of that stage.
            var deployer = new AgentDeployer(line.Required("project-endpoint", "FOUNDRY_PROJECT_ENDPOINT"), agentName);
            var label = line.Optional("label");
            var version = line.Optional("version")
                ?? (label is not null
                    ? AgentDeployer.ResolveLabel((await deployer.ListVersionsAsync(ct)).Select(AgentDeployer.ToStageVersion), label)
                    : throw new InvalidOperationException("route needs --version <n> or --label <stage> (v1-owner, v2-owner, v0-commissioner, v0-owner)."));
            await deployer.RouteAsync(version, ct);
            Console.WriteLine($"Endpoint now routes 100% to {agentName} v{version}{(label is null ? "" : $" ({label})")}.");
            Console.WriteLine($"version={version}");
            return 0;
        }

        case "delete":
        {
            var version = line.Required("version");
            var deployer = new AgentDeployer(line.Required("project-endpoint", "FOUNDRY_PROJECT_ENDPOINT"), agentName);
            var routed = AgentDeployer.RoutedVersion(await deployer.GetAgentAsync(ct));
            if (routed == version)
            {
                throw new InvalidOperationException($"v{version} is the routed version; route elsewhere first.");
            }

            await deployer.DeleteVersionAsync(version, ct);
            Console.WriteLine($"Deleted {agentName} v{version}.");
            return 0;
        }

        case "list":
        {
            var deployer = new AgentDeployer(line.Required("project-endpoint", "FOUNDRY_PROJECT_ENDPOINT"), agentName);
            var agent = await deployer.GetAgentAsync(ct);
            var routed = AgentDeployer.RoutedVersion(agent);
            Console.WriteLine($"{agent.Name}: state {agent.State}, routed to {(routed is null ? "(nothing)" : "v" + routed)}");
            foreach (var version in await deployer.ListVersionsAsync(ct))
            {
                var stage = version.Metadata.TryGetValue("stage", out var s) ? s : "-";
                var marker = version.Version == routed ? "*" : " ";
                Console.WriteLine($"{marker} v{version.Version,-4} {version.Status,-9} {stage,-16} {version.Description}");
            }

            return 0;
        }

        case "identity":
        {
            var deployer = new AgentDeployer(line.Required("project-endpoint", "FOUNDRY_PROJECT_ENDPOINT"), agentName);
            var agent = await deployer.GetAgentAsync(ct);
            var principalId = agent.InstanceIdentity?.PrincipalId;
            if (string.IsNullOrEmpty(principalId))
            {
                throw new InvalidOperationException($"{agentName} has no instance identity yet; create a version first.");
            }

            Console.WriteLine($"{agentName} identity: principal {principalId}, client {agent.InstanceIdentity!.ClientId}, status {agent.InstanceIdentity.Status}");
            Console.WriteLine($"principalId={principalId}");
            return 0;
        }

        default:
            Console.Error.WriteLine("Usage: publish | create | route | delete | list | identity (see the header of Program.cs).");
            return 2;
    }
}
catch (OperationCanceledException)
{
    return 130;
}
catch (Exception ex)
{
    // Message only: never echo configuration values or response bodies.
    Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
    return 1;
}
