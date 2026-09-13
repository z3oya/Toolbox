namespace Toolbox.Tests.SerialComm;

using System.Text;
using Toolbox.Core.SerialComm;

public class TextCodecTests
{
    [Fact]
    public void Resolve_maps_each_kind()
    {
        Assert.Same(Encoding.UTF8, TextCodec.Resolve(TextEncodingKind.Utf8));
        Assert.Same(Encoding.ASCII, TextCodec.Resolve(TextEncodingKind.Ascii));
        Assert.Same(Encoding.Latin1, TextCodec.Resolve(TextEncodingKind.Latin1));
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // GBK (cp936); idempotent
        Assert.Equal(936, TextCodec.Resolve(TextEncodingKind.Gbk).CodePage);
    }

    [Fact]
    public void Gbk_roundtrips_chinese_text()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var gbk = TextCodec.Resolve(TextEncodingKind.Gbk);
        const string text = "你好，串口助手 v1";
        Assert.Equal(text, gbk.GetString(gbk.GetBytes(text)));
    }

    [Fact]
    public void Latin1_maps_all_256_bytes_losslessly()
    {
        var latin1 = TextCodec.Resolve(TextEncodingKind.Latin1);
        var bytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        Assert.Equal(bytes, latin1.GetBytes(latin1.GetString(bytes)));
    }
}
