using Microsoft.EntityFrameworkCore;
using SchId.Api.Data;
using SchId.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Permite que este mismo ejecutable corra como Windows Service cuando se
// instala con `sc create` / NSSM, o como consola normal al correrlo con
// `dotnet run`. No requiere cambios entre ambos modos.
builder.Host.UseWindowsService();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<SchIdDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("SchIdDatabase")));

builder.Services.Configure<ImageStorageOptions>(builder.Configuration.GetSection("ImageStorage"));
builder.Services.AddSingleton<IImageStorageService, ImageStorageService>();

builder.Services.Configure<RetentionOptions>(builder.Configuration.GetSection("Retention"));
builder.Services.AddHostedService<RetentionCleanupService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
