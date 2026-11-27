using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.AgentBinaries;

namespace Hephaestus.Workspaces.Tests;

/// <summary>
/// The two vendors, in memory (Story 3.1): a lock that pins every executable of the matrix with small stand-in contents, the addresses it names,
/// and a handler that serves what is at each. Claude Code is a plain executable per build; Codex CLI is an archive per executable. A test
/// changes what is served, or what the lock says, to make a download wrong. This file is linked into every test project that needs it.
/// </summary>
internal sealed class FakeVendor
{
    public static readonly IReadOnlyList<(Agent Agent, AgentLibc Libc, string File)> Matrix =
    [
        (Agent.ClaudeCode, AgentLibc.Glibc, "claude"),
        (Agent.ClaudeCode, AgentLibc.Musl, "claude"),
        (Agent.CodexCli, AgentLibc.Static, "codex"),
        (Agent.CodexCli, AgentLibc.Static, "codex-code-mode-host"),
    ];

    private readonly Dictionary<string, byte[]> _served = [];

    public FakeVendor()
    {
        var lines = new List<string>();
        foreach (var architecture in Enum.GetValues<AgentArchitecture>())
        {
            foreach (var (agent, libc, file) in Matrix)
            {
                var executable = Encoding.UTF8.GetBytes($"#!/bin/sh\n# {agent.Tag()} {architecture.Tag()}-{libc.Tag()} {file}\n");
                var url = $"https://vendor.test/{agent.Tag()}/{architecture.Tag()}-{libc.Tag()}/{file}";
                if (agent == Agent.CodexCli)
                {
                    url += ".tar.gz";
                    var archive = Archive(("inside-" + file, executable));
                    _served[url] = archive;
                    lines.Add($"{agent.Tag()} 1.2.3 {architecture.Tag()} {libc.Tag()} {file} {Sha(executable)} {url} {Sha(archive)}");
                }
                else
                {
                    _served[url] = executable;
                    lines.Add($"{agent.Tag()} 1.2.3 {architecture.Tag()} {libc.Tag()} {file} {Sha(executable)} {url}");
                }
            }
        }

        LockText = "# A lock for tests.\n" + string.Join('\n', lines) + "\n";
        Lock = AgentLock.Parse(LockText);
        Handler = new Stub(this);
    }

    public string LockText { get; }

    public AgentLock Lock { get; }

    /// <summary>Every address that was asked for, in order.</summary>
    public List<string> Requests { get; } = [];

    public HttpMessageHandler Handler { get; }

    /// <summary>Set to make the vendor's host unreachable.</summary>
    public Exception? Unreachable { get; set; }

    public string Url(AgentBinary binary) => binary.Url.AbsoluteUri;

    public void Serve(AgentBinary binary, byte[] body) => _served[binary.Url.AbsoluteUri] = body;

    public void Withdraw(AgentBinary binary) => _served.Remove(binary.Url.AbsoluteUri);

    /// <summary>The executable the lock pins, as the vendor ships it (taken out of the archive when there is one).</summary>
    public byte[] Executable(AgentBinary binary) => binary.IsArchive
        ? Extract(_served[binary.Url.AbsoluteUri])
        : _served[binary.Url.AbsoluteUri];

    /// <summary>Places every executable of an architecture in a volume directory, as an earlier start would have left them.</summary>
    public void Install(string directory, AgentArchitecture architecture)
    {
        foreach (var binary in Lock.For(architecture))
        {
            var path = Path.Combine(directory, binary.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Executable(binary));
        }
    }

    public static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static byte[] Archive(params (string Name, byte[] Content)[] files)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(content) });
            }
        }

        return output.ToArray();
    }

    private static byte[] Extract(byte[] archive)
    {
        using var gzip = new GZipStream(new MemoryStream(archive), CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        var content = new MemoryStream();
        reader.GetNextEntry()!.DataStream!.CopyTo(content);
        return content.ToArray();
    }

    private sealed class Stub(FakeVendor vendor) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            vendor.Requests.Add(url);
            if (vendor.Unreachable is { } unreachable)
            {
                throw unreachable;
            }

            return Task.FromResult(vendor._served.TryGetValue(url, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
