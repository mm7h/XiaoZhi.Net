using Demo.OTA.Server.Models;
using Newtonsoft.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// launchSettings.json is not used by a published application.
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"])
    && string.IsNullOrWhiteSpace(builder.Configuration["HTTP_PORTS"])
    && string.IsNullOrWhiteSpace(builder.Configuration["HTTPS_PORTS"]))
{
    builder.WebHost.UseUrls("http://0.0.0.0:4531");
}

// Add services to the container.

builder.Services.AddControllers()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new SnakeCaseNamingStrategy()
        };
        options.SerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore;
    });

builder.Services.Configure<XiaoZhiOptions>(builder.Configuration.GetSection("XiaoZhi"));

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
