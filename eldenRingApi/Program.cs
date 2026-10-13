using eldenRingApi.Interfaces;
using eldenRingApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Diagnostics;
using MongoDB.Bson;
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
builder.Services.AddMemoryCache();
var configuredJwtKey = builder.Configuration["Jwt:Key"];
var jwtKey = string.IsNullOrWhiteSpace(configuredJwtKey)
    ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    : configuredJwtKey;
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "elden-ring-api";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "elden-ring-admin";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var email = context.Principal?.FindFirst(ClaimTypes.Email)?.Value;
                var versionText = context.Principal?.FindFirst("tokenVersion")?.Value;
                if (string.IsNullOrWhiteSpace(email) || !int.TryParse(versionText, out var tokenVersion))
                {
                    context.Fail("Invalid account token.");
                    return;
                }

                var database = context.HttpContext.RequestServices.GetRequiredService<IMongoDatabase>();
                var users = database.GetCollection<BsonDocument>("users");
                var user = await users.Find(Builders<BsonDocument>.Filter.Eq("_id", email)).FirstOrDefaultAsync(context.HttpContext.RequestAborted);
                var currentVersion = user is not null && user.TryGetValue("tokenVersion", out var storedVersion) && storedVersion.IsNumeric
                    ? storedVersion.ToInt32()
                    : 0;
                if (user is null || !user.GetValue("isActive", false).ToBoolean() || currentVersion != tokenVersion)
                    context.Fail("The account token is no longer valid.");
            }
        };
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
builder.Services.AddSingleton<IRecordRelationshipResolver, RecordRelationshipResolver>();
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

// Keep request diagnostics in local console logs and Azure App Service logs.
app.Use(async (context, next) =>
{
    var stopwatch = Stopwatch.StartNew();
    await next();
    stopwatch.Stop();
    app.Logger.LogInformation(
        "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds} ms. TraceId: {TraceId}",
        context.Request.Method,
        context.Request.Path,
        context.Response.StatusCode,
        stopwatch.ElapsedMilliseconds,
        context.TraceIdentifier);
});

// Return a consistent safe response while preserving the exception details in server logs.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception exception)
    {
        app.Logger.LogError(
            exception,
            "Unhandled exception for HTTP {Method} {Path}. TraceId: {TraceId}",
            context.Request.Method,
            context.Request.Path,
            context.TraceIdentifier);

        if (context.Response.HasStarted) throw;

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new
        {
            success = false,
            error = "An unexpected server error occurred.",
            traceId = context.TraceIdentifier
        });
    }
});

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
