@echo off
set ConnectionStrings__DefaultConnection=Server=localhost,1433;Database=ged_db;User Id=sa;Password=GedPass_2024!;TrustServerCertificate=True
set RabbitMQ__Host=localhost
set RabbitMQ__Port=5673
set RabbitMQ__Username=admin
set RabbitMQ__Password=admin123
set OpenSearch__Url=http://localhost:9200
set OpenSearch__Username=admin
set OpenSearch__Password=GedOpensearch2024!
set Redis__Enabled=true
set Redis__ConnectionString=localhost:6379
set ASPNETCORE_ENVIRONMENT=Development
set ASPNETCORE_URLS=http://+:5001
cd /d "%~dp0GED.API"
dotnet run --environment Development
