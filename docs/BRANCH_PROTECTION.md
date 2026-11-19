# Branch protection guide

Goal: nothing reaches `main` (and therefore production) except through a pull request that passed CI.

> **Plan check:** rulesets and branch protection on a **private** repo need GitHub Pro, Team or Enterprise. On the Free plan they only work on public repos. Check with `gh api repos/agabra23/wedding-site --jq .private`.

## Flow

```text
feature branch → PR into dev → PR from dev into main → deploy
```

## 1. Ruleset for `main`

GitHub → repo **Settings → Rules → Rulesets → New ruleset → New branch ruleset**.

| Setting | Value |
| --- | --- |
| Name | `protect-main` |
| Enforcement status | **Active** |
| Bypass list | **Empty** (so it applies to you too) |
| Target branches | Add target → Include by pattern → `main` |
| Restrict deletions | On |
| Block force pushes | On |
| Require a pull request before merging | On |
| → Required approvals | **0** (you can't approve your own PR; use 1 if a second person joins) |
| → Require conversation resolution | On |
| → Allowed merge methods | **Merge** only (see note below) |
| Require status checks to pass | On |
| → Require branches to be up to date | On |
| → Required checks | `site`, `api`, `infra-validate` |

Add **`terraform-plan`** to the required checks only after it passes reliably. It runs only on PRs that touch `infra/`, so requiring it early can block unrelated PRs.

**Why merge commits, not squash:** `dev` is long-lived. Squashing `dev` into `main` creates a commit `dev` doesn't have, so the branches diverge and later PRs conflict. Merge commits keep them in sync. Don't turn on "Require linear history": it forbids merge commits.

## 2. Light protection for `dev` (optional)

A second ruleset targeting `dev`: block deletions and force pushes, and require the `site`, `api` and `infra-validate` checks. Skip the PR requirement if you want to push straight to `dev`.

## 3. Lock the Production environment to `main`

Settings → **Environments → Production → Deployment branches and tags** → *Selected branches* → add `main`. Optionally add **Required reviewers** (yourself) for a manual approval before every deploy and `terraform apply`.

## 4. Same thing from the CLI

```bash
gh api repos/agabra23/wedding-site/rulesets --method POST --input - <<'JSON'
{
  "name": "protect-main",
  "target": "branch",
  "enforcement": "active",
  "bypass_actors": [],
  "conditions": { "ref_name": { "include": ["refs/heads/main"], "exclude": [] } },
  "rules": [
    { "type": "deletion" },
    { "type": "non_fast_forward" },
    { "type": "pull_request", "parameters": {
        "required_approving_review_count": 0,
        "dismiss_stale_reviews_on_push": false,
        "require_code_owner_review": false,
        "require_last_push_approval": false,
        "required_review_thread_resolution": true,
        "allowed_merge_methods": ["merge"] } },
    { "type": "required_status_checks", "parameters": {
        "strict_required_status_checks_policy": true,
        "required_status_checks": [
          { "context": "site" }, { "context": "api" }, { "context": "infra-validate" } ] } }
  ]
}
JSON
```

## 5. Verify

```bash
git checkout main && git commit --allow-empty -m test && git push origin main   # should be rejected
```

Then open a PR into `main`: the merge button stays disabled until `site`, `api` and `infra-validate` are green.
