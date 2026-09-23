using Toolbox.Core.Rtt;
using Toolbox.Core.Rtt.Elf;

namespace Toolbox.Tests;

/// <summary>LocateFromElf semantics (group E): the plausibility gate's boundaries were set
/// in the phase-2 plan review (minimum legal block is one up + one down buffer = 64 bytes in
/// the legacy layout), and the real-firmware fixtures pin both the positive path (Keil, where
/// the armlink .map says 0x24000070 / 168) and the negative path (GCC build without RTT).</summary>
public class RttControlBlockElfTests
{
    private static ElfImage ImageWithCb(uint address = ElfTestSupport.KeilCbAddress, uint size = ElfTestSupport.KeilCbSize, byte type = ElfBuilder.TypeObject) =>
        ElfImage.Load(new ElfBuilder
        {
            Symbols = { new ElfBuilder.Sym(RttControlBlock.ControlBlockSymbolName, address, size, type, ElfBuilder.BindGlobal) },
        }.Build());

    [Theory]
    [InlineData(168u)]   // modern layout, 3+3 buffers (Keil fixture ground truth)
    [InlineData(144u)]   // legacy 20-byte descriptors, 3+3
    [InlineData(72u)]    // modern, 1+1 buffers - the smallest modern block
    [InlineData(64u)]    // legacy, 1+1 - the absolute floor
    public void Resolves_plausible_control_blocks(uint size)
    {
        RttElfLocateResult result = RttControlBlock.LocateFromElf(ImageWithCb(ElfTestSupport.KeilCbAddress, size));
        Assert.True(result.Resolved);
        Assert.Equal(ElfTestSupport.KeilCbAddress, result.Address);
    }

    [Theory]
    [InlineData(24u)]    // (Size-24) == 0 divides by both strides but means zero buffers
    [InlineData(48u)]    // a single buffer is not a usable RTT configuration
    [InlineData(999u)]   // fits no descriptor stride
    [InlineData(0u)]
    public void Rejects_implausible_sizes_with_a_reason(uint size)
    {
        RttElfLocateResult result = RttControlBlock.LocateFromElf(ImageWithCb(0x2400_0070, size));
        Assert.Equal(RttElfLocateStatus.ImplausibleSize, result.Status);
        Assert.Contains("implausible", result.Reason);
        Assert.Contains(size.ToString(), result.Reason);
    }

    [Fact]
    public void A_function_sharing_the_name_does_not_count()
    {
        ElfImage image = ImageWithCb(0x0800_0101, 168, ElfBuilder.TypeFunc);
        RttElfLocateResult result = RttControlBlock.LocateFromElf(image);
        Assert.Equal(RttElfLocateStatus.SymbolMissing, result.Status);
        Assert.Contains("not in the symbol table", result.Reason);
    }

    [Fact]
    public void Two_object_candidates_fail_as_ambiguous_naming_both_addresses()
    {
        var builder = new ElfBuilder();
        builder.Symbols.Add(new ElfBuilder.Sym(RttControlBlock.ControlBlockSymbolName, 0x2400_0000, 168, ElfBuilder.TypeObject, ElfBuilder.BindLocal));
        builder.Symbols.Add(new ElfBuilder.Sym(RttControlBlock.ControlBlockSymbolName, 0x2400_1000, 168, ElfBuilder.TypeObject, ElfBuilder.BindGlobal));
        ElfImage image = ElfImage.Load(builder.Build());

        RttElfLocateResult result = RttControlBlock.LocateFromElf(image);
        Assert.Equal(RttElfLocateStatus.Ambiguous, result.Status);
        Assert.Contains("ambiguous", result.Reason);
        Assert.Contains("0x24000000", result.Reason);
        Assert.Contains("0x24001000", result.Reason);
    }

    [Fact]
    public void Failed_images_propagate_their_reason()
    {
        byte[] bytes = new ElfBuilder().Build();
        bytes[0] = (byte)'X';
        RttElfLocateResult result = RttControlBlock.LocateFromElf(ElfImage.Load(bytes));
        Assert.Equal(RttElfLocateStatus.InvalidImage, result.Status);
        Assert.Contains("magic", result.Reason);
    }

    [Fact]
    public void Null_image_is_a_clean_failure()
    {
        RttElfLocateResult result = RttControlBlock.LocateFromElf(null);
        Assert.Equal(RttElfLocateStatus.InvalidImage, result.Status);
        Assert.False(result.Resolved);
    }

    // ---- real firmware -----------------------------------------------------------------

    [Fact]
    public void Keil_fixture_resolves_to_the_map_ground_truth()
    {
        ElfImage image = ElfImage.FromFile(ElfTestSupport.KeilAxf);
        Assert.Equal(ElfLoadStatus.Ok, image.Status);

        SymbolLookup lookup = image.Lookup(RttControlBlock.ControlBlockSymbolName);
        Assert.Equal(SymbolLookupStatus.Found, lookup.Status);
        Assert.True(lookup.TryGetSymbol(out ElfSymbol symbol));
        Assert.Equal(((ulong)ElfTestSupport.KeilCbAddress, ElfTestSupport.KeilCbSize, ElfSymbolKind.Object, ElfSymbolBinding.Global),
                     (symbol.Address, symbol.Size, symbol.Kind, symbol.Binding));

        RttElfLocateResult result = RttControlBlock.LocateFromElf(image);
        Assert.True(result.Resolved);
        Assert.Equal(ElfTestSupport.KeilCbAddress, result.Address);
    }

    [Fact]
    public void Gcc_fixture_without_rtt_degrades_to_the_scan_path()
    {
        ElfImage image = ElfImage.FromFile(ElfTestSupport.GccElf);
        Assert.Equal(ElfLoadStatus.Ok, image.Status);
        RttElfLocateResult result = RttControlBlock.LocateFromElf(image);
        Assert.Equal(RttElfLocateStatus.SymbolMissing, result.Status);
        Assert.Contains("not in the symbol table", result.Reason);
    }
}
