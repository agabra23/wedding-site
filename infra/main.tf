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

resource "azurerm_cosmosdb_sql_container" "parties" {
  for_each            = toset(["parties", "parties-dev"])
  name                = each.key
  resource_group_name = azurerm_resource_group.rg.name
  account_name        = azurerm_cosmosdb_account.cosmos.name
  database_name       = azurerm_cosmosdb_sql_database.db.name
  partition_key_paths = ["/id"]
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

  secret {
    name  = "cosmos-connection"
    value = azurerm_cosmosdb_account.cosmos.primary_sql_connection_string
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

# ---------- Entra ID: admin sign-in app ----------
resource "random_uuid" "admin_scope" {}

resource "azuread_application" "admin" {
  display_name     = "wedding-admin"
  sign_in_audience = "AzureADandPersonalMicrosoftAccount" # work and personal Microsoft accounts

  api {
    requested_access_token_version = 2
    oauth2_permission_scope {
      id                         = random_uuid.admin_scope.result
      value                      = "access_as_user"
      type                       = "User"
      enabled                    = true
      admin_consent_display_name = "Access the wedding admin API"
      admin_consent_description  = "Lets the admin page call the wedding API."
      user_consent_display_name  = "Access the wedding admin API"
      user_consent_description   = "Lets the admin page call the wedding API."
    }
  }

  single_page_application {
    redirect_uris = [
      "https://${var.domain}/admin",
      "https://www.${var.domain}/admin",
      "http://localhost:5173/admin",
    ]
  }

  lifecycle {
    ignore_changes = [identifier_uris] # managed by the resource below
  }
}

resource "azuread_application_identifier_uri" "admin" {
  application_id = azuread_application.admin.id
  identifier_uri = "api://${azuread_application.admin.client_id}"
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
