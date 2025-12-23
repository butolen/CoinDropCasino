using CoinDrop;
using CoinDrop.config;
using CoinDrop.services;
using CoinDrop.services.implementations;
using CoinDrop.services.interfaces;
using Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebApp.Components;

using Microsoft.EntityFrameworkCore;
using WebApp.Endpoints;
using WebApp.services.implementations;


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


//IF
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<int>>(options =>
    {
        // Passwort-Regeln
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 7;          // <= deine Vorgabe
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;

        // Username / Email
        options.User.RequireUniqueEmail = true;       // Email eindeutig
        
        // E-Mail-Bestätigung notwendig
        options.SignIn.RequireConfirmedAccount = true;
    })
    .AddEntityFrameworkStores<CoinDropContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthentication()
    .AddMicrosoftAccount(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Microsoft:ClientId"];
        options.ClientSecret = builder.Configuration["Authentication:Microsoft:ClientSecret"];
        options.CallbackPath = "/signin-microsoft";

       
        options.SignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Google:ClientId"];
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
        options.CallbackPath = "/signin-google";

        // dito
        options.SignInScheme = IdentityConstants.ExternalScheme;
    });
// Repositories
builder.Services.AddScoped<UserRepo>();
builder.Services.AddScoped<TransactionRepo>();
builder.Services.AddScoped<GameSessionRepo>();
builder.Services.AddScoped<HDepositRepo>();
builder.Services.AddScoped<CDepositRepo>();
builder.Services.AddScoped<WithdrawalRepo>();
builder.Services.AddScoped<LogRepo>();


// UserService
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ISolanService,SolanaWalletService>();
//wallet service 

//crypto
builder.Services.Configure<CryptoConfig>(
    builder.Configuration.GetSection("Crypto"));
builder.Services.AddScoped<SolBalanceScannerJob>();
builder.Services.AddHostedService<CoinDrop.services.SolBalanceScannerHosted>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddScoped<WithdrawlService>();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();

    string[] roles = { "customer", "admin" };

    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole<int>(role));
    }
}app.UseHttpsRedirection();

// Erst Authentication, dann Authorization
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Endpoints NACH der Middleware registrieren
app.MapAuthEndpoints();
app.MapTestEndpoints();

app.Run();