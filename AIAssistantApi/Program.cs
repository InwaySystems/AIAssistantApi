using AIAssistantAPI.Interfaces;
using AIAssistantAPI.Services;
using Azure.Identity;

var builder = WebApplication.CreateBuilder(args);


// Add services to the container.

// Adding Azure Key Vault to the configuration provider
var keyVaultEndpoint = new Uri(builder.Configuration["AzureKeyVaultEndpoint"]);
builder.Configuration.AddAzureKeyVault(keyVaultEndpoint, new DefaultAzureCredential());

// Add services for session state
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30); // You can set the timeout as needed.
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});


builder.Services.AddControllers();
builder.Services.AddSingleton<IChatService, ChatService>();
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

app.UseSession();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
