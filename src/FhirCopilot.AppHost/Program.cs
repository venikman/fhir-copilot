var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.FhirCopilot_Api>("api");

builder.Build().Run();
