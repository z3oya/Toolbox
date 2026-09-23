using Toolbox.Core.Rtt.Elf;

namespace Toolbox.Tests;

/// <summary>fuzz-lite (group B): the parser must turn arbitrary bytes into a Status, never an
/// exception. Deterministic by construction - a fixed-seed xorshift, no Random - so a failure
/// on CI replays bit-exact locally.</summary>
public class ElfFuzzTests
{
    [Fact]
    public void Random_buffers_never_throw_and_never_parse_ok()
    {
        uint state = 0x5EED_0001;
        uint Next() { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state; }

        for (int i = 0; i < 100; i++)
        {
            int length = 4 + (int)(Next() % 1024);
            byte[] bytes = new byte[length];

            bool withMagic = (Next() & 1) == 0;   // half get a valid magic to reach deeper parse paths
            if (withMagic)
            {
                bytes[0] = 0x7f;
                bytes[1] = (byte)'E';
                bytes[2] = (byte)'L';
                bytes[3] = (byte)'F';
            }
            for (int j = withMagic ? 4 : 0; j < length; j++)
                bytes[j] = (byte)Next();

            ElfImage image = ElfImage.Load(bytes);   // must not throw
            Assert.True(image.Status is ElfLoadStatus.NotElf or ElfLoadStatus.UnsupportedClass or ElfLoadStatus.ParseFailed,
                $"iteration {i}: random bytes parsed as {image.Status}");
        }
    }

    [Fact]
    public void Truncated_valid_images_fail_cleanly_at_every_boundary()
    {
        byte[] full = new ElfBuilder
        {
            Symbols =
            {
                new ElfBuilder.Sym("s1", 0x1000, 4, ElfBuilder.TypeObject, ElfBuilder.BindGlobal),
                new ElfBuilder.Sym("s2", 0x2000, 8, ElfBuilder.TypeObject, ElfBuilder.BindGlobal),
            },
        }.Build();

        int[] cuts = [1, 4, 15, 16, 17, 40, 51, 52, 53, 60, 100, full.Length / 2, full.Length - 1];
        foreach (int keep in cuts)
        {
            ElfImage image = ElfImage.Load(full[..keep]);   // must not throw
            Assert.True(image.Status is ElfLoadStatus.NotElf or ElfLoadStatus.ParseFailed,
                $"keeping {keep}/{full.Length} bytes parsed as {image.Status}");
        }
    }
}
