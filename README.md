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
| PR into `dev` or `main`, push to `dev` | `ci` | site build, API build + tests + docker build, terraform fmt/validate |
| PR into `main` touching `infra/` | `infra` | `terraform plan` (federated cred: `pull_request`) |
| Push to `main` touching `infra/` | `infra` | `terraform apply` (environment `Production`) |
| Push to `main` touching site code | `site` | build + deploy Static Web App (environment `Production`) |
| Push to `main` touching `api/` | `api` | build image to GHCR + update Container App (environment `Production`) |
| Push to `dev` touching `infra/` | `infra` | `terraform apply` (environment `Development`) |
| Push to `dev` touching site code | `site-dev` | build + deploy dev Static Web App `swa-wedding-dev` against the dev API (environment `Development`) |
| Push to `dev` touching `api/` | `api-dev` | build image to GHCR + update dev Container App `ca-wedding-api-dev` (environment `Development`) |

Flow: feature branch → PR into `dev` → PR from `dev` into `main` → deploy.

## GitHub setup (one time)

Repo variables (not secret): `ARM_CLIENT_ID`, `ARM_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`.
Repo secret: `CLOUDFLARE_API_TOKEN`.
Environments: `Production` (optionally add yourself as a required reviewer) and `Development` (no reviewers; created automatically on first use). Terraform creates the federated credential for `Development`, but the first apply that adds it must run from `main` or locally, since a `dev` run can't sign in before it exists.

CI signs in to Azure as the app registration `ar-terraform` (the `ARM_CLIENT_ID` variable), **not** the `wedding-github-deploy` app Terraform manages. Its federated credentials are created by hand, because the apply itself signs in with them. This repo uses GitHub's immutable OIDC subject, so each subject starts with `repo:agabra23@123777400/wedding-site@1463459259` (read it with `gh api repos/agabra23/wedding-site/actions/oidc/customization/sub --jq .sub_claim_prefix`) followed by `:pull_request`, `:environment:Production` or `:environment:Development`. To add one:

```bash
az ad app federated-credential create --id <ARM_CLIENT_ID> --parameters '{"name":"...","issuer":"https://token.actions.githubusercontent.com","subject":"<prefix>:environment:<Name>","audiences":["api://AzureADTokenExchange"]}'
```

The dev site lives at `https://dev.thegabras2027.com` (Cloudflare CNAME plus SWA custom domain, both in Terraform; the `*.azurestaticapps.net` host also works). The dev API stays on its generated Azure host: the site build looks it up at deploy time, so it needs no DNS record of its own and uses the dev Cosmos account and its own Key Vault secrets (`event-password-dev`, `admin-password-dev`, `token-signing-key-dev`); set the real dev passwords the same way as prod.

## Local dev

Quickest: `npm run dev` (runs `scripts/dev.sh`) starts both and Ctrl+C stops both. Or run them separately:

```bash
cd api/WeddingApi && dotnet run --urls http://localhost:5080   # API, uses the dev Cosmos account
npm run dev:only                                                 # site on :5173 (Node 20+)
```

### Dev database

Local runs use their own serverless Cosmos account, `cosmos-wedding-thegabras2027-dev` (database `wedding`, container `parties`, same names as prod), so dev credentials can't reach prod data. After `terraform apply`, set the connection string once:

```bash
cd infra && terraform output -raw cosmos_dev_connection | \
  (cd ../api/WeddingApi && dotnet user-secrets set CosmosConnection "$(cat)")
```

In `Development` the API refuses to start unless `CosmosConnection` points at the dev account (`DevCosmosHost`) or the local emulator (see ADR-0003). Make sure no `CosmosConnection` or `PartiesContainer` environment variables are set, since they override user-secrets.

### API tests

The API tests host the API in-process and run against the Azure Cosmos DB emulator, so they need **Docker**:

```bash
docker run -d --name cosmos-emu -p 8081:8081 mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview
dotnet test api/WeddingApi.Tests
```

Each test class creates its own throwaway database and deletes it afterwards. Set `COSMOS_TEST_CONNECTION` to point at a different emulator. Tests control time through `ApiFactory.Clock` (a fake `TimeProvider`). CI runs the same emulator as a service container in the `api` job.

Local API secrets go in user-secrets (see the setup doc, Step 3). `appsettings.Development.json` carries throwaway dev values for `EventPassword`, `AdminPassword` and `TokenSigningKey`; in production they come from Azure Key Vault via the Container App (Terraform creates the vault; set the real passwords once with `az keyvault secret set`, see the comment in `infra/main.tf`; the signing key is generated by Terraform).

## Placeholders to fill

`infra/terraform.tfvars`, `infra/providers.tf` (state storage account), `.env.development`, `.env.production`, `api/WeddingApi/appsettings.json`.
