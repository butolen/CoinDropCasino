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
        options.SignIn.RequireConfirmedAccount = true; /// wichtig nur temporär 
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
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/login";
});
// Repositories
builder.Services.AddScoped<UserRepo>();
builder.Services.AddScoped<TransactionRepo>();
builder.Services.AddScoped<GameSessionRepo>();
builder.Services.AddScoped<HDepositRepo>();
builder.Services.AddScoped<CDepositRepo>();
builder.Services.AddScoped<WithdrawalRepo>();
builder.Services.AddScoped<LogRepo>();
builder.Services.AddScoped<IRepository<ApplicationUser>, UserRepo>();

builder.Services.AddScoped<IRepository<GameSession>, GameSessionRepo>();
builder.Services.AddScoped<IRepository<CryptoDeposit>, CDepositRepo>();
builder.Services.AddScoped<IRepository<HardwareDeposit>, HDepositRepo>();
builder.Services.AddScoped<IRepository<Withdrawal>, WithdrawalRepo>();
builder.Services.AddScoped<IRepository<Log>, LogRepo>();
builder.Services.AddScoped<IRepository<SystemSetting>, SystemSettingRepository>();
builder.Services.AddHttpClient<WithdrawlService>();
// UserService
builder.Services.AddScoped<IGameHistoryService, GameHistoryService>();
builder.Services.AddScoped<ITransactionHistoryService, TransactionHistoryService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ISolanService,SolanaWalletService>();
builder.Services.AddScoped<SessionCodeService>();
//admin dahboard 

builder.Services.AddScoped<ISystemSettingsService, SystemSettingsService>();
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
builder.Services.AddScoped<IAdminUserService, AdminUserService>();
//roulett
builder.Services.AddScoped<IRouletteService,RouletteService>();
//bj service
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IBlackjackService, BlackjackService>();
// admin seeder
builder.Services.AddScoped<IIdentitySeeder, IdentitySeeder>();
//wallet service 



//crypto


// PriceService mit HttpClient konfigurieren
builder.Services.AddHttpClient<PriceService>(client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    client.Timeout = TimeSpan.FromSeconds(30);
});
//Helius 
builder.Services.AddHttpClient("Helius", client =>
{
    var apiKey = builder.Configuration["Helius:ApiKey"];
    client.BaseAddress = new Uri($"https://mainnet.helius-rpc.com/?api-key={apiKey}");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddSingleton<IHeliusSolanaService, HeliusSolanaService>();

builder.Services.AddScoped<IPriceService, PriceService>();
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
app.UseMiddleware<ForceLogoutMiddleware>();
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

//seeeder ausführen
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<IIdentitySeeder>();
    await seeder.SeedAsync();
}
app.Run();