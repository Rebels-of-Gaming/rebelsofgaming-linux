using System.Net;
using RoG.Linux.App.Services;

namespace RoG.Linux.Tests.App;

/// <summary>Bilder: Größenlimit auch ohne Content-Length (Audit N7), nur Hosts der Plattform – auch nach Weiterleitungen.</summary>
public class ImageLoaderTests
{
    [Fact]
    public async Task Stream_without_content_length_is_cut_off_at_the_limit()
    {
        using var content = new StreamContent(new EndlessStream());
        Assert.Null(content.Headers.ContentLength);

        Assert.Null(await ImageLoader.ReadCappedAsync(content, 1024 * 1024));
    }

    [Fact]
    public async Task Lying_content_length_does_not_lift_the_limit()
    {
        using var content = new StreamContent(new MemoryStream(new byte[300]));
        content.Headers.ContentLength = 10; // Server behauptet 10 Byte, schickt 300
        Assert.Null(await ImageLoader.ReadCappedAsync(content, 100));
    }

    [Fact]
    public async Task Small_images_and_announced_oversize_are_handled()
    {
        using var small = new ByteArrayContent(new byte[100]);
        Assert.Equal(100, (await ImageLoader.ReadCappedAsync(small, 100))?.Length);

        using var announced = new ByteArrayContent(new byte[10]);
        announced.Headers.ContentLength = ImageLoader.MaxBytes + 1L;
        Assert.Null(await ImageLoader.ReadCappedAsync(announced, ImageLoader.MaxBytes));
    }

    [Fact]
    public async Task Oversized_response_is_never_decoded()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new EndlessStream()) });
        using var loader = new ImageLoader(handler);

        Assert.Null(await loader.LoadAsync(new Uri("https://cdn.rogclan.de/huge.png"), 100));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Only_platform_hosts_are_requested()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var loader = new ImageLoader(handler);

        Assert.Null(await loader.LoadAsync(new Uri("https://tracker.example.net/pixel.png"), 100));
        Assert.Null(await loader.LoadUncachedAsync(new Uri("http://rebelsofgaming.com/plain-http.png"), 100));
        Assert.Null(await loader.LoadAsync(new Uri("https://rebelsofgaming.com.evil.net/x.png"), 100));
        Assert.Empty(handler.Requests);

        Assert.True(loader.IsAllowed(new Uri("https://cdn.rogclan.de/avatars/1.webp")));
        Assert.True(loader.IsAllowed(new Uri("https://rebelsofgaming.com/media/external?u=https%3A%2F%2Fexample.org%2Fa.png&signature=x")));
        Assert.True(loader.IsAllowed(new Uri("http://rog.test/storage/a.png")));
        Assert.False(loader.IsAllowed(new Uri("https://i.imgur.com/a.png")));
    }

    [Fact]
    public async Task Redirect_to_a_foreign_host_is_not_followed()
    {
        var handler = new Handler(_ =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri("https://tracker.example.net/pixel.png");
            return redirect;
        });
        using var loader = new ImageLoader(handler);

        Assert.Null(await loader.LoadAsync(new Uri("https://rebelsofgaming.com/r/abc"), 100));
        Assert.Equal(["rebelsofgaming.com"], handler.Requests.Select(r => r.Host));
    }

    [Fact]
    public void Custom_policy_is_respected()
    {
        using var loader = new ImageLoader(new Handler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)), u => u.Host.EndsWith(".example.test", StringComparison.Ordinal));
        Assert.True(loader.IsAllowed(new Uri("https://cdn.example.test/a.png")));
        Assert.False(loader.IsAllowed(new Uri("https://cdn.rogclan.de/a.png")));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    /// <summary>Liefert endlos Nullen und kennt keine Länge (wie eine Antwort ohne Content-Length).</summary>
    private sealed class EndlessStream : Stream
    {
        public long Served { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => Served; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (Served > 64L * 1024 * 1024)
            {
                throw new InvalidOperationException("Limit wurde nicht durchgesetzt");
            }

            Array.Clear(buffer, offset, count);
            Served += count;
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
