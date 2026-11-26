using System.Text;
using Hephaestus.Host;

// Standard input is UTF-8 whatever the locale says, so a secret's value reaches the host exactly as it was piped in.
using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), detectEncodingFromByteOrderMarks: false);
return await EntryPoint.RunAsync(args, input, Console.Out, Console.Error, Environment.GetEnvironmentVariable, CancellationToken.None);
