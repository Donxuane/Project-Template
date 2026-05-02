using TradingBot.Application.Configuration;
using TradingBot.Application.DecisionEngine;
using TradingBot.Configuration;
using TradingBot.Percistance.Configuration;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.ConfigurationExtention();
// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.LoggerConfigure(builder.Configuration);
builder.Host.UseSerilog();
builder.Services.ConfigureServices(builder.Configuration);
builder.Services.ConfigApplication();
builder.Services.AddSettings(builder.Configuration);
builder.Services.Configure<TrendStateSettings>(builder.Configuration.GetSection(TrendStateSettings.SectionName));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}
