# Stage 1: Base runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS base
WORKDIR /app
EXPOSE 8080
# Chiseled: no shell, no root user, no package manager (~80MB)
# Default user: $APP_UID (non-root)

# Stage 2: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files FIRST for layer caching
COPY ["src/OrderService.Api/OrderService.Api.csproj", "src/OrderService.Api/"]
COPY ["src/OrderService.Domain/OrderService.Domain.csproj", "src/OrderService.Domain/"]
COPY ["src/OrderService.Application/OrderService.Application.csproj", "src/OrderService.Application/"]
COPY ["src/OrderService.Infrastructure/OrderService.Infrastructure.csproj", "src/OrderService.Infrastructure/"]
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["NuGet.Config", "."]

RUN dotnet restore "src/OrderService.Api/OrderService.Api.csproj"

# Copy everything else
COPY . .

RUN dotnet build "src/OrderService.Api/OrderService.Api.csproj" \
    -c Release --no-restore

# Stage 3: Publish. Tests run in CI before the image is built, not in a stage of it.
FROM build AS publish
RUN dotnet publish "src/OrderService.Api/OrderService.Api.csproj" \
    -c Release --no-build -o /app/publish \
    /p:UseAppHost=false

# Stage 4: Final runtime
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# No HEALTHCHECK: a chiseled image has no shell and no curl, and Docker and Compose run health checks
# inside the container. Kubernetes probes call /health/live from outside (kubernetes-dotnet).
ENTRYPOINT ["dotnet", "OrderService.Api.dll"]
