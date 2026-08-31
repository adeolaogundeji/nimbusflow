FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props NimbusFlow.slnx ./
COPY src/NimbusFlow.Domain/NimbusFlow.Domain.csproj src/NimbusFlow.Domain/packages.lock.json src/NimbusFlow.Domain/
COPY src/NimbusFlow.Application/NimbusFlow.Application.csproj src/NimbusFlow.Application/packages.lock.json src/NimbusFlow.Application/
COPY src/NimbusFlow.Infrastructure/NimbusFlow.Infrastructure.csproj src/NimbusFlow.Infrastructure/packages.lock.json src/NimbusFlow.Infrastructure/
COPY src/NimbusFlow.Worker/NimbusFlow.Worker.csproj src/NimbusFlow.Worker/packages.lock.json src/NimbusFlow.Worker/
RUN dotnet restore src/NimbusFlow.Worker/NimbusFlow.Worker.csproj --locked-mode

COPY src/ src/
RUN dotnet publish src/NimbusFlow.Worker/NimbusFlow.Worker.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "NimbusFlow.Worker.dll"]
