using Chatbot.Api.Data;
using Chatbot.Api.Services;
using Chatbot.Api.Services.ExternalData;
using Microsoft.EntityFrameworkCore;
using Microsoft.ML;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton(new MLContext(seed: 42));
builder.Services.AddSingleton<IntentService>();
builder.Services.AddSingleton<IIntentClassifier>(services => services.GetRequiredService<IntentService>());
builder.Services.AddHttpClient();
builder.Services.AddScoped<KnowledgeService>();
builder.Services.AddScoped<BotMenuService>();
builder.Services.AddScoped<ChatbotService>();

// Harici veri sağlayıcıları. Yeni bir site için yeni bir IExternalDataProvider eklenip
// appsettings.json -> ExternalData:BotProviders altında bot'a bağlanır.
builder.Services.AddScoped<IExternalDataProvider, NexoraCatalogProvider>();
builder.Services.AddScoped<ExternalDataProviderRegistry>();

var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
                     ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("ChatbotUi", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors("ChatbotUi");
app.MapControllers();

// Açılışta veritabanı şeması en son migration'a getirilir (Data/Migrations),
// ardından boş kalan bot verileri Data/Seed altındaki dosyalardan doldurulur.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db, Path.Combine(app.Environment.ContentRootPath, "Data", "Seed"));
}

app.Run();
