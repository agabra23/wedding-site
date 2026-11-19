terraform {
  required_version = ">= 1.9"

  required_providers {
    azurerm    = { source = "hashicorp/azurerm", version = "~> 4.0" }
    azuread    = { source = "hashicorp/azuread", version = "~> 3.0" }
    cloudflare = { source = "cloudflare/cloudflare", version = "~> 5.0" }
    random     = { source = "hashicorp/random", version = "~> 3.6" }
  }

  backend "azurerm" {
    resource_group_name  = "rg-tfstate"
    storage_account_name = "sttfstatewedab"
    container_name       = "tfstate"
    key                  = "wedding.tfstate"
    use_azuread_auth     = true
    # In CI, ARM_USE_OIDC=true + ARM_CLIENT_ID/ARM_TENANT_ID/ARM_SUBSCRIPTION_ID select OIDC auth.
  }
}

provider "azurerm" {
  features {}
  subscription_id = var.subscription_id
}

provider "azuread" {}

provider "cloudflare" {
  api_token = var.cloudflare_api_token
}
