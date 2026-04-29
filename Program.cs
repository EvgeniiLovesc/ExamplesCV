using KonturVideo.AI;
using KonturVideo.AI.Clients;
using KonturVideo.AI.Inference;
using KonturVideo.AI.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<AgentApiOptions>(builder.Configuration.GetSection("AgentApi"));
builder.Services.Configure<WebUiOptions>(builder.Configuration.GetSection("WebUi"));
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("Ai"));

builder.Services.AddHttpClient<AgentApiClient>();
builder.Services.AddHttpClient<WebUiClient>();
builder.Services.AddSingleton<IObjectDetector, YoloOnnxDetector>();
builder.Services.AddSingleton<FrameSampler>();
builder.Services.AddSingleton<RuleMatcher>();
builder.Services.AddSingleton<TrackMemory>();
builder.Services.AddHostedService<AiOrchestrator>();

var host = builder.Build();
host.Run();
