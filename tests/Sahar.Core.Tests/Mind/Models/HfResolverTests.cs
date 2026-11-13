using System.Net;
using System.Text;
using Sahar.Core.Mind.Models;

namespace Sahar.Core.Tests.Mind.Models;

/// <summary>«زيدي نموذج» by link or name: the commit and the file's LFS SHA-256 come from the Hugging Face API, so an
/// added model is pinned like the recommended ones.</summary>
public sealed class HfResolverTests
{
    private const string Sha = "f36b1ea49a332ede8fe5f389bbf5b3575ef71f48";

    private static HfResolver Resolver(FakeApi api) => new(api);

    [Fact]
    public async Task Resolve_PinsCommitAndLfsSha_PrefersQ4KM()
    {
        var r = await Resolver(new FakeApi()).ResolveAsync(HfRef.Parse("bartowski/Qwen_Qwen3.5-4B-GGUF")!, default);
        Assert.Null(r.Reason);
        Assert.Equal(Sha, r.Commit);
        Assert.Equal("Qwen_Qwen3.5-4B-Q4_K_M.gguf", r.File);
        Assert.Equal(3_010_000_000, r.Bytes);
        Assert.Equal(new string('a', 64), r.Sha256);
        Assert.Equal("apache-2.0", r.License);
    }

    [Fact]
    public async Task Resolve_QuantHint_And_NamedFile()
    {
        Assert.Equal("Qwen_Qwen3.5-4B-Q5_K_M.gguf", (await Resolver(new FakeApi()).ResolveAsync(HfRef.Parse("bartowski/Qwen_Qwen3.5-4B-GGUF:Q5_K_M")!, default)).File);
        Assert.Equal("Qwen_Qwen3.5-4B-Q8_0.gguf", (await Resolver(new FakeApi()).ResolveAsync(HfRef.Parse("bartowski/Qwen_Qwen3.5-4B-GGUF/Qwen_Qwen3.5-4B-Q8_0.gguf")!, default)).File);
    }

    [Fact]
    public async Task Resolve_NeverPicksMmproj_OrSplitParts()
    {
        var r = await Resolver(new FakeApi { OnlyOdd = true }).ResolveAsync(HfRef.Parse("x/y")!, default);
        Assert.Equal(HfResolver.NoGguf, r.Reason);
    }

    [Fact]
    public async Task Resolve_Gated_SaysSo() =>
        Assert.Equal(HfResolver.Gated, (await Resolver(new FakeApi { Gated = true }).ResolveAsync(HfRef.Parse("x/y")!, default)).Reason);

    [Fact]
    public async Task Resolve_Missing_SaysSo() =>
        Assert.Equal(HfResolver.NotFound, (await Resolver(new FakeApi { Status = HttpStatusCode.NotFound }).ResolveAsync(HfRef.Parse("x/y")!, default)).Reason);

    [Fact]
    public async Task Resolve_Offline_SaysBlocked() =>
        Assert.Equal(HfResolver.Blocked, (await Resolver(new FakeApi { Throw = true }).ResolveAsync(HfRef.Parse("x/y")!, default)).Reason);

    private sealed class FakeApi : HttpMessageHandler
    {
        public bool Gated, OnlyOdd, Throw;
        public HttpStatusCode Status = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (Throw) throw new HttpRequestException("offline");
            if (Status != HttpStatusCode.OK) return Task.FromResult(new HttpResponseMessage(Status));
            var path = req.RequestUri!.AbsolutePath;
            string json = path.Contains("/tree/")
                ? OnlyOdd
                    ? """[{"type":"file","path":"mmproj-F16.gguf","size":200,"lfs":{"oid":"bb","size":200}},{"type":"file","path":"big-00001-of-00002.gguf","size":9,"lfs":{"oid":"cc","size":9}}]"""
                    : $$$"""[{"type":"file","path":"README.md","size":10},{"type":"file","path":"Qwen_Qwen3.5-4B-Q8_0.gguf","size":4500000000,"lfs":{"oid":"{{{new string('c', 64)}}}","size":4500000000}},{"type":"file","path":"Qwen_Qwen3.5-4B-Q4_K_M.gguf","size":3010000000,"lfs":{"oid":"{{{new string('a', 64)}}}","size":3010000000}},{"type":"file","path":"Qwen_Qwen3.5-4B-Q5_K_M.gguf","size":3500000000,"lfs":{"oid":"{{{new string('d', 64)}}}","size":3500000000}},{"type":"file","path":"mmproj-Qwen-f16.gguf","size":600000000,"lfs":{"oid":"{{{new string('e', 64)}}}","size":600000000}}]"""
                : $$$"""{"id":"x/y","sha":"{{{Sha}}}","gated":{{{(Gated ? "\"auto\"" : "false")}}},"private":false,"cardData":{"license":"apache-2.0"}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
