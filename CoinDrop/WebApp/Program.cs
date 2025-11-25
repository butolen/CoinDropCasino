using CoinDrop;
using Domain;
using Microsoft.EntityFrameworkCore;
using WebApp.Components;
using WebApp.Data;
using Microsoft.EntityFrameworkCore;





var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();





// EF Core + MySQL
builder.Services.AddDbContextFactory<CoinDropContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        new MySqlServerVersion(new Version(8, 0, 36))
    ));
// Repositories
builder.Services.AddScoped<UserRepo>();
builder.Services.AddScoped<TransactionRepo>();
builder.Services.AddScoped<GameSessionRepo>();
builder.Services.AddScoped<HDepositRepo>();
builder.Services.AddScoped<CDepositRepo>();
builder.Services.AddScoped<WithdrawalRepo>();
builder.Services.AddScoped<LogRepo>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();