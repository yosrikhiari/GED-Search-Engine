$env:ConnectionStrings__DefaultConnection="Server=localhost,1433;Database=ged_db;User Id=sa;Password=GedPass_2024!;TrustServerCertificate=True"
$env:RabbitMQ__Host="localhost"
$env:RabbitMQ__Port="5673"
$env:RabbitMQ__Username="admin"
$env:RabbitMQ__Password="admin123"
$env:OpenSearch__Url="http://localhost:9200"
$env:OpenSearch__Username="admin"
$env:OpenSearch__Password="GedOpensearch2024!"
$env:Redis__Enabled="true"
$env:Redis__ConnectionString="localhost:6379"
$env:ASPNETCORE_ENVIRONMENT="Development"
$env:ASPNETCORE_URLS="http://+:5001"

dotnet run
