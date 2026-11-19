output "swa_deployment_token" {
  value     = azurerm_static_web_app.site.api_key
  sensitive = true
}
output "api_default_url" { value = "https://${azurerm_container_app.api.ingress[0].fqdn}" }
output "deploy_client_id" { value = azuread_application.deploy.client_id }
output "tenant_id" { value = data.azurerm_client_config.current.tenant_id }
output "cosmos_dev_connection" {
  value     = azurerm_cosmosdb_account.cosmos_dev.primary_sql_connection_string
  sensitive = true
}
output "swa_dev_url" { value = "https://${azurerm_static_web_app.site_dev.default_host_name}" }
output "api_dev_default_url" { value = "https://${azurerm_container_app.api_dev.ingress[0].fqdn}" }
