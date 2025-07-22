using System;
using IngestaArchivosAPI.Models;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace IngestaArchivosAPI.Utils;

public class DbUtils
{
    private readonly IConfiguration _configuration;

    public DbUtils(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public NpgsqlConnection GetConnection()
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("DefaultConnection is not configured");
        }

        return new NpgsqlConnection(connectionString);
    }


    public int? ObtenerArchivoIdPorRuta(string rutaMinio)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = new NpgsqlCommand(
            "SELECT archivo_id FROM proceso_ocr WHERE ruta_minio LIKE @ruta", conn);
        cmd.Parameters.AddWithValue("ruta", $"%{rutaMinio}%");

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return reader.IsDBNull(0) ? null : reader.GetInt32(0);
        }
        return null;
    }
    public (int officeId, string odCompanyName, string softName)? ObtenerDatosArchivo(int archivoId)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = new NpgsqlCommand(@"
            SELECT office_id, od_company_name, soft_name
            FROM archivo_ingestado
            INNER JOIN office_demographic ON od_id = office_id
            INNER JOIN office_software ON os_od_id = office_id
            INNER JOIN software ON software.soft_id = office_software.os_soft_id
            WHERE id = @archivoId AND os_is_active = TRUE
        ", conn);
        cmd.Parameters.AddWithValue("archivoId", archivoId);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return (
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2)
            );
        }
        return null;
    }
    public Assistant? ObtenerAssistantIdPorOficina(int oficinaId)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = new NpgsqlCommand(@"
        SELECT id, oficina_id, nombre_oficina, software_origen, assistant_id, fecha_creacion, activo, vector_store_id
        FROM asistente_oficina
        WHERE oficina_id = @oficinaId
        LIMIT 1 ", conn);
        cmd.Parameters.AddWithValue("oficinaId", oficinaId);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new Assistant
            {
                id = reader.GetGuid(0),
                oficina_id = reader.GetInt32(1),
                nombre_oficina = reader.GetString(2),
                software_origen = reader.GetString(3),
                assistant_id = reader.GetString(4),
                fecha_creacion = reader.GetDateTime(5),
                activo = reader.GetBoolean(6),
                vector_store_id = reader.GetString(7)
            };
        }
        return null;
    }

    public void RegistrarAssistantEnBd(int oficinaId, string nombreOficina, string softwareOrigen, string assistantId, string vectorStoreId, string modelId)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = new NpgsqlCommand(@"
            INSERT INTO asistente_oficina (oficina_id, nombre_oficina, software_origen, assistant_id, vector_store_id, model_id)
            VALUES (@oficinaId, @nombreOficina, @softwareOrigen, @assistantId, @vectorStoreId, @modelId)
        ", conn);
        cmd.Parameters.AddWithValue("oficinaId", oficinaId);
        cmd.Parameters.AddWithValue("nombreOficina", nombreOficina);
        cmd.Parameters.AddWithValue("softwareOrigen", softwareOrigen);
        cmd.Parameters.AddWithValue("assistantId", assistantId);
        cmd.Parameters.AddWithValue("vectorStoreId", vectorStoreId);
        cmd.Parameters.AddWithValue("modelId", modelId ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Elimina un asistente de la base de datos
    /// </summary>
    public void EliminarAssistantDeBd(Guid assistantId)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = new NpgsqlCommand(@"
            DELETE FROM asistente_oficina
            WHERE id = @assistantId
        ", conn);
        cmd.Parameters.AddWithValue("assistantId", assistantId);
        cmd.ExecuteNonQuery();
        
        Console.WriteLine($"🗑️ Asistente {assistantId} eliminado de la base de datos");
    }

    public string? ExisteArchivoOpenAi(string archivoNombre)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = new NpgsqlCommand(@"
            SELECT file_id FROM archivo_openai
            WHERE archivo_nombre = @archivoNombre
        ", conn);
        cmd.Parameters.AddWithValue("archivoNombre", archivoNombre);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return reader.IsDBNull(0) ? null : reader.GetString(0);
        }
        return null;
    }


    public void RegistrarArchivoOpenAi(
        string archivoNombre,
        string fileId,
        string assistantId,
        string threadId,
        long bytesArchivo,
        long fechaOpenAiEpoch,
        string status,
        string purpose = "assistants")
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = new NpgsqlCommand(@"
            INSERT INTO archivo_openai (
                archivo_nombre, file_id, assistant_id, thread_id,
                bytes, fecha_openai, status, purpose
            ) VALUES (
                @archivoNombre, @fileId, @assistantId, @threadId,
                @bytesArchivo, @fechaOpenAi, @status, @purpose
            )
            ON CONFLICT (archivo_nombre) DO NOTHING
        ", conn);
        cmd.Parameters.AddWithValue("archivoNombre", archivoNombre);
        cmd.Parameters.AddWithValue("fileId", fileId);
        cmd.Parameters.AddWithValue("assistantId", assistantId);
        cmd.Parameters.AddWithValue("threadId", threadId);
        cmd.Parameters.AddWithValue("bytesArchivo", bytesArchivo);
        cmd.Parameters.AddWithValue("fechaOpenAi", DateTimeOffset.FromUnixTimeSeconds(fechaOpenAiEpoch).UtcDateTime);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("purpose", purpose);
        cmd.ExecuteNonQuery();
    }
    public string? ObtenerPromptPorOficina(int oficinaId)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = new NpgsqlCommand(@"
            SELECT contenido FROM prompt_ia
            WHERE oficina_id = @oficinaId
            ORDER BY fecha_creacion DESC
            LIMIT 1
        ", conn);
        cmd.Parameters.AddWithValue("oficinaId", oficinaId);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return reader.IsDBNull(0) ? null : reader.GetString(0);
        }
        return null;
    }

    public void GuardarExtraccionPendienteValidacion(
        int archivoId,
        string archivoNombre,
        int officeId,
        string inputText,
        string openaiResponse,
        string promptUsed,
        string assistantId,
        string threadId,
        string runId)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = new NpgsqlCommand(@"
            INSERT INTO extraction_validations (
                archivo_id, archivo_nombre, office_id, input_text, 
                openai_response, prompt_used, assistant_id, thread_id, 
                run_id, status, created_at, updated_at
            ) VALUES (
                @archivoId, @archivoNombre, @officeId, @inputText,
                @openaiResponse, @promptUsed, @assistantId, @threadId,
                @runId, @status, @createdAt, @updatedAt
            )
        ", conn);
        
        cmd.Parameters.AddWithValue("archivoId", archivoId);
        cmd.Parameters.AddWithValue("archivoNombre", archivoNombre);
        cmd.Parameters.AddWithValue("officeId", officeId);
        cmd.Parameters.AddWithValue("inputText", inputText);
        cmd.Parameters.AddWithValue("openaiResponse", openaiResponse);
        cmd.Parameters.AddWithValue("promptUsed", promptUsed);
        cmd.Parameters.AddWithValue("assistantId", assistantId);
        cmd.Parameters.AddWithValue("threadId", threadId);
        cmd.Parameters.AddWithValue("runId", runId);
        cmd.Parameters.AddWithValue("status", ValidationStatus.PendingUserValidation.ToString());
        cmd.Parameters.AddWithValue("createdAt", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("updatedAt", DateTime.UtcNow);
        
        cmd.ExecuteNonQuery();
    }

    public List<ExtractionValidation> ObtenerExtraccionesPendientesValidacion(int? officeId = null, int limit = 100)
    {
        var extracciones = new List<ExtractionValidation>();
        
        using var conn = GetConnection();
        conn.Open();
        
        string query = @"
            SELECT id, archivo_id, archivo_nombre, office_id, input_text, 
                   openai_response, prompt_used, assistant_id, thread_id, run_id,
                   status, corrected_json, validated_by, validated_at, 
                   validation_notes, created_at, updated_at
            FROM extraction_validations 
            WHERE status = @status";
        
        if (officeId.HasValue)
        {
            query += " AND office_id = @officeId";
        }
        
        query += " ORDER BY created_at DESC LIMIT @limit";
        
        using var cmd = new NpgsqlCommand(query, conn);
        cmd.Parameters.AddWithValue("status", ValidationStatus.PendingUserValidation.ToString());
        
        if (officeId.HasValue)
        {
            cmd.Parameters.AddWithValue("officeId", officeId.Value);
        }
        
        cmd.Parameters.AddWithValue("limit", limit);
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            extracciones.Add(new ExtractionValidation
            {
                id = reader.GetGuid(0),
                archivo_id = reader.GetInt32(1),
                archivo_nombre = reader.GetString(2),
                office_id = reader.GetInt32(3),
                input_text = reader.GetString(4),
                openai_response = reader.GetString(5),
                prompt_used = reader.GetString(6),
                assistant_id = reader.GetString(7),
                thread_id = reader.GetString(8),
                run_id = reader.GetString(9),
                status = Enum.Parse<ValidationStatus>(reader.GetString(10)),
                corrected_json = reader.IsDBNull(11) ? null : reader.GetString(11),
                validated_by = reader.IsDBNull(12) ? null : reader.GetString(12),
                validated_at = reader.IsDBNull(13) ? null : reader.GetDateTime(13),
                validation_notes = reader.IsDBNull(14) ? null : reader.GetString(14),
                created_at = reader.GetDateTime(15),
                updated_at = reader.GetDateTime(16)
            });
        }
        
        return extracciones;
    }

    public void ValidarExtraccion(
        Guid extractionId, 
        ValidationStatus status, 
        string validatedBy, 
        string? correctedJson = null, 
        string? validationNotes = null)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = new NpgsqlCommand(@"
            UPDATE extraction_validations 
            SET status = @status, 
                corrected_json = @correctedJson,
                validated_by = @validatedBy,
                validated_at = @validatedAt,
                validation_notes = @validationNotes,
                updated_at = @updatedAt
            WHERE id = @extractionId
        ", conn);
        
        cmd.Parameters.AddWithValue("extractionId", extractionId);
        cmd.Parameters.AddWithValue("status", status.ToString());
        cmd.Parameters.AddWithValue("correctedJson", correctedJson ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("validatedBy", validatedBy);
        cmd.Parameters.AddWithValue("validatedAt", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("validationNotes", validationNotes ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("updatedAt", DateTime.UtcNow);
        
        cmd.ExecuteNonQuery();
    }

    public List<ExtractionValidation> ObtenerExtraccionesValidadasCorrectas(int? officeId = null, int limit = 1000)
    {
        var extracciones = new List<ExtractionValidation>();
        
        using var conn = GetConnection();
        conn.Open();
        
        string query = @"
            SELECT id, archivo_id, archivo_nombre, office_id, input_text, 
                   openai_response, prompt_used, assistant_id, thread_id, run_id,
                   status, corrected_json, validated_by, validated_at, 
                   validation_notes, created_at, updated_at
            FROM extraction_validations 
            WHERE status IN (@statusCorrect, @statusCorrected)";
        
        if (officeId.HasValue)
        {
            query += " AND office_id = @officeId";
        }
        
        query += " ORDER BY validated_at DESC LIMIT @limit";
        
        using var cmd = new NpgsqlCommand(query, conn);
        cmd.Parameters.AddWithValue("statusCorrect", ValidationStatus.ValidatedCorrect.ToString());
        cmd.Parameters.AddWithValue("statusCorrected", ValidationStatus.Corrected.ToString());
        
        if (officeId.HasValue)
        {
            cmd.Parameters.AddWithValue("officeId", officeId.Value);
        }
        
        cmd.Parameters.AddWithValue("limit", limit);
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            extracciones.Add(new ExtractionValidation
            {
                id = reader.GetGuid(0),
                archivo_id = reader.GetInt32(1),
                archivo_nombre = reader.GetString(2),
                office_id = reader.GetInt32(3),
                input_text = reader.GetString(4),
                openai_response = reader.GetString(5),
                prompt_used = reader.GetString(6),
                assistant_id = reader.GetString(7),
                thread_id = reader.GetString(8),
                run_id = reader.GetString(9),
                status = Enum.Parse<ValidationStatus>(reader.GetString(10)),
                corrected_json = reader.IsDBNull(11) ? null : reader.GetString(11),
                validated_by = reader.IsDBNull(12) ? null : reader.GetString(12),
                validated_at = reader.IsDBNull(13) ? null : reader.GetDateTime(13),
                validation_notes = reader.IsDBNull(14) ? null : reader.GetString(14),
                created_at = reader.GetDateTime(15),
                updated_at = reader.GetDateTime(16)
            });
        }
        
        return extracciones;
    }

    // ===== MÉTODOS PARA CONFIGURACIÓN DE MODELOS =====

    public OfficeModelConfig? ObtenerConfiguracionModeloPorOficina(int officeId)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = new NpgsqlCommand(@"
            SELECT id, office_id, model_name, is_fine_tuned, fine_tuned_model_id,
                   created_at, updated_at, created_by, notes
            FROM office_model_config
            WHERE office_id = @officeId
        ", conn);
        cmd.Parameters.AddWithValue("officeId", officeId);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new OfficeModelConfig
            {
                id = reader.GetGuid(0),
                office_id = reader.GetInt32(1),
                model_name = reader.GetString(2),
                is_fine_tuned = reader.GetBoolean(3),
                fine_tuned_model_id = reader.IsDBNull(4) ? null : reader.GetString(4),
                created_at = reader.GetDateTime(5),
                updated_at = reader.GetDateTime(6),
                created_by = reader.IsDBNull(7) ? null : reader.GetString(7),
                notes = reader.IsDBNull(8) ? null : reader.GetString(8)
            };
        }
        return null;
    }

    public string ObtenerModeloParaOficina(int officeId)
    {
        var config = ObtenerConfiguracionModeloPorOficina(officeId);
        if (config != null)
        {
            return config.GetModelToUse();
        }
        
        // Si no hay configuración, usar modelo por defecto
        return  "gpt-4o";
    }

    public void ConfigurarModeloParaOficina(
        int officeId, 
        string modelName, 
        bool isFineTuned = false, 
        string? fineTunedModelId = null, 
        string? createdBy = null, 
        string? notes = null)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = new NpgsqlCommand(@"
            INSERT INTO office_model_config (
                office_id, model_name, is_fine_tuned, fine_tuned_model_id, 
                created_by, notes, created_at, updated_at
            ) VALUES (
                @officeId, @modelName, @isFineTuned, @fineTunedModelId,
                @createdBy, @notes, @createdAt, @updatedAt
            )
            ON CONFLICT (office_id) DO UPDATE SET
                model_name = @modelName,
                is_fine_tuned = @isFineTuned,
                fine_tuned_model_id = @fineTunedModelId,
                created_by = @createdBy,
                notes = @notes,
                updated_at = @updatedAt
        ", conn);
        
        cmd.Parameters.AddWithValue("officeId", officeId);
        cmd.Parameters.AddWithValue("modelName", modelName);
        cmd.Parameters.AddWithValue("isFineTuned", isFineTuned);
        cmd.Parameters.AddWithValue("fineTunedModelId", fineTunedModelId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("createdBy", createdBy ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("notes", notes ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("createdAt", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("updatedAt", DateTime.UtcNow);
        
        cmd.ExecuteNonQuery();
    }

    public List<OfficeModelConfig> ObtenerTodasLasConfiguracionesModelo()
    {
        var configuraciones = new List<OfficeModelConfig>();
        
        using var conn = GetConnection();
        conn.Open();
        using var cmd = new NpgsqlCommand(@"
            SELECT id, office_id, model_name, is_fine_tuned, fine_tuned_model_id,
                   created_at, updated_at, created_by, notes
            FROM office_model_config
            ORDER BY office_id
        ", conn);
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            configuraciones.Add(new OfficeModelConfig
            {
                id = reader.GetGuid(0),
                office_id = reader.GetInt32(1),
                model_name = reader.GetString(2),
                is_fine_tuned = reader.GetBoolean(3),
                fine_tuned_model_id = reader.IsDBNull(4) ? null : reader.GetString(4),
                created_at = reader.GetDateTime(5),
                updated_at = reader.GetDateTime(6),
                created_by = reader.IsDBNull(7) ? null : reader.GetString(7),
                notes = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }
        
        return configuraciones;
    }

    // ===== MÉTODOS PARA JOBS DE FINE-TUNING =====

    public void GuardarJobFineTuning(int officeId, string jobId, string fileId, int totalExamples, int jsonlSize)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = new NpgsqlCommand(@"
            INSERT INTO fine_tuning_jobs (
                office_id, job_id, file_id, total_examples, jsonl_size,
                status, created_at, updated_at
            ) VALUES (
                @officeId, @jobId, @fileId, @totalExamples, @jsonlSize,
                @status, @createdAt, @updatedAt
            )
        ", conn);
        
        cmd.Parameters.AddWithValue("officeId", officeId);
        cmd.Parameters.AddWithValue("jobId", jobId);
        cmd.Parameters.AddWithValue("fileId", fileId);
        cmd.Parameters.AddWithValue("totalExamples", totalExamples);
        cmd.Parameters.AddWithValue("jsonlSize", jsonlSize);
        cmd.Parameters.AddWithValue("status", "running");
        cmd.Parameters.AddWithValue("createdAt", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("updatedAt", DateTime.UtcNow);
        
        cmd.ExecuteNonQuery();
    }

    public void ActualizarEstadoJobFineTuning(string jobId, string status, string? fineTunedModelId)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = new NpgsqlCommand(@"
            UPDATE fine_tuning_jobs 
            SET status = @status, 
                fine_tuned_model_id = @fineTunedModelId,
                updated_at = @updatedAt
            WHERE job_id = @jobId
        ", conn);
        
        cmd.Parameters.AddWithValue("jobId", jobId);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("fineTunedModelId", fineTunedModelId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("updatedAt", DateTime.UtcNow);
        
        cmd.ExecuteNonQuery();
    }

    public List<FineTuningJobInfo> ObtenerJobsFineTuning(int? officeId = null)
    {
        var jobs = new List<FineTuningJobInfo>();
        
        using var conn = GetConnection();
        conn.Open();
        
        string query = @"
            SELECT office_id, job_id, file_id, total_examples, jsonl_size,
                   status, fine_tuned_model_id, created_at, updated_at
            FROM fine_tuning_jobs";
        
        if (officeId.HasValue)
        {
            query += " WHERE office_id = @officeId";
        }
        
        query += " ORDER BY created_at DESC";
        
        using var cmd = new NpgsqlCommand(query, conn);
        
        if (officeId.HasValue)
        {
            cmd.Parameters.AddWithValue("officeId", officeId.Value);
        }
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            jobs.Add(new FineTuningJobInfo
            {
                OfficeId = reader.GetInt32(0),
                JobId = reader.GetString(1),
                FileId = reader.GetString(2),
                TotalExamples = reader.GetInt32(3),
                JsonlSize = reader.GetInt32(4),
                Status = reader.GetString(5),
                FineTunedModelId = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt = reader.GetDateTime(7),
                UpdatedAt = reader.GetDateTime(8)
            });
        }
        
        return jobs;
    }

    // Nuevo: Obtener vector_store_id por oficina
    public string? ObtenerVectorStoreIdPorOficina(int oficinaId)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = new NpgsqlCommand(@"
            SELECT vector_store_id FROM asistente_oficina
            WHERE oficina_id = @oficinaId
            LIMIT 1 ", conn);
        cmd.Parameters.AddWithValue("oficinaId", oficinaId);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return reader.IsDBNull(0) ? null : reader.GetString(0);
        }
        return null;
    }

    // Nuevo: Registrar vector_store_id para una oficina existente
    public void ActualizarVectorStoreIdPorOficina(int oficinaId, string vectorStoreId)
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = new NpgsqlCommand(@"
            UPDATE asistente_oficina SET vector_store_id = @vectorStoreId
            WHERE oficina_id = @oficinaId
        ", conn);
        cmd.Parameters.AddWithValue("oficinaId", oficinaId);
        cmd.Parameters.AddWithValue("vectorStoreId", vectorStoreId);
        cmd.ExecuteNonQuery();
    }
}

public class FineTuningJobInfo
{
    public int OfficeId { get; set; }
    public string JobId { get; set; } = string.Empty;
    public string FileId { get; set; } = string.Empty;
    public int TotalExamples { get; set; }
    public int JsonlSize { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? FineTunedModelId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
