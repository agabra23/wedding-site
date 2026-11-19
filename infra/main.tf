data "azurerm_client_config" "current" {}

resource "azurerm_resource_group" "rg" {
  name     = "rg-wedding"
  location = var.location
}

# ---------- Budget: email if spend passes $8/month (expected ~$4-5) ----------
resource "azurerm_consumption_budget_subscription" "budget" {
  name            = "wedding-budget"
  subscription_id = "/subscriptions/${var.subscription_id}"
  amount          = 8
  time_grain      = "Monthly"

  time_period {
    start_date = "2026-11-01T00:00:00Z" # first day of the current month
  }

  notification {
    enabled        = true
    threshold      = 80
    operator       = "GreaterThan"
    threshold_type = "Actual"
    contact_emails = [var.budget_email]
  }
}

# ---------- Cosmos DB (free tier) ----------
resource "azurerm_cosmosdb_account" "cosmos" {
  name                = var.cosmos_account_name
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  offer_type          = "Standard"
  kind                = "GlobalDocumentDB"
  free_tier_enabled   = true # can only be set at creation

  consistency_policy {
    consistency_level = "Session"
  }

  geo_location {
    location          = azurerm_resource_group.rg.location
    failover_priority = 0
  }
}

resource "azurerm_cosmosdb_sql_database" "db" {
  name                = "wedding"
  resource_group_name = azurerm_resource_group.rg.name
  account_name        = azurerm_cosmosdb_account.cosmos.name
  throughput          = 400 # shared; anything up to 1000 stays free
}

moved {
  from = azurerm_cosmosdb_sql_container.parties["parties"]
  to   = azurerm_cosmosdb_sql_container.parties
}

resource "azurerm_cosmosdb_sql_container" "parties" {
  name                = "parties"
  resource_group_name = azurerm_resource_group.rg.name
  account_name        = azurerm_cosmosdb_account.cosmos.name
  database_name       = azurerm_cosmosdb_sql_database.db.name
  partition_key_paths = ["/pk"] # ADR-0001: one constant value for every document
  default_ttl         = -1      # TTL on, no default expiry: only Revocation documents set their own
}

# ---------- Cosmos DB for local dev (serverless, separate account: ADR-0003) ----------
# No free_tier_enabled: only one free-tier account is allowed per subscription and prod has it.
resource "azurerm_cosmosdb_account" "cosmos_dev" {
  name                = "${var.cosmos_account_name}-dev"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  offer_type          = "Standard"
  kind                = "GlobalDocumentDB"

  capabilities {
    name = "EnableServerless"
  }

  consistency_policy {
    consistency_level = "Session"
  }

  geo_location {
    location          = azurerm_resource_group.rg.location
    failover_priority = 0
  }
}

resource "azurerm_cosmosdb_sql_database" "db_dev" {
  name                = "wedding"
  resource_group_name = azurerm_resource_group.rg.name
  account_name        = azurerm_cosmosdb_account.cosmos_dev.name
  # no throughput: serverless rejects it
}

resource "azurerm_cosmosdb_sql_container" "parties_dev" {
  name                = "parties"
  resource_group_name = azurerm_resource_group.rg.name
  account_name        = azurerm_cosmosdb_account.cosmos_dev.name
  database_name       = azurerm_cosmosdb_sql_database.db_dev.name
  partition_key_paths = ["/pk"] # ADR-0001
  default_ttl         = -1      # same as prod: only Revocation documents set their own expiry
}

# ---------- Static Web App (Free) ----------
resource "azurerm_static_web_app" "site" {
  name                = "swa-wedding"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  sku_tier            = "Free"
  sku_size            = "Free"
}

resource "azurerm_static_web_app_custom_domain" "www" {
  static_web_app_id = azurerm_static_web_app.site.id
  domain_name       = "www.${var.domain}"
  validation_type   = "cname-delegation"
  depends_on        = [cloudflare_dns_record.www]
}

