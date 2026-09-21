namespace Toolbox.Tests.Rtt;

using Toolbox.Core.Rtt;

public class RttConnectionConfigTests
{
    [Fact]
    public void Clamped_limits_speed_to_valid_range()
    {
        var tooSlow = new RttConnectionConfig { Chip = "STM32F407VG", SpeedKhz = 0 }.Clamped();
        var tooFast = new RttConnectionConfig { Chip = "STM32F407VG", SpeedKhz = 99_999 }.Clamped();
        Assert.Equal(RttConnectionConfig.MinSpeedKhz, tooSlow.SpeedKhz);
        Assert.Equal(RttConnectionConfig.MaxSpeedKhz, tooFast.SpeedKhz);
    }

    [Fact]
    public void Clamped_trims_chip_and_dll_path()
    {
        var clamped = new RttConnectionConfig
        {
            Chip = "  STM32F407VG  ",
            DllPath = @" C:\JLink_x64.dll ",
        }.Clamped();
        Assert.Equal("STM32F407VG", clamped.Chip);
        Assert.Equal(@"C:\JLink_x64.dll", clamped.DllPath);
    }

    [Fact]
    public void Defaults_match_reference_tool()
    {
        var config = new RttConnectionConfig();
        Assert.Equal(4000, config.SpeedKhz);
        Assert.Equal(RttInterface.Swd, config.Interface);
        Assert.True(config.ResetOnConnect);
        Assert.Equal(0u, config.RttAddress);   // 0 = SDK auto-scan
        Assert.Equal(0u, config.RttRange);
        Assert.Equal(0, config.SerialNo);      // 0 = default probe
        Assert.Equal(string.Empty, config.DllPath);
        Assert.Equal(0, config.Channel);       // 0 = default up/down channel pair
    }
}
