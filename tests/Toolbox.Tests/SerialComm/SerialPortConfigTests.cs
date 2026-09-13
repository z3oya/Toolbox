namespace Toolbox.Tests.SerialComm;

using Toolbox.Core.SerialComm;

public class SerialPortConfigTests
{
    [Fact]
    public void Clamped_leaves_valid_config_unchanged()
    {
        var config = new SerialPortConfig { PortName = "COM3", BaudRate = 115200, DataBits = 8 };
        Assert.Equal(config, config.Clamped());
    }

    [Fact]
    public void Clamped_clamps_baud_and_databits_into_range()
    {
        var low = new SerialPortConfig { PortName = "COM3", BaudRate = 0, DataBits = 9 }.Clamped();
        Assert.Equal(SerialPortConfig.MinBaudRate, low.BaudRate);
        Assert.Equal(SerialPortConfig.MaxDataBits, low.DataBits);

        var high = new SerialPortConfig { PortName = "COM3", BaudRate = 99_999_999, DataBits = 1 }.Clamped();
        Assert.Equal(SerialPortConfig.MaxBaudRate, high.BaudRate);
        Assert.Equal(SerialPortConfig.MinDataBits, high.DataBits);
    }

    [Fact]
    public void Clamped_trims_port_name()
    {
        var config = new SerialPortConfig { PortName = "  COM3  " }.Clamped();
        Assert.Equal("COM3", config.PortName);
    }
}
