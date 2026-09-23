using Toolbox.Core.Rtt.Elf;

namespace Toolbox.Tests;

/// <summary>Synthetic-image tests for ElfImage: load statuses (group A), symbol fidelity
/// (group C) and section reads (group G) of the phase-2 test matrix. The builder emits raw
/// ELF encodings, so a mis-mapping inside ElfImage shows up here as a real assertion failure.</summary>
public class ElfImageTests
{
    private static ElfImage Build(Action<ElfBuilder>? configure = null)
    {
        var builder = new ElfBuilder();
        configure?.Invoke(builder);
        return ElfImage.Load(builder.Build());
    }

    // ---- group A: load statuses -------------------------------------------------------

    [Fact]
    public void Load_valid_synthetic_image_is_ok()
    {
        ElfImage image = Build();
        Assert.Equal(ElfLoadStatus.Ok, image.Status);
        Assert.Equal("", image.FailureReason);
        Assert.True(image.IsLittleEndian);
    }

    [Theory]
    [InlineData(0)]     // null
    [InlineData(1)]     // empty
    public void Load_treats_null_and_empty_as_not_elf(int variant)
    {
        byte[] bytes = variant == 0 ? null! : Array.Empty<byte>();
        ElfImage image = ElfImage.Load(bytes);
        Assert.Equal(ElfLoadStatus.NotElf, image.Status);
    }

    [Fact]
    public void Load_rejects_bad_magic_as_not_elf()
    {
        byte[] bytes = new ElfBuilder().Build();
        bytes[0] = (byte)'X';
        ElfImage image = ElfImage.Load(bytes);
        Assert.Equal(ElfLoadStatus.NotElf, image.Status);
        Assert.Contains("magic", image.FailureReason);
    }

    [Fact]
    public void Load_rejects_elf64_class_before_parsing()
    {
        ElfImage image = ElfImage.Load(new ElfBuilder { Elf64Header = true }.Build());
        Assert.Equal(ElfLoadStatus.UnsupportedClass, image.Status);
        Assert.Empty(image.Symbols);
    }

    [Fact]
    public void Load_reports_truncated_header_as_parse_failed()
    {
        byte[] full = new ElfBuilder().Build();
        ElfImage image = ElfImage.Load(full[..40]);   // header is 52 bytes
        Assert.Equal(ElfLoadStatus.ParseFailed, image.Status);
        Assert.StartsWith("malformed ELF", image.FailureReason);
    }

    [Fact]
    public void Load_never_throws_on_zeroed_body_after_valid_magic()
    {
        byte[] bytes = new byte[512];
        bytes[0] = 0x7f;
        bytes[1] = (byte)'E';
        bytes[2] = (byte)'L';
        bytes[3] = (byte)'F';
        bytes[4] = 1;
        var image = ElfImage.Load(bytes);   // must not throw, whatever the library makes of it
        Assert.True(Enum.IsDefined(image.Status));
    }

    [Fact]
    public void FromFile_missing_path_throws_file_not_found()
    {
        string path = Path.Combine(Path.GetTempPath(), "no-such-elf-image-does-not-exist.elf");
        Assert.Throws<FileNotFoundException>(() => ElfImage.FromFile(path));
    }

    // ---- group C: symbol fidelity -----------------------------------------------------

