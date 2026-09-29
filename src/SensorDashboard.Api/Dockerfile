# syntax=docker/dockerfile:1
# Build context: repository root (docker compose sets it).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so package layers are cached until a project file changes.
COPY Directory.Build.props ./
COPY src/IoTDigitalTwin.Contracts/IoTDigitalTwin.Contracts.csproj src/IoTDigitalTwin.Contracts/
COPY src/SensorDashboard.Api/SensorDashboard.Api.csproj src/SensorDashboard.Api/
RUN dotnet restore src/SensorDashboard.Api/SensorDashboard.Api.csproj

COPY src/IoTDigitalTwin.Contracts/ src/IoTDigitalTwin.Contracts/
COPY src/SensorDashboard.Api/ src/SensorDashboard.Api/
RUN dotnet publish src/SensorDashboard.Api/SensorDashboard.Api.csproj \
    -c Release -o /app --no-restore /p:UseAppHost=false

# EF Core migration bundle: a single executable that applies pending migrations.
FROM build AS bundle
RUN dotnet tool install --global dotnet-ef --version "10.0.*"
ENV PATH="${PATH}:/root/.dotnet/tools"
RUN dotnet ef migrations bundle \
    --project src/SensorDashboard.Api/SensorDashboard.Api.csproj \
    --configuration Release --output /bundle/efbundle

# One-shot migration job (compose's `migrate` service). Reads the connection string from
# ConnectionStrings__IoTDigitalTwin, like the API.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS migrator
WORKDIR /migrate
COPY --from=bundle /bundle/efbundle .
USER $APP_UID
ENTRYPOINT ["./efbundle"]

# The API (default target).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "SensorDashboard.Api.dll"]
