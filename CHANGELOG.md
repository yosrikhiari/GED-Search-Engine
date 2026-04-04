# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Fixed
- ollama-pull docker-compose command syntax was broken (passed /bin/sh to ollama binary instead of using it as entrypoint) — fixed with correct sh -c wrapper
- Database schema initialization on fresh deploy was incomplete — init_schema.sql now includes DateConfidenceScore column and all required columns
- RabbitMQ health check was removed during testing — restored with Lazy<IConnection> to handle startup ordering correctly
- Health check endpoint was missing RabbitMQ and OpenSearch entries — both added with correct tags

### Changed
- Embedding model switched from nomic-embed-text to bge-m3 (vector dimensions 768 → 1024)
- All config files, appsettings.json, .env, and docker-compose.yml updated to reflect bge-m3
- OpenSearch index mapping updated to 1024 dimensions
- EF Core migration FixMissingColumns added for existing database compatibility