    [Fact]
    public void Symbols_exclude_the_null_entry_and_unnamed_entries()
    {
        ElfImage image = Build(b =>
        {
            b.Symbols.Add(new ElfBuilder.Sym("", 0x0800_0200, 0, ElfBuilder.TypeSection, ElfBuilder.BindLocal));
            b.Symbols.Add(new ElfBuilder.Sym("$t", 0x0800_0100, 0, ElfBuilder.TypeNotTyped, ElfBuilder.BindLocal));
            b.Symbols.Add(new ElfBuilder.Sym("main", 0x0800_0101, 64, ElfBuilder.TypeFunc, ElfBuilder.BindGlobal));
        });
        // builder emits: null entry + unnamed + $t + main => exactly 2 named symbols survive
        Assert.Equal(2, image.Symbols.Count);
        Assert.All(image.Symbols, s => Assert.True(s.Name.Length > 0));
        Assert.Equal(["$t", "main"], image.Symbols.Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void Symbol_fields_roundtrip_for_kinds_and_bindings()
    {
        ElfImage image = Build(b =>
        {
            b.Symbols.Add(new ElfBuilder.Sym("obj", 0x2400_0010, 168, ElfBuilder.TypeObject, ElfBuilder.BindGlobal));
            b.Symbols.Add(new ElfBuilder.Sym("fn", 0x0800_0201, 32, ElfBuilder.TypeFunc, ElfBuilder.BindGlobal));
            b.Symbols.Add(new ElfBuilder.Sym("static_fn", 0x0800_0301, 16, ElfBuilder.TypeFunc, ElfBuilder.BindLocal));
            b.Symbols.Add(new ElfBuilder.Sym("weak_irq", 0x0800_0401, 2, ElfBuilder.TypeFunc, ElfBuilder.BindWeak));
            b.Symbols.Add(new ElfBuilder.Sym("comp_unit.c", 0, 0, ElfBuilder.TypeFile, ElfBuilder.BindLocal));
            b.Symbols.Add(new ElfBuilder.Sym("abs_const", 0x1234, 4, ElfBuilder.TypeObject, ElfBuilder.BindGlobal, ElfBuilder.ShnAbs));
        });

        ElfSymbol Get(string name)
        {
            Assert.True(image.Lookup(name).TryGetSymbol(out ElfSymbol symbol), name);
            return symbol;
        }

        ElfSymbol obj = Get("obj");
        Assert.Equal((0x2400_0010UL, 168UL, ElfSymbolKind.Object, ElfSymbolBinding.Global), (obj.Address, obj.Size, obj.Kind, obj.Binding));

        ElfSymbol fn = Get("fn");
        Assert.Equal((0x0800_0201UL, 32UL, ElfSymbolKind.Function, ElfSymbolBinding.Global), (fn.Address, fn.Size, fn.Kind, fn.Binding));

        Assert.Equal(ElfSymbolBinding.Local, Get("static_fn").Binding);
        Assert.Equal(ElfSymbolBinding.Weak, Get("weak_irq").Binding);
        Assert.Equal(ElfSymbolKind.File, Get("comp_unit.c").Kind);
        Assert.Equal((0x1234UL, 4UL), (Get("abs_const").Address, Get("abs_const").Size));
    }

    [Fact]
    public void Only_symtab_is_read_when_dynsym_is_present()
    {
        ElfImage image = Build(b =>
        {
            b.WithDynsym = true;
            b.DynamicSymbols.Add(new ElfBuilder.Sym("dyn_only", 0x1000, 4, ElfBuilder.TypeObject, ElfBuilder.BindGlobal));
            b.Symbols.Add(new ElfBuilder.Sym("shared", 0x2000, 8, ElfBuilder.TypeObject, ElfBuilder.BindGlobal));
            b.DynamicSymbols.Add(new ElfBuilder.Sym("shared", 0x9999, 8, ElfBuilder.TypeObject, ElfBuilder.BindGlobal));
        });
        Assert.Equal(["shared"], image.Symbols.Select(s => s.Name));
        Assert.True(image.Lookup("shared").TryGetSymbol(out ElfSymbol shared));
        Assert.Equal(0x2000UL, shared.Address);   // the .symtab one, not .dynsym
        Assert.Equal(SymbolLookupStatus.NotFound, image.Lookup("dyn_only").Status);
    }

    [Fact]
    public void Symbols_are_sorted_by_address()
    {
        ElfImage image = Build(b =>
        {
            b.Symbols.Clear();
            b.Symbols.Add(new ElfBuilder.Sym("c", 0x3000, 4, ElfBuilder.TypeObject, ElfBuilder.BindGlobal));
            b.Symbols.Add(new ElfBuilder.Sym("a", 0x1000, 4, ElfBuilder.TypeObject, ElfBuilder.BindGlobal));
            b.Symbols.Add(new ElfBuilder.Sym("b", 0x2000, 4, ElfBuilder.TypeObject, ElfBuilder.BindGlobal));
        });
        Assert.Equal(["a", "b", "c"], image.Symbols.Select(s => s.Name));
    }

    // ---- group G: section reads -------------------------------------------------------

    private static readonly byte[] CommentBytes = "synthetic comment"u8.ToArray();

    [Fact]
    public void TryGetSectionBytes_returns_progbits_contents()
    {
        ElfImage image = Build(b => b.DataSections.Add(
            new ElfBuilder.DataSection(".comment", 0, CommentBytes)));
        Assert.True(image.TryGetSectionBytes(".comment", out byte[] data));
        Assert.Equal(CommentBytes, data);
    }

    [Fact]
    public void TryGetSection_exposes_layout_metadata()
    {
        ElfImage image = Build(b =>
        {
            b.DataSections.Add(new ElfBuilder.DataSection(".text", 0x0800_0000, [0x00, 0xBF, 0x00, 0xBF]));
            b.DataSections.Add(new ElfBuilder.DataSection(".comment", 0, CommentBytes));
        });
        Assert.True(image.TryGetSection(".comment", out ElfSection comment));
        Assert.Equal(0UL, comment.Address);
        Assert.Equal((ulong)CommentBytes.Length, comment.Size);
        Assert.True(comment.HasContents);
        // the first section body starts right after the 52-byte ELF header, 4-aligned
        Assert.True(image.TryGetSection(".text", out ElfSection text));
        Assert.Equal(52, text.FileOffset);
        Assert.Equal(0x0800_0000UL, text.Address);
        Assert.True(image.TryGetSectionBytes(".text", out byte[] textBytes));
        Assert.Equal([0x00, 0xBF, 0x00, 0xBF], textBytes);
    }

    [Fact]
    public void TryGetSectionBytes_returns_false_for_nobits_and_missing_sections()
    {
        ElfImage image = Build(b =>
        {
            b.NoBitsSections.Add(new ElfBuilder.NoBitsSection(".bss", 0x2400_0000, 0x1000));
        });
        Assert.False(image.TryGetSectionBytes(".bss", out byte[] data));
        Assert.Empty(data);
        Assert.True(image.TryGetSection(".bss", out ElfSection bss));
        Assert.False(bss.HasContents);
        Assert.False(image.TryGetSectionBytes(".absent", out _));
    }

    [Fact]
    public void Duplicate_section_names_resolve_to_first()
    {
        ElfImage image = Build(b =>
        {
            b.DataSections.Add(new ElfBuilder.DataSection(".dup", 0x1000, [1, 2]));
            b.DataSections.Add(new ElfBuilder.DataSection(".dup", 0x2000, [3, 4]));
        });
        Assert.True(image.TryGetSectionBytes(".dup", out byte[] data));
        Assert.Equal([1, 2], data);
        Assert.True(image.TryGetSection(".dup", out ElfSection section));
        Assert.Equal(0x1000UL, section.Address);
    }
}
