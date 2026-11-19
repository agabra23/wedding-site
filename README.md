# Wedding site

React (Vite) site on Azure Static Web Apps, .NET minimal API on Azure Container Apps, Cosmos DB (NoSQL API), all managed by Terraform. See the infra setup doc for the architecture.

```text
infra/            Terraform
src/, public/     React + TypeScript site
api/WeddingApi/   ASP.NET Core API + Dockerfile
.github/workflows ci, infra, site, api
```

## Branches and workflows

| Event | Workflow | What runs |
| --- | --- | --- |
| PR into `dev` or `main`, push to `dev` | `ci` | site build, API build + docker build, terraform fmt/validate |
| PR into `main` touching `infra/` | `infra` | `terraform plan` (federated cred: `pull_request`) |
| Push to `main` touching `infra/` | `infra` | `terraform apply` (environment `production`) |
| Push to `main` touching site code | `site` | build + deploy Static Web App (environment `production`) |
| Push to `main` touching `api/` | `api` | build image to GHCR + update Container App (environment `production`) |

Flow: feature branch → PR into `dev` → PR from `dev` into `main` → deploy.

## GitHub setup (one time)

Repo variables (not secret): `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`.
Repo secret: `CLOUDFLARE_API_TOKEN`.
Environment: `production` (optionally add yourself as a required reviewer).

## Local dev

```bash
cd api/WeddingApi && dotnet run --urls http://localhost:5080   # API, uses parties-dev
npm run dev                                                    # site on :5173 (Node 20+)
```

Local API secrets go in user-secrets (see the setup doc, Step 3).

## Placeholders to fill

`infra/terraform.tfvars`, `infra/providers.tf` (state storage account), `.env.development`, `.env.production`, `api/WeddingApi/appsettings.json` (client ID, domain, admin emails).
