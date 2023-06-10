using System.Collections.Concurrent;
using RT.Serialization.Settings;
using RT.Util.Collections;
using RT.Util.ExtensionMethods;
using RT.Util.Forms;
using RT.Util.Geometry;

namespace DcsExperiments;

public class Chart : ManagedForm
{
    static Chart()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
    }
    static SettingsFileXml<Dictionary<string, ManagedForm.Settings>> Cfg;

    public class Line
    {
        public Pen Pen = Pens.White;
        public ConcurrentBag<PointD> Points = new();
        public void Add(double x, double y) { Points.Add(new PointD(x, y)); }
    }

    private Bitmap _bmp;

    public double MinX, MinY, MaxX, MaxY;
    public double GridX, GridY;
    public int Border = 10;

    public Pen PenAxis = new Pen(Color.DarkGray, 1);
    public Pen PenGrid = new Pen(Color.MidnightBlue, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };

    public AutoDictionary<string, Line> Lines { get; } = new(_ => new Line());

    public Chart(string title) : base(loadSettings(title))
    {
        Text = title;
        Width = 800;
        Height = 600;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
    }
    private static ManagedForm.Settings loadSettings(string title)
    {
        if (Cfg == null)
            Cfg = new("DcsExperiments.Chart", SettingsLocation.Portable);
        if (!Cfg.Settings.ContainsKey(title))
            Cfg.Settings[title] = new();
        return Cfg.Settings[title];
    }

    private float sx(double wx) => (float)(Border + (wx - MinX) / (MaxX - MinX) * (_bmp.Width - 2 * Border));
    private float sy(double wy) => (float)(_bmp.Height - Border - (wy - MinY) / (MaxY - MinY) * (_bmp.Height - 2 * Border));
    private double wx(float sx) => MinX + (sx - Border) * (MaxX - MinX) / (_bmp.Width - 2 * Border);
    private double wy(float sy) => MinY + (_bmp.Height - Border - sy) * (MaxY - MinY) / (_bmp.Height - 2 * Border);

    public void Repaint()
    {
        paint();
        Invalidate();
    }

    private void paint()
    {
        if (_bmp == null || _bmp.Width != ClientRectangle.Width || _bmp.Height != ClientRectangle.Height)
            _bmp = new Bitmap(ClientRectangle.Width, ClientRectangle.Height);
        using var g = Graphics.FromImage(_bmp);
        g.Clear(Color.Black);
        // Grid
        if (GridX > 0)
            for (var x = (int)(wx(Border) / GridX) * GridX; x <= wx(_bmp.Width - Border); x += GridX)
                if (Math.Abs(x) > GridX * 0.0001 && sx(x) >= Border * 0.9 && sx(x) <= _bmp.Width - Border * 0.9) // x != 0 and allow going slightly beyond border
                    g.DrawLine(PenGrid, sx(x), Border, sx(x), _bmp.Height - Border);
        if (GridY > 0)
            for (var y = (int)(wy(_bmp.Height - Border) / GridY) * GridY; y <= wy(Border); y += GridY)
                if (Math.Abs(y) > GridY * 0.0001 && sy(y) >= Border * 0.9 && sy(y) <= _bmp.Height - Border * 0.9) // y != 0 and allow going slightly beyond border
                    g.DrawLine(PenGrid, Border, sy(y), _bmp.Width - Border, sy(y));
        // Zeroes
        g.DrawLine(PenAxis, sx(0), Border, sx(0), _bmp.Height - Border);
        g.DrawLine(PenAxis, Border, sy(0), _bmp.Width - Border, sy(0));
        // Lines
        g.SetClip(new Rectangle(Border, Border, _bmp.Width - 2 * Border, _bmp.Height - 2 * Border));
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
        foreach (var line in Lines.Values)
            if (line.Points.Count >= 2)
                g.DrawLines(line.Pen, line.Points.Select(p => new PointF(sx(p.X), sy(p.Y))).ToArray());
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (_bmp == null || _bmp.Width != ClientRectangle.Width || _bmp.Height != ClientRectangle.Height)
            paint();
        e.Graphics.DrawImageUnscaled(_bmp, 0, 0);
    }

    protected override void OnSettingsChanged()
    {
        Cfg.SaveInBackground();
    }

    public void AutoscaleY()
    {
        var mm = Lines.Values.SelectMany(l => l.Points).Select(p => p.Y).MinMaxSumCount();
        MinY = mm.Min;
        MaxY = mm.Max;
    }
}
