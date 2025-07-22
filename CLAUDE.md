# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is IngestaArchivosAPI, a simplified .NET 8 Web API that processes PDF documents through OCR extraction. The application focuses solely on extracting structured text from PDF files using an external OCR service.

## Key Architecture

- **ASP.NET Core 8 Web API** with controllers in `src/IngestaArchivosAPI/Controllers/`
- **Business Logic Layer** in `src/IngestaArchivosAPI/BLL/` (primarily `ArchivoService`)
- **External Services Integration**:
  - External OCR service endpoint for PDF text extraction
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

## Configuration

### Environment Configuration

Key configuration files:
- `appsettings.Example.json` - Example configuration template
- `appsettings.Development.json` - Development overrides (not tracked)
- `appsettings.Production.json` - Production overrides (not tracked)

Required configuration:
- `OCR:Endpoint` - URL of the external OCR service
- `ASPNETCORE_ENVIRONMENT` - Set to "Development" or "Production"

## API Endpoints

### Main Controller
- `POST /api/archivos/{office_id}/{userId}` - PDF upload and OCR processing
  - Accepts PDF files up to 10MB
  - Returns structured text extracted via OCR
  - Response includes timing information and extracted text
- `GET /api/archivos/version` - Application version information

### Test Endpoints
- `GET /api/test/health` - Simple health check endpoint

## External Dependencies

- **External OCR service** - Processes PDF files and extracts structured text
  - Default endpoint: `http://localhost:8081/extract-lines`
  - Configurable via `OCR:Endpoint` setting

## PDF Processing Workflow

**Simplified workflow for PDF-only processing:**

1. **File Upload** - PDF file uploaded via `POST /api/archivos/{office_id}/{userId}`
2. **Validation** - Verify file is PDF format (rejects non-PDF files)
3. **OCR Processing** - Send PDF to external OCR service
4. **Text Extraction** - Parse OCR response and extract `reconstructed_text` field
5. **Response** - Return structured response with:
   - `success` - Boolean indicating success/failure
   - `extractedText` - Full text content extracted from PDF
   - `textLength` - Character count of extracted text
   - `totalSeconds` - Total processing time
   - `timings` - Detailed timing breakdown

## Key Implementation Notes

- **PDF-Only Processing**: Application now only accepts PDF files, rejects all other formats
- **No Database Dependencies**: Removed all database, OpenAI, and MinIO integrations
- **Simplified Dependencies**: Only requires HTTP client for OCR service communication
- **OCR Integration**: Sends PDF as multipart form data to external OCR endpoint
- **Error Handling**: Comprehensive error handling with detailed logging throughout OCR process