# Built remotely in Azure Container Registry by azd (docker.remoteBuild); the build context is
# the repo root so the shared props, the League project, and the data folders are available.
# SDK tag matches global.json (rollForward latestPatch would not accept a newer feature band).
FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props nuget.config ./
COPY src/Directory.Build.props src/
COPY src/Swankers.League/ src/Swankers.League/
COPY src/Swankers.Mcp/ src/Swankers.Mcp/
RUN dotnet restore src/Swankers.Mcp/Swankers.Mcp.csproj --locked-mode
RUN dotnet publish src/Swankers.Mcp/Swankers.Mcp.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
# Real snapshot and demo scenarios ship in the image; sim state lives on the container's disk
# (one replica; a restart reseeds from the snapshot, which is what demo/reset.ps1 does anyway).
COPY data/snapshot/ /app/data/snapshot/
COPY data/demo/ /app/data/demo/
COPY data/franchise-names.json /app/data/franchise-names.json
RUN mkdir -p /app/data/sim && chown $APP_UID /app/data/sim
ENV ASPNETCORE_URLS=http://+:8080 \
    Mcp__SnapshotRoot=/app/data/snapshot \
    Mcp__StateDirectory=/app/data/sim \
    Mcp__ScenarioRoot=/app/data/demo
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Swankers.Mcp.dll"]
