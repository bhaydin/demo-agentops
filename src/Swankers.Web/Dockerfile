# Built remotely in Azure Container Registry by azd (docker.remoteBuild); build context is the repo root.
# SDK tag matches global.json (rollForward latestPatch would not accept a newer feature band).
FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props nuget.config ./
COPY src/Directory.Build.props src/
COPY src/Swankers.Web/ src/Swankers.Web/
RUN dotnet restore src/Swankers.Web/Swankers.Web.csproj --locked-mode
RUN dotnet publish src/Swankers.Web/Swankers.Web.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Swankers.Web.dll"]
