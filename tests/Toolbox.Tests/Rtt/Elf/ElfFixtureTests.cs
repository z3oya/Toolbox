using System.Text;
using Toolbox.Core.Rtt;
using Toolbox.Core.Rtt.Elf;

namespace Toolbox.Tests;

/// <summary>Group H: the submodule against real firmware, with GNU binutils as the independent
/// referee. The .readelf.txt snapshot is ground truth for the GCC image's whole symbol table -
/// every uniquely-named symbol must match on all four fields, duplicate-named symbols must be
/// reported as ambiguous with exactly the reference candidate set, and section byte reads must
/// reproduce the offsets readelf/grep see on the raw file.</summary>
public class ElfFixtureTests
{
    private sealed record Row(uint Value, uint Size, string Type, string Binding);

    private static (Dictionary<string, List<Row>> ByName, int DeclaredCount, int ParsedRows) LoadSnapshot()
    {
        var byName = new Dictionary<string, List<Row>>();
        int declared = 0, parsedRows = 0;
        foreach (string rawLine in File.ReadAllLines(ElfTestSupport.GccReadelfSnapshot))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.StartsWith("Symbol table '.symtab'", StringComparison.Ordinal))
            {
                // "Symbol table '.symtab' contains 2606 entries:" - the count is field 4
                declared = int.Parse(line.Split(' ')[4]);
                continue;
            }
            string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != 8 || !tokens[0].EndsWith(':') || !char.IsAsciiDigit(tokens[0][0])) continue;
            parsedRows++;

            var row = new Row(
                Convert.ToUInt32(tokens[1], fromBase: 16),
                Convert.ToUInt32(tokens[2]),
                tokens[3] switch
                {
                    "FUNC" => nameof(ElfSymbolKind.Function),
                    "OBJECT" => nameof(ElfSymbolKind.Object),
                    "FILE" => nameof(ElfSymbolKind.File),
                    "SECTION" => nameof(ElfSymbolKind.Section),
                    "NOTYPE" => nameof(ElfSymbolKind.NotTyped),
                    _ => tokens[3],   // unknown type: compared as a raw string, will fail loudly
                },
                tokens[4].ToLowerInvariant());
            (byName[tokens[7]] = byName.GetValueOrDefault(tokens[7]) ?? []).Add(row);
        }
        return (byName, declared, parsedRows);
    }

    private static string MapKind(ElfSymbolKind kind) => kind switch
    {
        ElfSymbolKind.Function => nameof(ElfSymbolKind.Function),
        ElfSymbolKind.Object => nameof(ElfSymbolKind.Object),
        ElfSymbolKind.File => nameof(ElfSymbolKind.File),
        ElfSymbolKind.Section => nameof(ElfSymbolKind.Section),
        ElfSymbolKind.NotTyped => nameof(ElfSymbolKind.NotTyped),
        _ => kind.ToString(),
    };

    [Fact]
    public void Gcc_elf_full_table_matches_readelf()
    {
        ElfImage image = ElfImage.FromFile(ElfTestSupport.GccElf);
        Assert.Equal(ElfLoadStatus.Ok, image.Status);
        (Dictionary<string, List<Row>> snapshot, int declared, int parsedRows) = LoadSnapshot();

        // count reconciliation: the snapshot's 8-field rows are the named symbols except
        // SECTION rows (st_name=0, readelf derives the display name). The rows my parser
        // skips (null entry, unnamed ABS FILE) have 7 fields.
        int sectionRows = snapshot.Values.Sum(rows => rows.Count(r => r.Type == nameof(ElfSymbolKind.Section)));
        Assert.Equal(2606, declared);
        Assert.Equal(parsedRows - sectionRows, image.Symbols.Count);

        int checkedUnique = 0, checkedDuplicate = 0;
        foreach ((string name, List<Row> rows) in snapshot)
        {
            SymbolLookup lookup = image.Lookup(name);
            if (rows.Count == 1)
            {
                Row row = rows[0];
                if (row.Type == nameof(ElfSymbolKind.Section)) continue;   // st_name=0: readelf derives the display name, we exclude it
                Assert.Equal(SymbolLookupStatus.Found, lookup.Status);
                Assert.True(lookup.TryGetSymbol(out ElfSymbol ours), name);
                Assert.Equal((row.Value, row.Size, row.Type, row.Binding),
                             (ours.Address, ours.Size, MapKind(ours.Kind), ours.Binding.ToString().ToLowerInvariant()));
                checkedUnique++;
            }
            else
            {
                Assert.Equal(SymbolLookupStatus.Ambiguous, lookup.Status);
                Assert.Equal(rows.Count, lookup.Candidates.Count);
                Assert.Equal(rows.Select(r => (ulong)r.Value).OrderBy(v => v), lookup.Candidates.Select(c => c.Address));
                checkedDuplicate++;
            }
        }
        Assert.True(checkedUnique > 1000, $"only {checkedUnique} unique names compared - snapshot parse broken?");
        Assert.True(checkedDuplicate > 0, "no duplicate-name symbols exercised (mapping symbols expected)");
    }

    [Fact]
    public void Gcc_elf_spot_checks_across_symbol_classes()
    {
        ElfImage image = ElfImage.FromFile(ElfTestSupport.GccElf);

        Assert.True(image.Lookup("tcp_input").TryGetSymbol(out ElfSymbol fn));   // global function
        Assert.Equal((0x0801_7471UL, 2076UL), (fn.Address, fn.Size));

        Assert.True(image.Lookup("memp_NETBUF").TryGetSymbol(out ElfSymbol obj));   // global object in flash
        Assert.Equal((0x0802_1e98UL, 12UL), (obj.Address, obj.Size));

        SymbolLookup weak = image.Lookup("RTC_Alarm_IRQHandler");   // weak IRQ handler
        Assert.Equal(SymbolLookupStatus.Found, weak.Status);
        Assert.True(weak.TryGetSymbol(out ElfSymbol handler));
        Assert.Equal(ElfSymbolBinding.Weak, handler.Binding);

        Assert.Equal(SymbolLookupStatus.NotFound, image.Lookup(".text").Status);   // STT_SECTION stays unresolvable
        Assert.Equal(RttElfLocateStatus.SymbolMissing, RttControlBlock.LocateFromElf(image).Status);   // this build has no RTT
    }

    [Fact]
    public void Keil_axf_resolves_the_control_block_and_symbol()
    {
        ElfImage image = ElfImage.FromFile(ElfTestSupport.KeilAxf);
        Assert.Equal(ElfLoadStatus.Ok, image.Status);
        Assert.True(image.IsLittleEndian);

        SymbolLookup lookup = image.Lookup(RttControlBlock.ControlBlockSymbolName);
        Assert.Equal(SymbolLookupStatus.Found, lookup.Status);
        Assert.True(lookup.TryGetSymbol(out ElfSymbol symbol));
        Assert.Equal(((ulong)ElfTestSupport.KeilCbAddress, ElfTestSupport.KeilCbSize), (symbol.Address, symbol.Size));
    }

    [Fact]
    public void Section_byte_reads_reproduce_raw_file_offsets()
    {
        ElfImage image = ElfImage.FromFile(ElfTestSupport.GccElf);
        Assert.True(image.TryGetSectionBytes(".comment", out byte[] data));
        Assert.True(data.AsSpan().IndexOf("GCC: ("u8) == 0, "expected the comment section to open with the GCC stamp");

        int needleAt = Encoding.ASCII.GetString(data).IndexOf("GCC: (", StringComparison.Ordinal);
        Assert.True(image.TryGetSection(".comment", out ElfSection section));
        Assert.Equal(281_362L, section.FileOffset + needleAt);   // ground truth: grep -abo on the raw file
    }
}
