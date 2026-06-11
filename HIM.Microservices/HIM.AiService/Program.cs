using HIM.AiService.Extenstions;
using HIM.AiService.Models.AI;
using HIM.AiService.Services.AI.Interface;

var builder = WebApplication.CreateBuilder(args);

// Bind Configuration (Options Pattern - SOLID: Dependency Inversion)
builder.Services.Configure<AiSettings>(builder.Configuration.GetSection(nameof(AiSettings)));

// Add services to the container.
builder.Services.AddServices();

builder.Services.AddControllers();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var kbService = scope.ServiceProvider.GetRequiredService<IKnowledgeBaseService>();
    _ = kbService.InitializeAsync();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
