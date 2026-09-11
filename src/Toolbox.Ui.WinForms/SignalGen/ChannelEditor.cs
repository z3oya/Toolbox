using Toolbox.Core.SignalGen;

namespace Toolbox.Ui.WinForms.SignalGen;

/// <summary>Edits one ChannelConfig. Raises Changed (on the UI thread) whenever any field changes.</summary>
public class ChannelEditor : UserControl
{
    private readonly CheckBox _enabled = new() { Text = "Enabled", AutoSize = true };
    private readonly ComboBox _wave = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly NumericUpDown _freq = NewNum((decimal)ChannelConfig.MinFrequency, (decimal)ChannelConfig.MaxFrequency, 1000m, 1m);
    private readonly NumericUpDown _amp = NewNum((decimal)ChannelConfig.MinAmplitude, (decimal)ChannelConfig.MaxAmplitude, 0.5m, 0.05m);
    private readonly NumericUpDown _phase = NewNum(0m, 360m, 0m, 15m);          // degrees (UI-only domain)
    private readonly NumericUpDown _duty = NewNum(1m, 99m, 50m, 1m);            // percent == Core 0.01..0.99
    private readonly ComboBox _modKind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
    private readonly ComboBox _modWave = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly NumericUpDown _modFreq = NewNum((decimal)ModulationConfig.MinModFrequency, (decimal)ModulationConfig.MaxModFrequency, 10m, 1m);
    private readonly NumericUpDown _depth = NewNum(0m, (decimal)ModulationConfig.MaxAmDepth, 0.5m, 0.05m);
    private readonly Label _depthLabel = new() { Text = "Depth", AutoSize = true };
    private bool _loading;

    public event EventHandler? Changed;

    public ChannelEditor()
    {
        _wave.Items.AddRange(Enum.GetNames<WaveformKind>());
        _wave.SelectedIndex = 0;
        _modKind.Items.AddRange(new object[] { "None", "AM", "FM" });
        _modKind.SelectedIndex = 0;
        _modWave.Items.AddRange(Enum.GetNames<WaveformKind>());
        _modWave.SelectedIndex = 0;

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true, Padding = new Padding(8) };
        grid.Controls.AddRange(new Control[]
        {
            L("Wave"), _wave, L("Frequency (Hz)"), _freq,
            L("Amplitude"), _amp, L("Phase (deg)"), _phase,
            L("Duty (%)"), _duty, _enabled, new Control(),
            L("Mod"), _modKind, L("Mod wave"), _modWave,
            L("Mod freq (Hz)"), _modFreq, _depthLabel, _depth,
        });
        Controls.Add(grid);

        // Correct events per control type (TextChanged is unreliable for ComboBox/NumericUpDown).
        _enabled.CheckedChanged += (_, _) => RaiseIfLive();
        _wave.SelectedIndexChanged += (_, _) => RaiseIfLive();
        _modWave.SelectedIndexChanged += (_, _) => RaiseIfLive();
        foreach (var n in new NumericUpDown[] { _freq, _amp, _phase, _duty, _modFreq, _depth })
            n.ValueChanged += (_, _) => RaiseIfLive();
        _modKind.SelectedIndexChanged += (_, _) =>
        {
            bool fm = _modKind.SelectedIndex == 2;
            _depthLabel.Text = fm ? "Deviation (Hz)" : "Depth";
            _depth.Maximum = fm ? (decimal)ModulationConfig.MaxFmDeviation : (decimal)ModulationConfig.MaxAmDepth;
            _depth.Increment = fm ? 1m : 0.05m;
            RaiseIfLive();
        };
    }

    private static Label L(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left };
    private static NumericUpDown NewNum(decimal min, decimal max, decimal value, decimal step) => new()
    {
        Minimum = min, Maximum = max, Value = value, Increment = step,
        DecimalPlaces = 2, Width = 90, Anchor = AnchorStyles.Left,
    };

    /// <summary>Push a config into the controls (does not raise Changed).</summary>
    public void LoadConfig(ChannelConfig cfg)
    {
        _loading = true;
        _enabled.Checked = cfg.Enabled;
        _wave.SelectedIndex = (int)cfg.Waveform;
        _freq.Value = ClampTo(cfg.Frequency, _freq);
        _amp.Value = ClampTo(cfg.Amplitude, _amp);
        _phase.Value = ClampTo(cfg.Phase * 180.0 / Math.PI, _phase);
        _duty.Value = ClampTo(cfg.DutyCycle * 100.0, _duty);
        _modKind.SelectedIndex = cfg.Mod is null ? 0 : 1 + (int)cfg.Mod.Kind;
        _modWave.SelectedIndex = (int)(cfg.Mod?.ModWave ?? WaveformKind.Sine);
        _modFreq.Value = ClampTo(cfg.Mod?.ModFrequency ?? 10.0, _modFreq);
        _depth.Value = ClampTo(cfg.Mod?.Depth ?? 0.5, _depth);
        _loading = false;
    }

    /// <summary>Build the current config from the controls.</summary>
    public ChannelConfig Current()
    {
        ModulationConfig? mod = _modKind.SelectedIndex switch
        {
            1 => new ModulationConfig { Kind = ModulationKind.Am, ModWave = (WaveformKind)_modWave.SelectedIndex, ModFrequency = (double)_modFreq.Value, Depth = (double)_depth.Value },
            2 => new ModulationConfig { Kind = ModulationKind.Fm, ModWave = (WaveformKind)_modWave.SelectedIndex, ModFrequency = (double)_modFreq.Value, Depth = (double)_depth.Value },
            _ => null,
        };
        return new ChannelConfig
        {
            Enabled = _enabled.Checked,
            Waveform = (WaveformKind)_wave.SelectedIndex,
            Frequency = (double)_freq.Value,
            Amplitude = (double)_amp.Value,
            Phase = (double)_phase.Value * Math.PI / 180.0,
            DutyCycle = (double)_duty.Value / 100.0,
            Mod = mod,
        };
    }

    // (decimal)double.NaN throws OverflowException (and would strand _loading=true); map NaN to the lower bound, matching Core's Sanitize semantics.
    private static decimal ClampTo(double v, NumericUpDown n) => double.IsNaN(v) ? n.Minimum : (decimal)Math.Clamp(v, (double)n.Minimum, (double)n.Maximum);

    private void RaiseIfLive()
    {
        if (!_loading) Changed?.Invoke(this, EventArgs.Empty);
    }
}
