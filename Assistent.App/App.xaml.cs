using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Assistent.App.Services;
using Assistent.App.Settings;
using Assistent.App.Tools;
using Assistent.App.ViewModels;
using Assistent.Core.Assistant;
using Assistent.Core.Chat;
using Assistent.Core.Configuration;
using Assistent.Core.Security;
using Assistent.Core.Tools;
using Assistent.Providers.LlamaSharp;
using Assistent.Providers.Ollama;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistent.App;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.SetBasePath(AppContext.BaseDirectory);
                cfg.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .ConfigureLogging(b =>
            {
                b.AddDebug();
                b.AddConsole();
            })
            .ConfigureServices(ConfigureServices)
            .Build();

        _host.Start();

        var prefs = _host.Services.GetRequiredService<SecurityPreferences>();
        ThemeManager.Apply(Current, prefs.Theme);
        prefs.ThemeChanged += (_, _) => Dispatcher.Invoke(() => ThemeManager.Apply(Current, prefs.Theme));

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    private static void ConfigureServices(HostBuilderContext ctx, IServiceCollection services)
    {
        services.Configure<AppOptions>(ctx.Configuration.GetSection(AppOptions.SectionName));
        services.Configure<OllamaOptions>(ctx.Configuration.GetSection(OllamaOptions.SectionName));
        services.Configure<LlamaSharpOptions>(ctx.Configuration.GetSection(LlamaSharpOptions.SectionName));
        services.Configure<SecurityOptions>(ctx.Configuration.GetSection(SecurityOptions.SectionName));

        services.AddSingleton<SecurityPreferences>();
        services.AddSingleton<ISecurityPreferences>(sp => sp.GetRequiredService<SecurityPreferences>());
        services.AddSingleton<IOllamaRuntimeOverride, OllamaRuntimeOverride>();
        services.AddSingleton<IPowerShellConfirmation, WpfPowerShellConfirmation>();

        services.AddHttpClient("Ollama", (sp, http) =>
        {
            var o = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            var rt = sp.GetRequiredService<IOllamaRuntimeOverride>();
            var baseUri = rt.BaseUriOverride
                ?? (Uri.TryCreate(o.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var u) ? u : null);
            if (baseUri is not null)
                http.BaseAddress = baseUri;
            http.Timeout = o.RequestTimeout;
        });

        services.AddSingleton<OllamaChatClient>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("Ollama");
            return new OllamaChatClient(
                http,
                sp.GetRequiredService<IOptions<OllamaOptions>>(),
                sp.GetRequiredService<IOllamaRuntimeOverride>(),
                sp.GetRequiredService<ILogger<OllamaChatClient>>());
        });

        services.AddSingleton<LlamaSharpChatClient>();
        services.AddSingleton<IChatModelClient>(sp =>
        {
            var app = sp.GetRequiredService<IOptions<AppOptions>>().Value;
            return app.Provider == AssistantProviderKind.LlamaSharp
                ? sp.GetRequiredService<LlamaSharpChatClient>()
                : sp.GetRequiredService<OllamaChatClient>();
        });

        services.AddSingleton<OllamaHealthService>();
        services.AddSingleton<OllamaHostRecovery>();

        services.AddSingleton<IToolHandler, OpenApplicationHandler>();
        services.AddSingleton<IToolHandler, OpenUrlHandler>();
        services.AddSingleton<IToolHandler, GetSystemInfoHandler>();
        services.AddSingleton<IToolHandler, ReadFileHandler>();
        services.AddSingleton<IToolHandler, ListDirectoryHandler>();
        services.AddSingleton<IToolHandler, FindFilesHandler>();
        services.AddSingleton<IToolHandler, GetDateTimeHandler>();
        services.AddSingleton<IToolHandler, GetKnownFolderPathHandler>();
        services.AddSingleton<IToolHandler, RevealInExplorerHandler>();
        services.AddSingleton<IToolHandler, ClipboardReadToolHandler>();
        services.AddSingleton<IToolHandler, ExecutePowerShellHandler>();
        services.AddSingleton<ToolRunner>();
        services.AddSingleton<AssistantOrchestrator>();

        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsWindow>();

        services.AddSingleton<MainViewModel>();
        services.AddTransient<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
