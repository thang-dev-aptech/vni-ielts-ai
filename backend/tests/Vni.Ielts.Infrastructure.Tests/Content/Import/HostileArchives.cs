using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Vni.Ielts.Infrastructure.Tests.Content.Import;

/// <summary>
/// The hostile fixtures docs/security/zip-ingestion-security.md § Testing asks
/// to keep in the repository — generated deterministically rather than checked
/// in as binaries, so a reviewer can read what each one contains and a
/// refactor cannot silently drop one.
///
/// <b>Where `ZipArchive` will write a hostile header, it is used.</b> It does
/// not validate entry names in Create mode, so traversal, absolute, backslash,
/// null-byte and reserved names all come from it directly, and a symlink is a
/// matter of external attributes. Where it will not — a header that lies about
/// the uncompressed size — the bytes are patched after the fact by locating
/// the entry's central-directory and local-header records.
/// </summary>
internal static class HostileArchives
{
    public const int UnixSymlink = 0xA1FF << 16;
    public const int UnixRegular = unchecked((int)(0x81A4u << 16));

    public sealed record Entry(string Name, byte[] Content, int? ExternalAttributes = null,
        CompressionLevel Level = CompressionLevel.Optimal);

    public static Entry File(string name, string text = "x", CompressionLevel level = CompressionLevel.Optimal) =>
        new(name, Encoding.UTF8.GetBytes(text), null, level);

    public static Entry Bytes(string name, byte[] content, int? attributes = null,
        CompressionLevel level = CompressionLevel.Optimal) => new(name, content, attributes, level);

    /// <summary>A complete, well-formed archive built from the given entries.</summary>
    public static MemoryStream Build(params Entry[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                var zipEntry = archive.CreateEntry(entry.Name, entry.Level);
                if (entry.ExternalAttributes is { } attributes)
                    zipEntry.ExternalAttributes = attributes;
                if (entry.Name.EndsWith('/') && entry.Content.Length == 0) continue;
                using var output = zipEntry.Open();
                output.Write(entry.Content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>A valid four-folder package with one small file per skill.</summary>
    public static MemoryStream FullPackage() => Build(
        File("reading/passage-1.txt", "The reading passage."),
        File("listening/section-1.txt", "The listening transcript."),
        File("writing/task-1.txt", "The writing prompt."),
        File("speaking/part-1.txt", "The speaking cue."));

    /// <summary>A bomb: <paramref name="size"/> zero bytes, which deflate shrinks by hundreds to one.</summary>
    public static MemoryStream Bomb(int size = 10 * 1024) =>
        Build(Bytes("reading/bomb.txt", new byte[size], level: CompressionLevel.SmallestSize));

    public static MemoryStream Traversal() => Build(File("reading/ok.txt"), File("../../evil.txt"));
    public static MemoryStream AbsoluteUnix() => Build(File("/etc/passwd"));
    public static MemoryStream AbsoluteWindows() => Build(File(@"C:\x.txt"));
    public static MemoryStream BackslashTraversal() => Build(File(@"reading\..\..\evil.txt"));
    public static MemoryStream NullByte() => Build(File("reading/ok.txt\0.exe"));
    public static MemoryStream ReservedName() => Build(File("reading/CON.json"));
    public static MemoryStream RightToLeftOverride() => Build(File("reading/exe\u202Etxt.pdf"));
    public static MemoryStream NestedZip() => Build(File("reading/inner.zip", "PK"));
    public static MemoryStream Symlink() =>
        Build(Bytes("reading/link", "../../../etc/passwd"u8.ToArray(), UnixSymlink));

    /// <summary>
    /// A well-formed archive whose central directory understates one entry's
    /// uncompressed size. The entry is stored, not deflated, so the reader
    /// hands back every real byte while the header claims almost none.
    /// </summary>
    public static MemoryStream LyingHeader(string name, int realSize, uint declaredSize)
    {
        var real = new byte[realSize];
        Random.Shared.NextBytes(real);
        var stream = Build(Bytes(name, real, level: CompressionLevel.NoCompression));
        var bytes = stream.ToArray();
        var nameBytes = Encoding.UTF8.GetBytes(name);

        // Central directory header: signature 0x02014b50; uncompressed size at
        // +24; name at +46. Local header: signature 0x04034b50; uncompressed
        // size at +22; name at +30. Both are rewritten so the mismatch is
        // between header and payload, not between the two headers.
        Patch(bytes, [0x50, 0x4B, 0x01, 0x02], nameBytes, nameOffset: 46, sizeOffset: 24, declaredSize);
        Patch(bytes, [0x50, 0x4B, 0x03, 0x04], nameBytes, nameOffset: 30, sizeOffset: 22, declaredSize);

        return new MemoryStream(bytes, writable: false);
    }

    private static void Patch(byte[] bytes, byte[] signature, byte[] name, int nameOffset, int sizeOffset, uint value)
    {
        for (var i = 0; i + nameOffset + name.Length <= bytes.Length; i++)
        {
            if (!bytes.AsSpan(i, 4).SequenceEqual(signature)) continue;
            if (!bytes.AsSpan(i + nameOffset, name.Length).SequenceEqual(name)) continue;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i + sizeOffset, 4), value);
            return;
        }

        throw new InvalidOperationException($"No record with signature {Convert.ToHexString(signature)} for {Encoding.UTF8.GetString(name)}.");
    }

    /// <summary>The 22-byte record of an archive with no entries at all.</summary>
    public static MemoryStream Empty()
    {
        var stream = new MemoryStream();
        using (new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true)) { }
        stream.Position = 0;
        return stream;
    }

    public static MemoryStream NotAZip() => new("%PDF-1.7 definitely not an archive"u8.ToArray());

    /// <summary>A read-only stream that cannot seek, as an HTTP request body cannot.</summary>
    public static Stream Unseekable(MemoryStream inner) => new ForwardOnlyStream(inner);

    /// <summary>A stream whose every read blocks, so an extraction timeout is certain to fire mid-copy.</summary>
    public static Stream Slow(MemoryStream inner, TimeSpan perRead) => new SlowStream(inner, perRead);

    private sealed class ForwardOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class SlowStream(MemoryStream inner, TimeSpan perRead) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Thread.Sleep(perRead);
            return inner.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
