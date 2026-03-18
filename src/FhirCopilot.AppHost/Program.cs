var builder = DistributedApplication.CreateBuilder(args);

var fhirBaseUrl = builder.AddParameter("fhir-base-url", "https://bulk-fhir.fly.dev/fhir");

builder.AddProject<Projects.FhirCopilot_Api>("api")
    .WithEnvironment("Provider__FhirBaseUrl", fhirBaseUrl);

builder.Build().Run();
