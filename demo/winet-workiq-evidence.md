# WI.NET Work IQ evidence (W1)

Evidence for the W1 gate in [docs/BUILD-PLAN.md](../docs/BUILD-PLAN.md): real Work IQ retrieval through the application's own C# path. Recorded 2026-10-05.

The maintainer ran every live command. This record was written afterwards from the captures those runs wrote (then under the ignored folder `artifacts/workplace/live/`; since moved out of the checkout, see the end), from the local CLI's `version`, `config show`, and `--help` output, and from the maintainer's report of the test results. Where a fact rests on the maintainer's report rather than on a capture, it says so.

No tenant id, domain, account, mail content, message id, or link is recorded here. The corpus is the fictional one in [data/workplace/corpus-seed.md](../data/workplace/corpus-seed.md); the captures also hold the sender's real display name and address, which is one reason they stay out of the repository.

## Result

| W1 gate criterion | Status | Basis |
|---|---|---|
| An application-side C# path, not the CLI, invokes real Work IQ under the maintainer's delegated identity | Proven | `WorkIqLiveSmokeTests.Reads_the_tagged_corpus_with_references_and_a_span` passed (maintainer's report). The captures that test writes exist with matching times: raw result and mapped evidence at 20:08:22 UTC |
| It returns the expected tagged corpus items | Proven | 9 of 9 items, every subject tagged `[NW-DPM]`, in the same order as the seed's send order (checked against the mapped-evidence capture) |
| Evidence items carry the source references exactly as returned | Proven | Asserted by the test; rechecked from the captures: each of the 9 evidence references is a message `id` in the raw result and each link is a returned `webLink`. Running the current mapper over the captured raw result offline gives the same 9 references |
| The tool-call span is observable with scenario, mode `live`, tool name, and item count, and no content or token | Proven in the test, not yet viewed in a dashboard | The test's `ActivityListener` asserted one `workiq.tool fetch` span on `Swankers.Coach` with scenario `workplace`, mode `live`, and the item count. That the span carries no content is asserted by the model-free provider tests, which share the same code path. The span's trace id and duration were printed to the console and not kept. Nobody has looked at a live span in the Aspire dashboard yet |

On 2026-10-05 the maintainer amended the gate so that these four proofs pass it: **W1 passed.** Viewing a live span in the Aspire dashboard became a presentation-readiness item for W5 and W6.

Still open from W1: the maintainer's review of this file and of the allow-list before any Work IQ result reaches a model.

## Environment

- Windows 11 on ARM64, Node v26.7.0.
- Work IQ CLI `1.0.1.47154`, from `npm install -g @microsoft/workiq@1.0.1`.
- `ModelContextProtocol` 2.2.0 as pinned in `Directory.Packages.props`; no package was added.
- Code: branch `feat/workplace-meeting-prep`, uncommitted, on top of `92ab4bc`.
- An account in the maintainer's own tenant, not the corporate one.

## Configuration that worked

