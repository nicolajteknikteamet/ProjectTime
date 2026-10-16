#!/usr/bin/env bash
# Skapar en gratis ProjectTime-server i Azure App Service (Free F1, Windows) och skriver ut
# det du behöver för att låta GitHub Actions driftsätta dit.
#
# Kör i Azure Cloud Shell (Bash) på https://shell.azure.com – inget behöver installeras lokalt:
#   curl -fsSL https://raw.githubusercontent.com/<ägare>/ProjectTime/main/deploy/azure/create-azure.sh -o create-azure.sh
#   bash create-azure.sh <unikt-appnamn> '<starkt-lösenord>'
#
# Varför Windows? På gratisnivån är filsystemet en nätverksresurs, och Microsoft avråder från SQLite
# på Linux-varianten (inga exklusiva fillås). På Windows fungerar SQLite på D:\home.
set -euo pipefail

APP_NAME="${1:?Ange ett globalt unikt appnamn, t.ex. projecttime-nicolaj}"
PASSWORD="${2:?Ange lösenordet för webbgränssnittet}"
LOCATION="${LOCATION:-swedencentral}"
RESOURCE_GROUP="${RESOURCE_GROUP:-projecttime-rg}"
PLAN_NAME="${PLAN_NAME:-projecttime-plan}"
TIME_ZONE="${TIME_ZONE:-Europe/Stockholm}"

echo "==> Resursgrupp $RESOURCE_GROUP i $LOCATION"
az group create --name "$RESOURCE_GROUP" --location "$LOCATION" --output none

echo "==> App Service-plan $PLAN_NAME (Free F1, Windows)"
az appservice plan create --name "$PLAN_NAME" --resource-group "$RESOURCE_GROUP" --sku F1 --output none

echo "==> Webbapp $APP_NAME (.NET 10)"
# Om 'dotnet:10' avvisas: kör 'az webapp list-runtimes --os windows' och välj närmaste .NET 10-värde.
az webapp create --name "$APP_NAME" --resource-group "$RESOURCE_GROUP" --plan "$PLAN_NAME" --runtime "dotnet:10" --output none

echo "==> Inställningar"
az webapp config set --name "$APP_NAME" --resource-group "$RESOURCE_GROUP" \
  --web-sockets-enabled true --http20-enabled true --min-tls-version 1.2 --output none
az webapp update --name "$APP_NAME" --resource-group "$RESOURCE_GROUP" --https-only true --output none
az webapp config appsettings set --name "$APP_NAME" --resource-group "$RESOURCE_GROUP" --output none --settings \
  "PROJECTTIME_ProjectTime__Password=$PASSWORD" \
  "PROJECTTIME_ProjectTime__TimeZone=$TIME_ZONE" \
  "WEBSITE_TIME_ZONE=W. Europe Standard Time" \
  "ASPNETCORE_ENVIRONMENT=Production"

echo "==> Tillåt publiceringsprofil (basic auth för SCM) så att GitHub Actions kan driftsätta"
az resource update --resource-group "$RESOURCE_GROUP" --name scm --namespace Microsoft.Web \
  --resource-type basicPublishingCredentialsPolicies --parent "sites/$APP_NAME" --set properties.allow=true --output none

PROFILE_FILE="$APP_NAME.PublishSettings"
az webapp deployment list-publishing-profiles --name "$APP_NAME" --resource-group "$RESOURCE_GROUP" --xml > "$PROFILE_FILE"

cat <<MSG

Klart. Servern finns på https://$APP_NAME.azurewebsites.net (tom tills första driftsättningen).

Nästa steg i GitHub-repot (Settings → Secrets and variables → Actions):
  1. Ny *secret*   AZURE_WEBAPP_PUBLISH_PROFILE  = innehållet i filen $PROFILE_FILE
     (visa med:  cat $PROFILE_FILE )
  2. Ny *variable* AZURE_WEBAPP_NAME             = $APP_NAME
  3. Kör workflowen "Deploy to Azure" (Actions → Deploy to Azure → Run workflow), eller pusha till main.

Logga sedan in på https://$APP_NAME.azurewebsites.net med lösenordet du angav och registrera dina datorer under Enheter.
Radera publiceringsprofilen från Cloud Shell när den ligger i GitHub:  rm $PROFILE_FILE
MSG
