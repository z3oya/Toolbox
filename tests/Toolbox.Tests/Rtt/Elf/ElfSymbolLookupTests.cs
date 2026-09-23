using Toolbox.Core.Rtt.Elf;

namespace Toolbox.Tests;

/// <summary>Lookup semantics on synthetic images (group D): kind filtering must run BEFORE
/// the uniqueness check, ambiguity must carry every candidate, and unnamed/foreign names must
/// never resolve.</summary>
public class ElfSymbolLookupTests
{
    private static ElfImage Build()
    {
        var builder = new ElfBuilder();
        builder.Symbols.Add(new ElfBuilder.Sym("an_object", 0x2400_0000, 168, ElfBuilder.TypeObject, ElfBuilder.BindGlobal));
        builder.Symbols.Add(new ElfBuilder.Sym("a_function", 0x0800_0101, 32, ElfBuilder.TypeFunc, ElfBuilder.BindGlobal));
        builder.Symbols.Add(new ElfBuilder.Sym("a_file", 0, 0, ElfBuilder.TypeFile, ElfBuilder.BindLocal));
        return ElfImage.Load(builder.Build());
    }

    [Fact]
    public void Found_for_each_kind_when_filter_matches()
    {
        ElfImage image = Build();
        Assert.Equal(SymbolLookupStatus.Found, image.Lookup("an_object", ElfSymbolKind.Object).Status);
        Assert.Equal(SymbolLookupStatus.Found, image.Lookup("a_function", ElfSymbolKind.Function).Status);
        Assert.Equal(SymbolLookupStatus.Found, image.Lookup("a_file", ElfSymbolKind.File).Status);
    }

    [Fact]
    public void Kind_mismatch_is_not_found_even_when_the_name_exists()
    {
        ElfImage image = Build();
        Assert.Equal(SymbolLookupStatus.NotFound, image.Lookup("an_object", ElfSymbolKind.Function).Status);
        Assert.Equal(SymbolLookupStatus.NotFound, image.Lookup("a_function", ElfSymbolKind.Object).Status);
    }

    [Fact]
    public void Unknown_empty_and_failed_image_lookups_are_not_found()
    {
        Assert.Equal(SymbolLookupStatus.NotFound, Build().Lookup("nope").Status);
        Assert.Equal(SymbolLookupStatus.NotFound, Build().Lookup("").Status);
        Assert.Equal(SymbolLookupStatus.NotFound, ElfImage.Load(null!).Lookup("anything").Status);
    }

    [Fact]
    public void Lookup_is_ordinal_case_sensitive()
    {
        Assert.Equal(SymbolLookupStatus.NotFound, Build().Lookup("An_Object").Status);
        Assert.Equal(SymbolLookupStatus.NotFound, Build().Lookup("AN_OBJECT").Status);
    }

    [Fact]
    public void Duplicate_same_kind_names_are_ambiguous_with_ordered_candidates()
    {
        var builder = new ElfBuilder();
        builder.Symbols.Add(new ElfBuilder.Sym("dup", 0x2400_0200, 4, ElfBuilder.TypeObject, ElfBuilder.BindLocal));
        builder.Symbols.Add(new ElfBuilder.Sym("dup", 0x2400_0100, 8, ElfBuilder.TypeObject, ElfBuilder.BindGlobal));
        ElfImage image = ElfImage.Load(builder.Build());

        SymbolLookup lookup = image.Lookup("dup");
        Assert.Equal(SymbolLookupStatus.Ambiguous, lookup.Status);
        Assert.Equal(2, lookup.Candidates.Count);
        Assert.Equal([0x2400_0100UL, 0x2400_0200UL], lookup.Candidates.Select(c => c.Address));   // by address
        Assert.Equal([8UL, 4UL], lookup.Candidates.Select(c => c.Size));   // the 8-byte GLOBAL sits at the lower address
    }

    [Fact]
    public void Kind_filter_disambiguates_a_name_shared_by_object_and_function()
    {
        var builder = new ElfBuilder();
        builder.Symbols.Add(new ElfBuilder.Sym("both", 0x0800_0101, 32, ElfBuilder.TypeFunc, ElfBuilder.BindGlobal));
        builder.Symbols.Add(new ElfBuilder.Sym("both", 0x2400_0000, 168, ElfBuilder.TypeObject, ElfBuilder.BindGlobal));
        ElfImage image = ElfImage.Load(builder.Build());

        SymbolLookup asObject = image.Lookup("both", ElfSymbolKind.Object);
        Assert.Equal(SymbolLookupStatus.Found, asObject.Status);
        Assert.True(asObject.TryGetSymbol(out ElfSymbol obj));
        Assert.Equal(0x2400_0000UL, obj.Address);

        Assert.Equal(SymbolLookupStatus.Found, image.Lookup("both", ElfSymbolKind.Function).Status);
        Assert.Equal(SymbolLookupStatus.Ambiguous, image.Lookup("both").Status);   // unfiltered: genuinely ambiguous
        Assert.Equal(2, image.Lookup("both").Candidates.Count);
    }
}
