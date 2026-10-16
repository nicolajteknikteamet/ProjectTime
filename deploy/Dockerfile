# Bygger och kör ProjectTime Server (webbgränssnitt + API för agenter).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ProjectTime.sln ./
COPY src/ProjectTime.Shared/ProjectTime.Shared.csproj src/ProjectTime.Shared/
COPY src/ProjectTime.Server/ProjectTime.Server.csproj src/ProjectTime.Server/
RUN dotnet restore src/ProjectTime.Server/ProjectTime.Server.csproj
COPY src/ src/
RUN dotnet publish src/ProjectTime.Server/ProjectTime.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends tzdata && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://0.0.0.0:5080 \
    PROJECTTIME_ProjectTime__DataDirectory=/data
VOLUME ["/data"]
EXPOSE 5080
HEALTHCHECK --interval=30s --timeout=3s CMD curl -fs http://localhost:5080/healthz || exit 1
ENTRYPOINT ["dotnet", "ProjectTime.Server.dll"]
