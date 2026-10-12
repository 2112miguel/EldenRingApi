using eldenRingApi.Interfaces;
using eldenRingApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using MongoDB.Driver;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var mongoConnectionString = builder.Configuration["MongoDb:ConnectionString"]
    ?? builder.Configuration["MongoDb:MONGODB_CONNECTION_STRING"]
    ?? Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING")
    ?? Environment.GetEnvironmentVariable("MONGODB_URI")
    ?? throw new InvalidOperationException("Configura MongoDb:ConnectionString, MONGODB_CONNECTION_STRING o MONGODB_URI.");
var mongoDatabaseName = builder.Configuration["MongoDb:DatabaseName"] ?? "eldenring";

builder.Services.AddControllers();
builder.Services.AddOpenApi();
var configuredJwtKey = builder.Configuration["Jwt:Key"];
var jwtKey = string.IsNullOrWhiteSpace(configuredJwtKey)
    ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    : configuredJwtKey;
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "elden-ring-api";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "elden-ring-admin";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1)
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminWrite", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("scope", "api.write");
    });
});
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoConnectionString));
builder.Services.AddSingleton<IMongoDatabase>(serviceProvider =>
    serviceProvider.GetRequiredService<IMongoClient>().GetDatabase(mongoDatabaseName));
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddHostedService<BootstrapAdminService>();
builder.Services.AddScoped<IWeaponsService, WeaponsService>();
builder.Services.AddScoped<IBossesService, BossesService>();
builder.Services.AddScoped<IAmmosService, AmmosService>();
builder.Services.AddScoped<IArmorsService, ArmorsService>();
builder.Services.AddScoped<IAshesService, AshesService>();
builder.Services.AddScoped<IClassesService, ClassesService>();
builder.Services.AddScoped<ICreaturesService, CreaturesService>();
builder.Services.AddScoped<IIncantationsService, IncantationsService>();
builder.Services.AddScoped<IItemsService, ItemsService>();
builder.Services.AddScoped<ILocationsService, LocationsService>();
builder.Services.AddScoped<INpcsService, NpcsService>();
builder.Services.AddScoped<IShieldsService, ShieldsService>();
builder.Services.AddScoped<ISorceriesService, SorceriesService>();
builder.Services.AddScoped<ISpiritsService, SpiritsService>();
builder.Services.AddScoped<ITalismansService, TalismansService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// All future write endpoints are protected even if an endpoint omits [Authorize].
app.Use(async (context, next) =>
{
    var isSafeMethod = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method);
    var isLogin = context.Request.Path.Equals("/api/auth/token", StringComparison.OrdinalIgnoreCase);
    if (isSafeMethod || isLogin)
    {
        await next();
        return;
    }

    var authorizationService = context.RequestServices.GetRequiredService<IAuthorizationService>();
    var authorized = await authorizationService.AuthorizeAsync(context.User, context, "AdminWrite");
    if (authorized.Succeeded)
    {
        await next();
        return;
    }

    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    await context.Response.WriteAsJsonAsync(new { success = false, error = "A valid Bearer token with api.write permission is required for write operations." });
});

app.MapControllers();

app.Run();
