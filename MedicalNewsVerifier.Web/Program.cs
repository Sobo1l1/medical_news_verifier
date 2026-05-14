using MedicalNewsVerifier.Web.Data;
using MedicalNewsVerifier.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddScoped<IPythonLinguisticClient, PythonLinguisticClient>();
builder.Services.AddHttpClient<IOfficialSourceFetcher, OfficialSourceFetcher>();
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

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    DatabaseSchemaPatcher.ApplyAll(db);
    SeedData.Initialize(db);
}

app.Run();
