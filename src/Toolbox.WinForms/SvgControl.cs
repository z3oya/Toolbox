using System.ComponentModel;
using Svg;

namespace Toolbox.WinForms;

/// <summary>
/// A minimal control that renders an SVG vector image via SVG.NET + GDI+.
/// The SVG is re-rendered at the current control size, so it stays crisp
/// at any size or DPI.
/// </summary>
public class SvgControl : Control
{
    private SvgDocument? _document;
    private Bitmap? _rendered;

    public SvgControl()
    {
        ResizeRedraw = true;
        Size = new Size(64, 64);
    }

    /// <summary>Parsed SVG document. Modify it, then call <see cref="RefreshSvg"/>.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SvgDocument? Document => _document;

    /// <summary>Loads an SVG file from disk.</summary>
    public void LoadSvg(string path)
    {
        _document = SvgDocument.Open(path);
        RebuildRender();
        Invalidate();
    }

    /// <summary>Re-renders after the <see cref="Document"/> has been modified.</summary>
    public void RefreshSvg()
    {
        RebuildRender();
        Invalidate();
    }

    private void RebuildRender()
    {
        _rendered?.Dispose();
        _rendered = null;

        if (_document is null || Width <= 0 || Height <= 0)
        {
            return;
        }

        _document.Width = ClientSize.Width;
        _document.Height = ClientSize.Height;
        _rendered = _document.Draw();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RebuildRender();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_rendered is not null)
        {
            e.Graphics.DrawImage(_rendered, ClientRectangle);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rendered?.Dispose();
        }
        base.Dispose(disposing);
    }
}
