# =============================================================================
# Stage 1: Build
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy only project files first (layer caching)
COPY Directory.Packages.props .
COPY src/EmailBroker.Core/EmailBroker.Core.csproj    src/EmailBroker.Core/
COPY src/EmailBroker.Providers/EmailBroker.Providers.csproj src/EmailBroker.Providers/
COPY src/EmailBroker.Api/EmailBroker.Api.csproj      src/EmailBroker.Api/

# Restore (cached until project files change)
RUN dotnet restore src/EmailBroker.Api/EmailBroker.Api.csproj

# Copy source and build
COPY . .
RUN dotnet publish src/EmailBroker.Api/EmailBroker.Api.csproj \
    -c Release \
    -o /app \
    --no-restore

# =============================================================================
# Stage 2: Runtime
# =============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

# Install curl for health checks
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user for security (UID/GID 1000 = dokploy default)
RUN addgroup --system --gid 1000 app \
    && adduser --system --uid 1000 --gid 1000 app

WORKDIR /app

# Copy published artifacts from build stage
COPY --from=build /app .

RUN chown -R app:app /app

USER app

# Dokploy expects the app to listen on this port
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "EmailBroker.Api.dll"]
