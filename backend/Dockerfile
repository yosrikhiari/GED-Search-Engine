# ── Build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["GED.API/GED.API.csproj",            "GED.API/"]
COPY ["GED.Core/GED.Core.csproj",          "GED.Core/"]
COPY ["GED.Infrastructure/GED.Infrastructure.csproj", "GED.Infrastructure/"]

RUN dotnet restore "GED.API/GED.API.csproj"

COPY . .

RUN dotnet publish "GED.API/GED.API.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# Install runtime dependencies
RUN apt-get update && apt-get install -y \
    ocrmypdf \
    tesseract-ocr \
    tesseract-ocr-eng \
    tesseract-ocr-fra \
    tesseract-ocr-ara \
    poppler-utils \
    curl \
    && rm -rf /var/lib/apt/lists/* \
    && apt-get clean

# Create non-root user for security
RUN groupadd --gid 1000 gedgroup && \
    useradd --uid 1000 --gid 1000 --shell /bin/bash --create-home geduser

# Create application directories with proper permissions
RUN mkdir -p /var/lib/ged/documents \
             /var/lib/ged \
             /app/logs \
             && chown -R geduser:gedgroup /var/lib/ged \
             && chown -R geduser:gedgroup /app/logs

USER geduser

WORKDIR /app

COPY --from=build --chown=geduser:gedgroup /app/publish .

# Set environment variables
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Expose API port
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "GED.API.dll"]
