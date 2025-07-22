using IngestaArchivosAPI.BLL;
using IngestaArchivosAPI.Data;
using IngestaArchivosAPI.Services;
using IngestaArchivosAPI.Utils;
using Microsoft.EntityFrameworkCore;
using Minio;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Entity Framework PostgreSQL
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Minio Client
var minioClient = new MinioClient()
    .WithEndpoint(builder.Configuration["Minio:Endpoint"])
    .WithCredentials(builder.Configuration["Minio:AccessKey"], builder.Configuration["Minio:SecretKey"])
    .WithSSL(false)
    .Build();

builder.Services.AddSingleton(minioClient);

// Services
builder.Services.AddScoped<MinioService>();
builder.Services.AddScoped<ArchivoService>();
builder.Services.AddScoped<DbUtils>();
builder.Services.AddScoped<OpenAIUtils>();
builder.Services.AddScoped<MinioUtils>();

// Background Services
builder.Services.AddHostedService<FineTuningMonitorService>();

// HttpClient para ArchivoService (para OCR) - Configurado con timeout extendido
builder.Services.AddHttpClient("OCR", client =>
{
    client.Timeout = TimeSpan.FromMinutes(15); // 15 minutos para OCR procesamiento
});

// HttpClient para OpenAI Services
builder.Services.AddHttpClient("OpenAI", client =>
{
    client.MaxResponseContentBufferSize = 50 * 1024 * 1024; // 50MB buffer
    client.Timeout = TimeSpan.FromMinutes(10); // 10 min timeout
});

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configurar JSON options globalmente
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

// CORS Configuration
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularDev", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowAngularDev");
app.UseHttpsRedirection();
app.MapControllers();

app.Run();
