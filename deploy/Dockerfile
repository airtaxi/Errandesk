# syntax=docker/dockerfile:1
# Build context: the repository root (see compose.yaml).

FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /source

COPY global.json Directory.Build.props ./
COPY src/Errandesk.Contracts/Errandesk.Contracts.csproj src/Errandesk.Contracts/
COPY src/Errandesk.Server/Errandesk.Server.csproj src/Errandesk.Server/
RUN dotnet restore src/Errandesk.Server/Errandesk.Server.csproj

COPY src/Errandesk.Contracts/ src/Errandesk.Contracts/
COPY src/Errandesk.Server/ src/Errandesk.Server/
RUN dotnet publish src/Errandesk.Server/Errandesk.Server.csproj --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Errandesk.Server.dll"]
