var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.cochca>("cochca")
    .WithExternalHttpEndpoints();

builder.Build().Run();
