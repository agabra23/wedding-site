variable "subscription_id" { type = string }

variable "location" {
  type    = string
  default = "eastus2"
}

variable "domain" { type = string }              # yourwedding.com
variable "cosmos_account_name" { type = string } # globally unique
variable "github_repo" { type = string }         # owner/repo
variable "budget_email" { type = string }
variable "cloudflare_zone_id" { type = string }

variable "cloudflare_api_token" {
  type      = string
  sensitive = true # set via TF_VAR_cloudflare_api_token
}

variable "admin_object_id" { type = string } # Entra object ID of the human owner (az ad signed-in-user show --query id)
