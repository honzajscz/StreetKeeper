using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using StreetFlow.Dashboard;
using StreetFlow.Supabase;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient());

builder.Services.AddScoped(_ => new SupabaseOptions
{
    Url = builder.Configuration["Supabase:Url"] ?? string.Empty,
    AnonKey = builder.Configuration["Supabase:AnonKey"] ?? string.Empty,
});

await builder.Build().RunAsync();
