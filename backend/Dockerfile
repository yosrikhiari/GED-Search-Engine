# ── Stage 1: Build ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# Copy solution + project files first (layer caching for dotnet restore)
COPY GED.sln ./
COPY GED.API/GED.API.csproj           GED.API/
COPY GED.Core/GED.Core.csproj         GED.Core/
COPY GED.Infrastructure/GED.Infrastructure.csproj GED.Infrastructure/

RUN dotnet restore GED.sln

# Copy full source then publish
COPY . .
RUN dotnet publish GED.API/GED.API.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ── Stage 2: Runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

WORKDIR /app

# ── System dependencies ───────────────────────────────────────────────────────
RUN apt-get update && apt-get install -y --no-install-recommends \
    # Tesseract OCR engine + English language data (required for image OCR)
    tesseract-ocr \
    tesseract-ocr-eng \
    # Extra languages — add/remove as needed
    tesseract-ocr-fra \
    tesseract-ocr-spa \
    # libgdiplus — needed by SixLabors.ImageSharp on Linux
    libgdiplus \
    # libfontconfig — needed by iText7 for PDF rendering
    libfontconfig1 \
    # curl — used by healthcheck scripts
    curl \
    && rm -rf /var/lib/apt/lists/*

RUN ldconfig && \
    ln -sf /usr/lib/x86_64-linux-gnu/liblept.so.5 /usr/lib/x86_64-linux-gnu/liblept.so && \
    ln -sf /usr/lib/x86_64-linux-gnu/libtesseract.so.5 /usr/lib/x86_64-linux-gnu/libtesseract.so

# ── Tessdata verification ─────────────────────────────────────────────────────
# Log the tessdata path at build time so we catch misconfiguration early
RUN ls -la /usr/share/tesseract-ocr/ && \
    echo "Tesseract version:" && tesseract --version && \
    echo "Available language files:" && \
    ls /usr/share/tesseract-ocr/*/tessdata/*.traineddata 2>/dev/null || \
    echo "WARNING: No tessdata files found at standard path!"

# ── Application ───────────────────────────────────────────────────────────────
COPY --from=build /app/publish .

# Create document storage directory
RUN mkdir -p /var/lib/ged/documents && \
    chmod 777 /var/lib/ged/documents

# Expose port
EXPOSE 8080

# Set tessdata path explicitly as env var (can be overridden in docker-compose)
ENV OCR__TessDataPath=/usr/share/tesseract-ocr/5/tessdata \
    ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "GED.API.dll"]