1. `npm install -g @microsoft/workiq@1.0.1`, then `workiq accept-eula`.
2. `workiq config set disableBrokeredAuth=true`. Without it, `workiq auth login` fails in the Windows broker before any prompt ("WAM Error, 2156265475, ApiContractViolation"; [work-iq issue 118](https://github.com/microsoft/work-iq/issues/118)). `workiq config show` on this machine now reports `disableBrokeredAuth=true`, a default account, and the EULA accepted.
3. Tenant enablement. The first browser sign-in failed with `AADSTS650052` (no service principal for "Agent Tools", app id `ea9ffc3e-8a23-4a7d-836d-234d7c7565c1`). The maintainer resolved it; which steps were taken, and which licensing path applies in that tenant, were not reported and are not recorded here.
4. `workiq auth login`.
5. Nine emails sent from the account to itself, with the subjects and bodies in the seed file, in its order.

This build has no tenant option: Learn documents `-t, --tenant-id`, and `workiq ask -t ...` is rejected. The tenant follows the signed-in account, and `--account <email>` selects it on any command, including `mcp`.

## Commands the maintainer ran

Tool discovery (reported as passed; its captures are at 19:57:30 UTC):

```powershell
$env:WORKIQ_LIVE = '1'
dotnet test tests/Swankers.Coach.Tests -c Release --filter "FullyQualifiedName~WorkIqLiveSmoke" --logger "console;verbosity=normal"
```

Retrieval (reported as passed in about 11 seconds for the whole test run):

```powershell
$env:WORKIQ_LIVE = '1'
$env:WORKIQ_ARGS = 'mcp --account <account>'
$env:WORKIQ_ALLOWED_TOOLS = 'fetch'
$env:WORKIQ_READ_TOOL = 'fetch'
$env:WORKIQ_EXPECT_ITEMS = '9'
$env:WORKIQ_READ_ARGS = '{"entityUrls":["/me/mailFolders/inbox/messages?$filter=contains(subject,%27%5BNW-DPM%5D%27)&$select=id,subject,receivedDateTime,from,bodyPreview,webLink&$top=25"]}'

dotnet test tests/Swankers.Coach.Tests -c Release `
  --filter "FullyQualifiedName~WorkIqLiveSmokeTests.Reads_the_tagged_corpus_with_references_and_a_span" `
  --logger "console;verbosity=normal"
```

The query reads the Inbox only. The maintainer found that `/me/messages` returns each seed email twice, because mail sent to oneself is in both Inbox and Sent Items.

Since that run, these values became the provider's defaults (`WorkIqOptions` in `src/Swankers.Coach/Workplace/WorkplaceOptions.cs`): allow-list `fetch`, read tool `fetch`, and this Inbox query built from the engagement tag. A model-free test asserts the default arguments equal the string above character for character. The defaults themselves have not been run live; a run with only `WORKIQ_LIVE` and `WORKIQ_ACCOUNT` set would confirm them.

## What the server lists

`workiq mcp` wrote one line to stderr on each start: `[MCP Proxy] Registered 9 remote tools from https://workiq.svc.cloud.microsoft/mcp`. So the local server is a proxy in front of the remote Work IQ MCP endpoint. Nothing token-like appeared on stderr in either run.

It listed twelve tools. The names match Learn's tool reference (no `_work_iq` suffix), plus `accept_eula`.

| Tool | Required arguments | This scenario |
|---|---|---|
| `fetch` | `entityUrls` (array of strings) | The only tool called |
| `fetch_blob` | `path` | Not allowed |
| `ask` | `question` | Not allowed |
| `call_function` | `functionUrl` | Not allowed |
| `get_schema` | none | Not allowed |
| `list_agents` | none | Not allowed |
| `search_paths` | `filter` | Not allowed |
| `create_entity` | `parentUrl`, `jsonBody` | Never allowed, even if listed |
| `update_entity` | `entityUrl`, `jsonBody` | Never allowed, even if listed |
| `delete_entity` | `entityUrl` | Never allowed, even if listed |
| `do_action` | `actionUrl`, `jsonBody` | Never allowed, even if listed |
| `accept_eula` | `eulaUrl` | Never allowed, even if listed |

`fetch` also takes an optional `agentId`. Its paths are relative, with no base URL.

## Result shape

Structure of the `fetch` result for the Inbox query, values removed. It arrived as JSON, 7,732 characters for nine messages.

```text
results: array of 1 (one entry per requested path)
  data:
    @odata.context: string
    value: array of 9
      @odata.etag: string
      id: string (152 characters)
      receivedDateTime: UTC timestamp
      subject: string
      bodyPreview: string (89 to 168 characters; line breaks kept)
      webLink: string (host outlook.office365.com)
      from:
        emailAddress:
          name: string
          address: string
  statusCode: 200
note: "Results limited to 25 items per collection. If you need different results, refine your $filter or $search parameters."
```

This is retrieved source material with identifiers, links, and timestamps. It is not a synthesized answer and carries no citations of its own; the reference for each item is its Graph `id` and `webLink`.

## What the application did with it

`WorkIqEvidenceMapper` turned each message into an evidence item: `subject` to title, `from.emailAddress.name` to author, `receivedDateTime` to `receivedAt`, `bodyPreview` to text, `id` and `webLink` copied unchanged as the reference and link. All nine carried the tag and were kept. They were numbered E1 to E9 in order of receipt, which is the seed's send order.

The shape matched what the mapper had been written against from the documentation, so its mapping did not change. Three things did change because of what the live server showed:

- The provider now checks each path's `statusCode`. A refused path arrives inside a successful tool call and would otherwise have read as "no evidence".
- `accept_eula` joined the tools that are refused whatever the allow-list says.
- The verified tool name and Inbox query became the defaults; the allow-list had been empty until the real names were known.

## Limits

- **Preview text.** The query selects `bodyPreview`, which Microsoft Graph caps at 255 characters. The corpus items are 89 to 168 characters, so nothing was cut. A longer message would be cut without any marker.
- **Page size.** The service limits a collection to 25 items and the provider does not follow further pages. The corpus is nine.
- **Ids.** Live ids follow order of receipt. The Replay fixture, the seed, and the build plan's corpus table were renumbered to the same order on 2026-10-05, so a mailbox seeded in the seed's order gives each message the same id in Live and in Replay. That holds only while the Inbox holds exactly these nine tagged messages in that order.
- **Dates.** Every `receivedDateTime` is October 5, the day the corpus was seeded, while the fictional events run from September 22 to October 2. The maintainer's rule: the explicit business date contained in the evidence is authoritative for chronology, and mailbox received time is transport metadata only. Each item's business date is read from the first date written in its text; applied offline to the nine captured texts it gives September 22, 29, 30, 30, October 1, 1, 2, 2, 2. Received time only orders the ids.
- **Timing.** The mail was received between 19:10 and 19:25 UTC and read at 20:08 UTC. How soon after sending it becomes readable was not measured.
- **Not exercised.** The `ask` tool; any tool other than `fetch`; the end-to-end agent in Live mode (no Work IQ result has been sent to a model); token expiry and renewal; a second machine.

## Where the captures are

On the maintainer's machine only, outside the repository, in `%LOCALAPPDATA%\SwankersCoach\workplace\live\` (moved there on 2026-10-05; the checkout is in a synced folder, so ignoring them in git was not enough). The live smoke test writes there by default, and `WORKIQ_CAPTURE_ROOT` overrides it:

- `tools-20261005-195730.txt`: the twelve tools with descriptions and input schemas.
- `raw-result-20261005-200822.txt`: the `fetch` result as returned.
- `evidence-20261005-200822.txt`: the nine mapped evidence items.
- `stderr-connect-20261005-195730.txt`, `stderr-read-20261005-200822.txt`: the one stderr line from each start.
