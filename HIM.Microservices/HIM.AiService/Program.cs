using HIM.AiService.Extenstions;
using HIM.AiService.Models.AI;

var builder = WebApplication.CreateBuilder(args);

// Bind Configuration (Options Pattern - SOLID: Dependency Inversion)
builder.Services.Configure<AiSettings>(builder.Configuration.GetSection(nameof(AiSettings)));

// Add services to the container.
builder.Services.AddServices();

builder.Services.AddControllers();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