resource "azurerm_static_web_app_custom_domain" "apex" {
  static_web_app_id = azurerm_static_web_app.site.id
  domain_name       = var.domain
  validation_type   = "dns-txt-token"
}

# ---------- Key Vault: Event Password, Admin Password, token signing key ----------
# Access policies rather than RBAC: the deploy identity is only Contributor and can't create role assignments.
resource "azurerm_key_vault" "kv" {
  name                       = "kv-wedding-${substr(sha1(var.subscription_id), 0, 8)}"
  location                   = azurerm_resource_group.rg.location
  resource_group_name        = azurerm_resource_group.rg.name
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  sku_name                   = "standard"
  rbac_authorization_enabled = false
}

# Whoever runs Terraform (the deploy identity in CI) manages the secrets
resource "azurerm_key_vault_access_policy" "terraform" {
  key_vault_id       = azurerm_key_vault.kv.id
  tenant_id          = data.azurerm_client_config.current.tenant_id
  object_id          = data.azurerm_client_config.current.object_id
  secret_permissions = ["Get", "List", "Set", "Delete", "Recover", "Purge"]
}

resource "azurerm_user_assigned_identity" "api" {
  name                = "id-wedding-api"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
}

resource "azurerm_key_vault_access_policy" "api" {
  key_vault_id       = azurerm_key_vault.kv.id
  tenant_id          = data.azurerm_client_config.current.tenant_id
  object_id          = azurerm_user_assigned_identity.api.principal_id
  secret_permissions = ["Get"]
}

# The passwords start as random throwaway values so nobody can sign in with a guessable
# default. Set the real ones once, outside Terraform (they never enter CI or state):
#   az keyvault secret set --vault-name <vault> --name event-password --value '...'
#   az keyvault secret set --vault-name <vault> --name admin-password --value '...'
# Then restart the API revision so it picks them up.
resource "random_password" "initial_event_password" {
  length  = 32
  special = false
}

resource "random_password" "initial_admin_password" {
  length  = 32
  special = false
}

resource "azurerm_key_vault_secret" "event_password" {
  name         = "event-password"
  key_vault_id = azurerm_key_vault.kv.id
  value        = random_password.initial_event_password.result
  depends_on   = [azurerm_key_vault_access_policy.terraform]

  lifecycle {
    ignore_changes = [value]
  }
}

resource "azurerm_key_vault_secret" "admin_password" {
  name         = "admin-password"
  key_vault_id = azurerm_key_vault.kv.id
  value        = random_password.initial_admin_password.result
  depends_on   = [azurerm_key_vault_access_policy.terraform]

  lifecycle {
    ignore_changes = [value]
  }
}

# Terraform owns the signing key (rotating it signs everyone out)
resource "random_password" "token_signing_key" {
  length  = 64
  special = false
}

resource "azurerm_key_vault_secret" "token_signing_key" {
  name         = "token-signing-key"
  key_vault_id = azurerm_key_vault.kv.id
  value        = random_password.token_signing_key.result
  depends_on   = [azurerm_key_vault_access_policy.terraform]
}

# ---------- Container Apps: environment + always-warm API ----------
resource "azurerm_container_app_environment" "env" {
  name                = "cae-wedding"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  # No log_analytics_workspace_id: avoids Log Analytics ingestion charges
}

