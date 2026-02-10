# Backend Dockerfile for .NET 8 application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY ["GED.API/GED.API.csproj", "GED.API/"]
COPY ["GED.Core/GED.Core.csproj", "GED.Core/"]
COPY ["GED.Infrastructure/GED.Infrastructure.csproj", "GED.Infrastructure/"]
RUN dotnet restore "GED.API/GED.API.csproj"

# Copy everything else and build
COPY . .
WORKDIR "/src/GED.API"
RUN dotnet build "GED.API.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "GED.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final stage - runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Install Tesseract OCR and dependencies
RUN apt-get update && apt-get install -y \
    tesseract-ocr \
    tesseract-ocr-eng \
    tesseract-ocr-fra \
    tesseract-ocr-spa \
    tesseract-ocr-deu \
    libtesseract-dev \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Create directories for document storage
RUN mkdir -p /var/lib/ged/documents /app/logs

# Copy published application
COPY --from=publish /app/publish .

# Expose port
EXPOSE 8080

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=30s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

# Run the application
ENTRYPOINT ["dotnet", "GED.API.dll"]
