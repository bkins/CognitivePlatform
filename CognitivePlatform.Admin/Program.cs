using CognitivePlatform.Admin;
using CognitivePlatform.Admin.CpAdminClients;
using CognitivePlatform.Admin.Services;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
       .AddInteractiveServerComponents();

builder.Services.AddMudServices();

builder.Services.AddScoped<AdminSessionService>();
builder.Services.AddTransient<AdminSecretHandler>();

var cpApiBaseUrl = builder.Configuration["AdminSettings:CpApiBaseUrl"]
               ?? "http://localhost:5273";

builder.Services
       .AddHttpClient<IAdminSystemClient, AdminSystemClient>(client =>
           client.BaseAddress = new Uri(cpApiBaseUrl))
       .AddHttpMessageHandler<AdminSecretHandler>();

builder.Services
       .AddHttpClient<IAdminRegistryClient, AdminRegistryClient>(client =>
           client.BaseAddress = new Uri(cpApiBaseUrl))
       .AddHttpMessageHandler<AdminSecretHandler>();

builder.Services
       .AddHttpClient<IAdminKnowledgeClient, AdminKnowledgeClient>(client =>
           client.BaseAddress = new Uri(cpApiBaseUrl))
       .AddHttpMessageHandler<AdminSecretHandler>();

builder.Services
       .AddHttpClient<IAdminJournalClient, AdminJournalClient>(client =>
           client.BaseAddress = new Uri(cpApiBaseUrl))
       .AddHttpMessageHandler<AdminSecretHandler>();

builder.Services
       .AddHttpClient<IAdminLogsClient, AdminLogsClient>(client =>
           client.BaseAddress = new Uri(cpApiBaseUrl))
       .AddHttpMessageHandler<AdminSecretHandler>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();
