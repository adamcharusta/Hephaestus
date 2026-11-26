using Hephaestus.Host;

return await EntryPoint.RunAsync(args, Console.Out, Console.Error, Environment.GetEnvironmentVariable, CancellationToken.None);
