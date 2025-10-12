using TradingBot.Application.Configuration;
using TradingBot.Configuration;
using TradingBot.Percistance.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.ConfigurationExtention();
// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.LoggerConfigure();
builder.Services.ConfigureServices(builder.Configuration);
builder.Services.ConfigApplication();

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

app.Run();