resource "azurerm_container_app" "api" {
  name                         = "ca-wedding-api"
  container_app_environment_id = azurerm_container_app_environment.env.id
  resource_group_name          = azurerm_resource_group.rg.name
  revision_mode                = "Single"
  depends_on                   = [azurerm_key_vault_access_policy.api]

  secret {
    name  = "cosmos-connection"
    value = azurerm_cosmosdb_account.cosmos.primary_sql_connection_string
  }

  # ADR-0002: shared passwords and token signing key, read from Key Vault by the API's identity
  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.api.id]
  }

  secret {
    name                = "event-password"
    key_vault_secret_id = azurerm_key_vault_secret.event_password.versionless_id
    identity            = azurerm_user_assigned_identity.api.id
  }

  secret {
    name                = "admin-password"
    key_vault_secret_id = azurerm_key_vault_secret.admin_password.versionless_id
    identity            = azurerm_user_assigned_identity.api.id
  }

  secret {
    name                = "token-signing-key"
    key_vault_secret_id = azurerm_key_vault_secret.token_signing_key.versionless_id
    identity            = azurerm_user_assigned_identity.api.id
  }

  ingress {
    external_enabled = true
    target_port      = 80
    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas = 1 # always warm: no cold starts
    max_replicas = 1

    container {
      name   = "api"
      image  = "mcr.microsoft.com/k8se/quickstart:latest" # placeholder until CI ships the real image
      cpu    = 0.25
      memory = "0.5Gi"

      env {
        name        = "CosmosConnection"
        secret_name = "cosmos-connection"
      }
      env {
        name        = "EventPassword"
        secret_name = "event-password"
      }
      env {
        name        = "AdminPassword"
        secret_name = "admin-password"
      }
      env {
        name        = "TokenSigningKey"
        secret_name = "token-signing-key"
      }
      env {
        name  = "PartiesContainer"
        value = "parties"
      }
    }
  }

  lifecycle {
    # GitHub Actions owns the image tag (api.yml); Terraform owns everything else
    ignore_changes = [template[0].container[0].image]
  }
}

resource "azurerm_container_app_custom_domain" "api" {
  name             = "api.${var.domain}"
  container_app_id = azurerm_container_app.api.id
  depends_on       = [cloudflare_dns_record.api, cloudflare_dns_record.api_verify]

  lifecycle {
    # The free managed certificate is bound once by CLI (see README)
    ignore_changes = [certificate_binding_type, container_app_environment_certificate_id]
  }
}

# ---------- Entra ID: GitHub Actions deploy identity (OIDC, no stored secret) ----------
resource "azuread_application" "deploy" {
  display_name = "wedding-github-deploy"
}

resource "azuread_service_principal" "deploy" {
  client_id = azuread_application.deploy.client_id
}

resource "azuread_application_federated_identity_credential" "deploy_main" {
  application_id = azuread_application.deploy.id
  display_name   = "github-main"
  audiences      = ["api://AzureADTokenExchange"]
  issuer         = "https://token.actions.githubusercontent.com"
  subject        = "repo:${var.github_repo}:ref:refs/heads/main"
}

resource "azurerm_role_assignment" "deploy" {
  scope                = azurerm_resource_group.rg.id
  role_definition_name = "Contributor"
  principal_id         = azuread_service_principal.deploy.object_id
}

# ---------- Cloudflare DNS (all DNS only, never proxied) ----------
resource "cloudflare_dns_record" "www" {
  zone_id = var.cloudflare_zone_id
  name    = "www.${var.domain}"
  type    = "CNAME"
  content = azurerm_static_web_app.site.default_host_name
  ttl     = 1
  proxied = false
}

resource "cloudflare_dns_record" "apex" {
  zone_id = var.cloudflare_zone_id
  name    = var.domain
  type    = "CNAME" # Cloudflare flattens it at the root
  content = azurerm_static_web_app.site.default_host_name
  ttl     = 1
  proxied = false
}

resource "cloudflare_dns_record" "apex_validation" {
  zone_id = var.cloudflare_zone_id
  name    = "_dnsauth.${var.domain}"
  type    = "TXT"
  content = azurerm_static_web_app_custom_domain.apex.validation_token
  ttl     = 1

  lifecycle {
    ignore_changes = [content] # Azure clears the token once validated
  }
}

resource "cloudflare_dns_record" "api" {
  zone_id = var.cloudflare_zone_id
  name    = "api.${var.domain}"
  type    = "CNAME"
  content = azurerm_container_app.api.ingress[0].fqdn
  ttl     = 1
  proxied = false
}

resource "cloudflare_dns_record" "api_verify" {
  zone_id = var.cloudflare_zone_id
  name    = "asuid.api.${var.domain}"
  type    = "TXT"
  content = azurerm_container_app.api.custom_domain_verification_id
  ttl     = 1
}
