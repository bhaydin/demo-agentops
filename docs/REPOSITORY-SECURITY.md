# Repository and deployment authorization

The public repository accepts contributions through pull requests. Public visibility does
not grant write access. `bhaydin` is the sole repository administrator and code owner.

## Main branch

Two active GitHub rulesets apply to `refs/heads/main`:

- **main - required checks and history** requires a PR, resolved conversations, and the
  `build` check from GitHub Actions (integration 15368), with the branch up to date.
  Force pushes and deletion are blocked. This ruleset has **no bypass actors**, including
  administrators. The build includes the model-free tests and no-MFL-import guard.
- **main - maintainer review** requires one approval, code-owner review, dismissal of
  stale reviews, and approval of the latest push by someone other than its author.
  `.github/CODEOWNERS` assigns every path to `@bhaydin`, including workflows, eval
  thresholds, infrastructure, and CODEOWNERS itself.

GitHub does not let authors approve their own PRs. The sole administrator therefore has
a **PR-only review bypass** for owner-authored changes. This is an explicit maintainer
merge decision; it cannot bypass the separate required-checks ruleset or permit direct
pushes. Additional administrators would inherit this review bypass, so granting admin
access changes the trust boundary. Never bypass review for an uninspected contribution.

## GitHub Actions and Azure

All external contributors need approval before fork workflows run, even repeat
contributors. Inspect workflow changes and executable build/test code first. Approving
a workflow run is separate from approving or merging its PR.

The default GitHub token is read-only and cannot approve PRs. Actions must use full
commit SHAs. PR CI uses GitHub-hosted runners, no Azure login, and no persisted checkout
credentials. Cloud evals are manual runs from `main`; PRs run model-free tests only.

Both `evals` and `foundry` environments require **@bhaydin's explicit approval**, allow
only the **main branch** (not tags, PR refs, or other branches), and disable administrator
bypass of environment protection. The maintainer can approve their own dispatched run
because this is a single-maintainer demo; no unattended approval is configured.

Azure enforces the boundary independently of YAML: `swankers-ci-evals` trusts only
`repo:bhaydin/demo-agentops:environment:evals`, and `swankers-ci-deploy` trusts only the
equivalent `environment:foundry` subject (both also register GitHub's ID-qualified form).
The old eval federation for `main` and `pull_request` is removed. Knowing the client IDs
does not grant access. Neither CI application has a client password or certificate.
The eval identity retains its separate project scope and cannot change Coach's hosted
agent; the workflow verifies that denial before running evals.

To evaluate or promote:

1. Merge the reviewed PR after the required build passes.
2. In **Actions → evals → Run workflow**, select **main** and the desired prompt versions.
   Leave `promote` false for the Thursday comparison; select it explicitly for deployment.
3. Review the run's commit and approve the **evals** environment to permit model access.
4. For promotion, inspect the selected version's report and approve **foundry**. The
   job accepts only a passing report from that same workflow run. A failing v2 comparison
   does not block promotion of a passing v1; missing/failed v1 evidence does.

The stage/rollback commands use the presenter's local Azure identity. These GitHub gates
govern CI credentials; they do not remove the maintainer's Azure RBAC permissions or
block manual `azd` deployments. Account compromise or an administrator deliberately
changing protection settings remains outside this boundary.

## Reapplying and verifying

Run `pwsh infra/github-security.ps1` with GitHub repository administration to reconcile
the rulesets, environments, and Actions settings. It preserves unrelated rulesets but
removes broader branch/tag allowances on the two CI environments. Run
`pwsh infra/ci-identity.ps1` for full identity/bootstrap setup; it applies GitHub protection
first, reconciles federation on the dedicated CI apps, and stores client IDs in their
respective environments. Do not restore branch/PR federation or the repository-wide
eval client ID. Older workflows without `environment: evals` fail authentication after
migration; merge the hardened workflow before starting new cloud runs.

Verify using `gh api repos/bhaydin/demo-agentops/rules/branches/main`, the repository's
**Settings → Environments** and **Actions → General**, and
`az ad app federated-credential list --id <application-object-id>`. Readback must show
main-only policies, owner reviewers, no environment admin bypass, and only the matching
environment subjects for each CI identity. Verify PR CI and the next maintainer-approved
cloud run before relying on a fresh deployment.

References: [GitHub rulesets REST API](https://docs.github.com/en/rest/repos/rules),
[environment protection](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments),
[Actions permissions](https://docs.github.com/en/rest/actions/permissions), and
[Azure federation CLI](https://learn.microsoft.com/en-us/cli/azure/ad/app/federated-credential?view=azure-cli-latest).
