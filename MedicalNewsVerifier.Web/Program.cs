using MedicalNewsVerifier.Web.Data;
using MedicalNewsVerifier.Web.Services;
using MedicalNewsVerifier.Web.Services.Parsers;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews()
    .AddJsonOptions(o =>
    {
        var j = o.JsonSerializerOptions;
        j.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        j.PropertyNameCaseInsensitive = true;
    });
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IAnalysisJobStore, AnalysisJobStore>();
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection("Ollama"));
builder.Services.AddHttpClient<IOllamaComparisonClient, OllamaComparisonClient>((sp, http) =>
{
    var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OllamaOptions>>().Value;
    var baseUrl = string.IsNullOrWhiteSpace(opt.BaseUrl)
        ? "http://localhost:11434/v1/"
        : opt.BaseUrl.TrimEnd('/') + "/";
    http.BaseAddress = new Uri(baseUrl);
    http.Timeout = TimeSpan.FromSeconds(Math.Max(30, opt.TimeoutSeconds));
});
builder.Services.AddScoped<IPythonLinguisticClient, PythonLinguisticClient>();

static void ConfigureSourceParserHttpClient(HttpClient http, IConfiguration configuration)
{
    var timeoutSeconds = configuration.GetValue("SourceParsers:TimeoutSeconds", 30);
    http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
    http.DefaultRequestHeaders.UserAgent.ParseAdd("MedicalNewsVerifier/1.0 (corpus-sync)");
}

builder.Services.AddHttpClient<MinzdravNewsParser>((sp, http) =>
    ConfigureSourceParserHttpClient(http, sp.GetRequiredService<IConfiguration>()));
builder.Services.AddHttpClient<RospotrebnadzorRecommendationsParser>((sp, http) =>
    ConfigureSourceParserHttpClient(http, sp.GetRequiredService<IConfiguration>()));
builder.Services.AddHttpClient<ReferencedOfficialUrlFetcher>((sp, http) =>
    ConfigureSourceParserHttpClient(http, sp.GetRequiredService<IConfiguration>()));
builder.Services.AddHttpClient<OfficialStatisticsEnricher>((sp, http) =>
    ConfigureSourceParserHttpClient(http, sp.GetRequiredService<IConfiguration>()));
builder.Services.AddTransient<ISourceParser>(sp => sp.GetRequiredService<MinzdravNewsParser>());
builder.Services.AddTransient<ISourceParser>(sp => sp.GetRequiredService<RospotrebnadzorRecommendationsParser>());
builder.Services.AddScoped<SourceParserRegistry>();
builder.Services.AddScoped<IRelevantCorpusService, RelevantCorpusService>();

builder.Services.AddHttpClient<IOfficialSourceFetcher, OfficialSourceFetcher>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IAnalysisReportExporter, AnalysisReportExporter>();
builder.Services.AddScoped<ISystemDiagnosticsService, SystemDiagnosticsService>();
builder.Services.AddScoped<INewsAnalysisService, NewsAnalysisService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    MigrationHistoryBaseline.ApplyIfNeeded(db);
    db.Database.Migrate();
    SeedData.Initialize(db);
}

app.Run();
