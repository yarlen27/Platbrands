# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is IngestaArchivosAPI, a .NET 8 Web API that processes document files through OCR and AI extraction. The application integrates with OpenAI services, MinIO object storage, and PostgreSQL database to handle document ingestion, processing, and data extraction workflows.

## Key Architecture

- **ASP.NET Core 8 Web API** with controllers in `src/IngestaArchivosAPI/Controllers/`
- **Entity Framework Core** with PostgreSQL using `ApplicationDbContext` in `src/IngestaArchivosAPI/Data/`
- **Business Logic Layer** in `src/IngestaArchivosAPI/BLL/` (primarily `ArchivoService`)
- **External Services Integration**:
  - OpenAI API for document processing and AI extraction
  - MinIO object storage for file management
  - External OCR service endpoint
- **Background Services** for monitoring fine-tuning jobs
- **Docker containerization** with docker-compose deployment

## Development Commands

### Local Development
```bash
# Run the application locally
cd src/IngestaArchivosAPI
dotnet run

# Run with specific profile
dotnet run --launch-profile http   # localhost:5039
dotnet run --launch-profile https  # localhost:7022
```

### Building and Testing
```bash
# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Publish for release
dotnet publish -c Release -o /app/publish
```

### Docker Development
```bash
cd src/IngestaArchivosAPI

# Build and run with docker-compose
docker compose up -d --build

# View logs
docker compose logs

# Stop containers
docker compose down
```

## Deployment

### Production Deployment
```bash
# Automated deployment to production server
./deploy.sh

# Manual deployment parameters
./deploy.sh [environment] [server_user] [server_host] [branch]
```

The deployment script automatically:
- Connects to production server via SSH
- Updates code from Git repository
- Rebuilds and restarts Docker containers
- Performs health checks

### Environment Configuration

Key configuration files:
- `appsettings.json` - Base configuration
- `appsettings.Development.json` - Development overrides
- `appsettings.Production.json` - Production overrides

Required environment variables for production:
- `OPENAI_API_KEY` - OpenAI API key
- `ASPNETCORE_ENVIRONMENT` - Set to "Production"

## Core Data Models

### Primary Entities (`src/IngestaArchivosAPI/Data/`)
- `ArchivoIngestado` - Main file ingestion records
- `ProcesoOcr` - OCR processing status and results
- `Assistant` - OpenAI assistant configurations per office
- `PromptIa` - AI prompt templates
- `ExtractionValidation` - Validation results for extracted data
- `FineTuningJob` - OpenAI fine-tuning job tracking

### Business Models (`src/IngestaArchivosAPI/Models/`)
- `Transaction` / `TransactionHeader` - Financial transaction data
- `AssistantResponse` - AI processing responses
- `OfficeModelConfig` - Office-specific AI model configurations

## Key Services and Utilities

### Services (`src/IngestaArchivosAPI/Services/`)
- `OpenAIServiceSimple` - OpenAI API integration
- `MinioService` - Object storage operations
- `FineTuningMonitorService` - Background service for monitoring AI training

### Utilities (`src/IngestaArchivosAPI/Utils/`)
- `OpenAIUtils` - OpenAI helper functions and prompts
- `MinioUtils` - File storage utilities
- `DbUtils` - Database helper operations
- `FileUtils` - File processing utilities
- `FineTuningUtils` - AI model training utilities

## API Endpoints

Main controllers:
- `/api/archivos/{office_id}/{userId}` - File upload and processing
- `/api/prompts/` - AI prompt management
- `/api/test/` - Health checks and testing endpoints

## Database Schema

The application uses PostgreSQL with Entity Framework migrations. Key tables:
- `archivos_ingestados` - File ingestion tracking
- `proceso_ocr` - OCR processing results
- `asistente_oficina` - Assistant configurations per office
- `prompts_ia` - AI prompt templates
- `extraction_validations` - Data validation results

## External Dependencies

- **PostgreSQL** database (port 55433 in dev)
- **MinIO** object storage (port 9002 in dev)
- **OpenAI API** for AI processing
- **External OCR service** (port 8081 in dev)

## File Processing Workflow

1. File upload via `ArchivosController`
2. Duplicate detection and storage via `MinioService`
3. OCR processing through external service
4. AI extraction using OpenAI assistant
5. Data validation and storage
6. Response with processing results and timings