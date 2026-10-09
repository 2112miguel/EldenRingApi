using eldenRingApi.Interfaces;
using eldenRingApi.Services;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

var mongoConnectionString = builder.Configuration["MongoDb:ConnectionString"]
    ?? builder.Configuration["MongoDb:MONGODB_CONNECTION_STRING"]
    ?? Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING")
    ?? Environment.GetEnvironmentVariable("MONGODB_URI")
    ?? throw new InvalidOperationException("Configura MongoDb:ConnectionString, MONGODB_CONNECTION_STRING o MONGODB_URI.");
var mongoDatabaseName = builder.Configuration["MongoDb:DatabaseName"] ?? "eldenring";

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoConnectionString));
builder.Services.AddSingleton<IMongoDatabase>(serviceProvider =>
    serviceProvider.GetRequiredService<IMongoClient>().GetDatabase(mongoDatabaseName));
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

app.UseAuthorization();

app.MapControllers();

app.Run();
