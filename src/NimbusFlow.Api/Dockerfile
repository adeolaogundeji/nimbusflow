FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props NimbusFlow.slnx ./
COPY src/NimbusFlow.Domain/NimbusFlow.Domain.csproj src/NimbusFlow.Domain/packages.lock.json src/NimbusFlow.Domain/
COPY src/NimbusFlow.Application/NimbusFlow.Application.csproj src/NimbusFlow.Application/packages.lock.json src/NimbusFlow.Application/
COPY src/NimbusFlow.Infrastructure/NimbusFlow.Infrastructure.csproj src/NimbusFlow.Infrastructure/packages.lock.json src/NimbusFlow.Infrastructure/
COPY src/NimbusFlow.Api/NimbusFlow.Api.csproj src/NimbusFlow.Api/packages.lock.json src/NimbusFlow.Api/
RUN dotnet restore src/NimbusFlow.Api/NimbusFlow.Api.csproj --locked-mode

COPY src/ src/
RUN dotnet publish src/NimbusFlow.Api/NimbusFlow.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "NimbusFlow.Api.dll"]
