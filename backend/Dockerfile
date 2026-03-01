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
WORKDIR /app

RUN apt-get update && apt-get install -y \
    ocrmypdf \
    tesseract-ocr \
    tesseract-ocr-eng \
    && rm -rf /var/lib/apt/lists/*

# Create document storage directory
RUN mkdir -p /var/lib/ged/documents \
 && mkdir -p /var/lib/ged

COPY --from=build /app/publish .

# Expose API port
EXPOSE 8080

ENTRYPOINT ["dotnet", "GED.API.dll"]
