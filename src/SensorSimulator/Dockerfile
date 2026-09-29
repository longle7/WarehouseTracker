# syntax=docker/dockerfile:1
# Build context: repository root (docker compose sets it).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/IoTDigitalTwin.Contracts/IoTDigitalTwin.Contracts.csproj src/IoTDigitalTwin.Contracts/
COPY src/SensorSimulator/SensorSimulator.csproj src/SensorSimulator/
RUN dotnet restore src/SensorSimulator/SensorSimulator.csproj

COPY src/IoTDigitalTwin.Contracts/ src/IoTDigitalTwin.Contracts/
COPY src/SensorSimulator/ src/SensorSimulator/
RUN dotnet publish src/SensorSimulator/SensorSimulator.csproj \
    -c Release -o /app --no-restore /p:UseAppHost=false

# A worker needs only the .NET runtime, not ASP.NET Core.
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "SensorSimulator.dll"]
