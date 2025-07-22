using IngestaArchivosAPI.BLL;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Services - Solo ArchivoService para OCR
builder.Services.AddScoped<ArchivoService>();

// HttpClient para ArchivoService (para OCR) - Configurado con timeout extendido
builder.Services.AddHttpClient("OCR", client =>
{
    client.Timeout = TimeSpan.FromMinutes(15); // 15 minutos para OCR procesamiento
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
