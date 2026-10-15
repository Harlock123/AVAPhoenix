using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAPhoenix.Engine;
using Effect = AVAPhoenix.Engine.Effect;

namespace AVAPhoenix.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAPhoenix/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFFA090C0, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF, Pink = 0xFFFF6AD0;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        DrawBackdrop(dc);
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;
        if (inGame)
        {
            if (g.Nest is { } n) DrawNest(dc, n, g.Phase == Phase.Clear ? g.PhaseTime : 0);
            foreach (var b in g.Birds.Where(b => !b.Dead)) DrawBird(dc, b);
            foreach (var s in g.Shots)
            {
                dc.FillRectangle(Brush(Alpha(Yellow, 0.35)), new Rect(s.X - 3, s.Y - 2, 6, 18));
                dc.FillRectangle(Brush(0xFFFFFFE0), new Rect(s.X - 1.5, s.Y, 3, 14));
            }
            foreach (var b in g.Bullets)
            {
                dc.DrawEllipse(Brush(Alpha(Red, 0.35)), null, new Point(b.X, b.Y), 6, 8);
                dc.DrawLine(Pen(0xFFFFD070, 3), new Point(b.X - b.Vx * 0.03, b.Y - 8), new Point(b.X, b.Y + 4));
            }
            if (g.Ship.Alive) DrawShip(dc, new Point(g.Ship.X, Field.ShipY), g.Ship.Shielded ? g.Ship.ShieldTime : 0, 1);
            DrawPanels(dc);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 18), 26, Yellow, center: true);
    }

    /// <summary>Deep space, its stars drifting down past; the battle's column a shade lighter than the panels beside it.</summary>
    private void DrawBackdrop(DrawingContext dc)
    {
        dc.FillRectangle(Brush(0xFF0C0618), new Rect(0, 0, Field.Width, Field.Height));
        bool inGame = Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;
        if (inGame) dc.FillRectangle(Brush(0xFF02020C), new Rect(Field.Left, Field.Top, Field.Right - Field.Left, Field.Bottom - Field.Top));
        double left = inGame ? Field.Left : 0, width = inGame ? Field.Right - Field.Left : Field.Width;
        for (int k = 0; k < 120; k++)
        {
            double speed = 10 + Hash(k, 2, 9) % 40;
            double x = left + Hash(k, 1, 9) % (uint)width;
            double y = (Hash(k, 3, 9) % 768 + _clock * speed) % 768;
            if (inGame && y < Field.Top) continue;
            double s = 1 + Hash(k, 4, 9) % 2;
            double tw = 0.5 + 0.5 * Math.Sin(_clock * (2 + k % 5) + k);
            uint colour = (k % 7) switch { 0 => 0xFFFFC0A0, 1 => 0xFFA0C0FF, _ => 0xFFFFFFFF };
            dc.FillRectangle(Brush(Alpha(colour, 0.3 + 0.5 * tw * s / 2)), new Rect(x, y, s, s));
        }
        if (inGame)
        {
            dc.DrawLine(Pen(0xFF4A2A7A, 2), new Point(Field.Left, Field.Top), new Point(Field.Left, Field.Bottom));
            dc.DrawLine(Pen(0xFF4A2A7A, 2), new Point(Field.Right, Field.Top), new Point(Field.Right, Field.Bottom));
        }
    }

    // ───────────────────────────── the ship ─────────────────────────────

    /// <summary>The ship: a red and white fighter with a flickering exhaust; under its shield, a shimmering dome.</summary>
    private void DrawShip(DrawingContext dc, Point at, double shield, double scale)
    {
        double k = scale;
        Point P(double x, double y) => new(at.X + x * k, at.Y + y * k);
        double flick = 6 + 4 * Math.Sin(_clock * 40);
        Poly(dc, 0xFFFFB040, null, 0, P(-5, 14), P(5, 14), P(0, 14 + flick));
        // Wings.
        Poly(dc, 0xFFD02A2A, 0xFF7A0A0A, 1.4, P(-22, 12), P(-6, -6), P(6, -6), P(22, 12), P(10, 14), P(-10, 14));
        Poly(dc, 0xFFFFFFFF, null, 0, P(-22, 12), P(-18, 6), P(-8, 10), P(-10, 14));
        Poly(dc, 0xFFFFFFFF, null, 0, P(22, 12), P(18, 6), P(8, 10), P(10, 14));
        // Wingtip guns.
        dc.FillRectangle(Brush(0xFFC0C8D8), new Rect(at.X - 21 * k, at.Y + 2 * k, 3 * k, 10 * k));
        dc.FillRectangle(Brush(0xFFC0C8D8), new Rect(at.X + 18 * k, at.Y + 2 * k, 3 * k, 10 * k));
        // Body and cockpit.
        Poly(dc, 0xFFF0F0F8, 0xFF6A6A80, 1.2, P(0, -22), P(7, -4), P(6, 14), P(-6, 14), P(-7, -4));
        dc.DrawEllipse(Brush(0xFF2AA0FF), Pen(0xFF0A3A7A, 1), P(0, -4), 3.5 * k, 6 * k);
        if (shield > 0)
        {
            double a = Math.Min(1, shield / 0.3);
            double pulse = 0.5 + 0.5 * Math.Sin(_clock * 30);
            dc.DrawEllipse(Brush(Alpha(Cyan, 0.18 * a)), Pen(Alpha(0xFFB0F4FF, (0.6 + 0.4 * pulse) * a), 3), P(0, 0), 34 * k, 30 * k);
            for (int i = 0; i < 6; i++)
            {
                double ang = _clock * 4 + i * Math.PI / 3;
                dc.DrawEllipse(Brush(Alpha(0xFFFFFFFF, 0.7 * a)), null, P(Math.Cos(ang) * 34, Math.Sin(ang) * 30), 2, 2);
            }
        }
    }

    // ───────────────────────────── the birds ─────────────────────────────

    private void DrawBird(DrawingContext dc, Bird b)
    {
        if (b.Kind == BirdKind.Flitter) DrawFlitter(dc, new Point(b.X, b.Y), b.Variant, b.T + b.Phase, b.State is BirdState.Swooping or BirdState.Returning, 1);
        else DrawEmberwing(dc, b, new Point(b.X, b.Y), 1);
    }

    /// <summary>A Flitter: a small bird, its wings beating quicker when it dives.</summary>
    private static void DrawFlitter(DrawingContext dc, Point at, int variant, double t, bool diving, double scale)
    {
        double k = scale;
        uint body = variant == 0 ? Pink : Cyan, wing = variant == 0 ? 0xFFB03AE0 : 0xFF2A7AFF, tip = variant == 0 ? 0xFFFFE14A : 0xFFFFFFFF;
        double flap = Math.Sin(t * (diving ? 22 : 12));
        Point P(double x, double y) => new(at.X + x * k, at.Y + y * k);
        foreach (int s in new[] { -1, 1 })
        {
            double up = flap * 9;
            Poly(dc, wing, null, 0, P(s * 4, -2), P(s * 15, -4 - up), P(s * 19, 2 - up * 0.6), P(s * 5, 5));
            dc.DrawLine(Pen(tip, 1.6), P(s * 15, -4 - up), P(s * 19, 2 - up * 0.6));
        }
        dc.DrawEllipse(Brush(body), Pen(0xFF3A0A4A, 1), P(0, 1), 6 * k, 8 * k);
        dc.DrawEllipse(Brush(body), Pen(0xFF3A0A4A, 1), P(0, -7), 5 * k, 4.5 * k);
        Poly(dc, 0xFFFFC020, null, 0, P(-2, -4), P(2, -4), P(0, 1));
        dc.DrawEllipse(Brush(0xFFFFFFFF), null, P(-2.2, -8), 1.8 * k, 1.8 * k);
        dc.DrawEllipse(Brush(0xFFFFFFFF), null, P(2.2, -8), 1.8 * k, 1.8 * k);
        // Tail.
        Poly(dc, wing, null, 0, P(-3, 8), P(3, 8), P(0, 14));
    }

    /// <summary>An Emberwing: a great fire-bird with feathered wings, each made of segments that can be shot away (and grow back). Before it hatches, a glowing egg.</summary>
    private void DrawEmberwing(DrawingContext dc, Bird b, Point at, double scale)
    {
        double k = scale;
        Point P(double x, double y) => new(at.X + x * k, at.Y + y * k);
        uint main = GameEngine.EmberColour(b), deep = b.Variant == 0 ? 0xFFB01A0A : 0xFF5A1AA0, bright = b.Variant == 0 ? 0xFFFFD040 : 0xFFE0B0FF;
        if (b.State == BirdState.Hatching)
        {
            if (b.T < 0) return;
            double grow = Math.Clamp(b.T / GameEngine.HatchSeconds, 0, 1);
            double glow = 0.5 + 0.5 * Math.Sin(_clock * 12);
            dc.DrawEllipse(Brush(Alpha(main, 0.25 + 0.2 * glow)), null, at, 20 * grow + 6, 24 * grow + 6);
            dc.DrawEllipse(Brush(bright), Pen(deep, 1.5), at, 10 + 4 * grow, 13 + 5 * grow);
            if (grow > 0.5)
            {
                dc.DrawLine(Pen(deep, 1.5), P(-8, -2), P(-3, 3));
                dc.DrawLine(Pen(deep, 1.5), P(-3, 3), P(2, -3));
                dc.DrawLine(Pen(deep, 1.5), P(2, -3), P(7, 2));
            }
            return;
        }
        bool diving = b.State is BirdState.Swooping or BirdState.Returning;
        double flap = Math.Sin((b.T + b.Phase) * (diving ? 9 : 5));
        // Wings: four feathered segments a side, rising and falling with the beat.
        foreach (int s in new[] { -1, 1 })
        {
            int segs = b.Wings[s < 0 ? 0 : 1];
            for (int i = 0; i < segs; i++)
            {
                double x0 = Bird.BodyRadius + i * Bird.Segment - 1, x1 = x0 + Bird.Segment + 1;
                double y0 = -flap * i * 4, y1 = -flap * (i + 1) * 4;
                uint colour = i switch { 0 => deep, 1 => main, 2 => main, _ => bright };
                Poly(dc, colour, Alpha(0xFF000000, 0.5), 1, P(s * x0, y0 - 6), P(s * x1, y1 - 7), P(s * x1, y1 + 5 + i), P(s * x0, y0 + 6));
                dc.DrawLine(Pen(Alpha(bright, 0.8), 1.2), P(s * (x0 + 2), y0 + 4), P(s * (x1 - 1), y1 + 6 + i));
            }
            // A ragged stump where the wing was shot away.
            if (segs < Bird.WingSegments)
            {
                double xe = Bird.BodyRadius + segs * Bird.Segment, ye = -flap * segs * 4;
                dc.DrawEllipse(Brush(Alpha(0xFFFFE080, 0.5 + 0.4 * Math.Sin(_clock * 20))), null, P(s * xe, ye), 3, 4);
            }
        }
        // Tail feathers.
        Poly(dc, deep, null, 0, P(-6, 10), P(0, 26), P(6, 10));
        Poly(dc, bright, null, 0, P(-2, 12), P(0, 22), P(2, 12));
        dc.DrawEllipse(Brush(main), Pen(deep, 1.5), P(0, 2), Bird.BodyRadius * k, 14 * k);
        dc.DrawEllipse(Brush(Alpha(bright, 0.7)), null, P(0, 5), 6 * k, 8 * k);
        // The head: a crest, fierce eyes and a hooked beak.
        dc.DrawEllipse(Brush(main), Pen(deep, 1.2), P(0, -12), 8 * k, 7 * k);
        Poly(dc, bright, null, 0, P(-4, -17), P(0, -27 - flap * 2), P(4, -17));
        Poly(dc, 0xFFFFFFFF, null, 0, P(-6, -14), P(-1.5, -12), P(-6, -11));
        Poly(dc, 0xFFFFFFFF, null, 0, P(6, -14), P(1.5, -12), P(6, -11));
        Poly(dc, 0xFFFFC020, 0xFF7A4A00, 1, P(-3, -9), P(3, -9), P(0, -2));
    }

    // ───────────────────────────── the mothership ─────────────────────────────

    /// <summary>The mothership: a great saucer, the Warden under its dome, the turning band and the thick hull below.</summary>
    private void DrawNest(DrawingContext dc, Mothership n, double downFor)
    {
        double shake = downFor > 0 ? Math.Sin(_clock * 60) * 4 : 0;
        if (downFor > 1.6) return;
        double x = n.X + shake, y = n.Y + downFor * downFor * 60;
        Point P(double dx, double dy) => new(x + dx, y + dy);
        double sb = Mothership.ShellBottom, hw = Mothership.ShellHalfWidth;
        // The shell.
        Poly(dc, 0xFF3A1A6A, 0xFF8A5AE0, 2, P(-hw, sb), P(-150, 20), P(-70, -4), P(70, -4), P(150, 20), P(hw, sb));
        Poly(dc, 0xFF5A2A9A, null, 0, P(-hw + 20, sb - 4), P(-140, 24), P(-60, 6), P(60, 6), P(140, 24), P(hw - 20, sb - 4));
        for (int i = 0; i < 9; i++)
        {
            double lx = -170 + i * 42.5, ly = sb - 12;
            bool on = ((int)(_clock * 6) + i) % 3 == 0;
            dc.DrawEllipse(Brush(on ? 0xFFFFE14A : 0xFF7A5A20), null, P(lx, ly), 3.5, 3.5);
        }
        // The Warden's window and the Warden.
        dc.FillRectangle(Brush(0xFF0A1A30), new Rect(x - Mothership.Window, y + 10, Mothership.Window * 2, sb - 10));
        dc.DrawEllipse(Brush(0xFF0A1A30), Pen(0xFF9AE0FF, 2), P(0, Mothership.WardenY), 24, 22);
        if (n.WardenAlive)
        {
            double bob = Math.Sin(_clock * 3) * 1.5;
            dc.DrawEllipse(Brush(0xFF4AE06A), Pen(0xFF0A5A1A, 1.5), P(0, Mothership.WardenY + bob), 12, 13);
            dc.DrawEllipse(Brush(0xFF000000), null, P(-5, Mothership.WardenY - 2 + bob), 4, 5);
            dc.DrawEllipse(Brush(0xFF000000), null, P(5, Mothership.WardenY - 2 + bob), 4, 5);
            dc.DrawEllipse(Brush(0xFFFF3030), null, P(-5, Mothership.WardenY - 2 + bob), 1.5, 1.5);
            dc.DrawEllipse(Brush(0xFFFF3030), null, P(5, Mothership.WardenY - 2 + bob), 1.5, 1.5);
            dc.DrawLine(Pen(0xFF4AE06A, 2), P(-6, Mothership.WardenY - 11 + bob), P(-11, Mothership.WardenY - 20 + bob));
            dc.DrawLine(Pen(0xFF4AE06A, 2), P(6, Mothership.WardenY - 11 + bob), P(11, Mothership.WardenY - 20 + bob));
        }
        dc.DrawEllipse(Brush(Alpha(0xFFFFFFFF, 0.15)), null, P(-8, Mothership.WardenY - 8), 8, 5);
        // The band, turning.
        double bl = x + Mothership.BandLeft, bt = y + Mothership.BandTop;
        dc.FillRectangle(Brush(0xFF101830), new Rect(bl, bt, n.BandWidth, Mothership.RowH));
        for (int i = 0; i < Mothership.BandCells; i++)
        {
            if (!n.Band[i]) continue;
            double local = (i * Mothership.CellW + n.BandShift) % n.BandWidth;
            if (local + Mothership.CellW > n.BandWidth) continue;
            dc.FillRectangle(Brush(i % 2 == 0 ? 0xFF6AE0FF : 0xFFE0F8FF), new Rect(bl + local + 0.5, bt + 1, Mothership.CellW - 1, Mothership.RowH - 2));
        }
        // The hull.
        double hl = x + Mothership.HullLeft;
        for (int r = 0; r < Mothership.HullRows; r++)
            for (int c = 0; c < Mothership.HullCols; c++)
            {
                if (!n.Hull[r, c]) continue;
                uint colour = (r + c) % 2 == 0 ? 0xFFE06A1A : 0xFFC0501A;
                dc.FillRectangle(Brush(colour), new Rect(hl + c * Mothership.CellW, y + Mothership.HullTop + r * Mothership.RowH, Mothership.CellW - 0.6, Mothership.RowH - 0.6));
            }
        dc.DrawLine(Pen(0xFF8A5AE0, 2), P(-hw, sb), P(hw, sb));
    }

    // ───────────────────────────── effects ─────────────────────────────

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (8 + 36 * t) * e.Size;
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, 1 - t), 3), c, r, r);
                for (int k = 0; k < 10; k++)
                {
                    double a = k * Math.PI / 5 + Hash(k, (int)e.X, 1) % 10 * 0.06, d = r * (0.6 + Hash(k, 2, 3) % 10 * 0.06);
                    dc.FillRectangle(Brush(Alpha(0xFFFFF0C0, 1 - t)), new Rect(c.X + Math.Cos(a) * d - 2, c.Y + Math.Sin(a) * d - 2, 4, 4));
                }
                break;
            }
            case EffectKind.Feathers:
                for (int k = 0; k < 4; k++)
                {
                    double sway = Math.Sin(e.Age * 8 + k) * 8, fall = t * 40 + k * 4;
                    var at = new Point(c.X + (k - 1.5) * 8 + sway, c.Y + fall);
                    Poly(dc, Alpha(e.Colour, 1 - t), null, 0, new Point(at.X - 2, at.Y), new Point(at.X, at.Y - 7), new Point(at.X + 2, at.Y), new Point(at.X, at.Y + 4));
                }
                break;
            case EffectKind.Blast:
            {
                double r = (16 + 70 * Math.Sqrt(t)) * e.Size;
                dc.DrawEllipse(Brush(Alpha(0xFFFFE080, (1 - t) * 0.8)), null, c, r * 0.6, r * 0.6);
                dc.DrawEllipse(null, Pen(Alpha(Fire, 1 - t), 6), c, r, r * 0.8);
                for (int k = 0; k < 18; k++)
                {
                    double a = k * Math.PI / 9 + Hash(k, (int)e.X, 5) % 10 * 0.05, d = r * (0.7 + Hash(k, 3, 5) % 10 * 0.05);
                    uint col = k % 3 == 0 ? 0xFFFFFFFF : k % 3 == 1 ? Fire : Yellow;
                    dc.FillRectangle(Brush(Alpha(col, 1 - t)), new Rect(c.X + Math.Cos(a) * d - 3, c.Y + Math.Sin(a) * d * 0.8 - 3, 6, 6));
                }
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - 14 - t * 24), 26, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 40, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, panels and screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 18), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 48), 36, Yellow);
        Text(dc, "HI", new Point(Field.Width - 16, 18), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 48), 30, Dim, right: true);
        if (g.State is GameState.Playing or GameState.Attract or GameState.Paused)
            Text(dc, $"WAVE {g.Wave}", new Point(Field.Width / 2, 36), 38, White, center: true);
    }

    /// <summary>The spare ships and the shield on the left; the waves of the cycle and what they're worth on the right.</summary>
    private void DrawPanels(DrawingContext dc)
    {
        var g = Engine;
        double lx = Field.Left / 2, rx = Field.Right + (Field.Width - Field.Right) / 2;
        Text(dc, "SHIPS", new Point(lx, 120), 30, Dim, center: true);
        int spare = Math.Max(0, g.Lives - (g.Ship.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 8); i++)
            DrawShip(dc, new Point(lx - 48 + i % 3 * 48, 168 + i / 3 * 46), 0, 0.8);

        Text(dc, "SHIELD", new Point(lx, 330), 30, Dim, center: true);
        var s = g.Ship;
        double charge = s.Shielded ? s.ShieldTime / GameEngine.ShieldSeconds : 1 - s.ShieldCool / GameEngine.ShieldRecharge;
        bool ready = !s.Shielded && s.ShieldCool <= 0;
        var bar = new Rect(lx - 70, 356, 140, 18);
        dc.FillRectangle(Brush(0xFF1A1030), bar);
        dc.FillRectangle(Brush(s.Shielded ? Cyan : ready ? 0xFF4AE06A : 0xFF3A6AA0), new Rect(bar.X, bar.Y, bar.Width * Math.Clamp(charge, 0, 1), bar.Height));
        dc.DrawRectangle(null, Pen(Dim, 1.5), bar);
        Text(dc, s.Shielded ? "UP" : ready ? "READY" : "CHARGING", new Point(lx, 396), 26, ready ? 0xFF4AE06A : s.Shielded ? Cyan : Dim, center: true);

        Text(dc, "THE WAVES", new Point(rx, 120), 30, Dim, center: true);
        for (int i = 0; i < GameEngine.WavesPerCycle; i++)
        {
            double y = 168 + i * 46;
            bool now = i == g.Stage;
            if (now) dc.FillRectangle(Brush(0x40FFE14A), new Rect(rx - 86, y - 20, 172, 40), 4);
            var at = new Point(rx - 60, y);
            if (i < 2) DrawFlitter(dc, at, i, _clock, false, 1);
            else if (i < 4) DrawEmberwing(dc, new Bird { Kind = BirdKind.Emberwing, Variant = i - 2, State = BirdState.Holding, T = _clock }, at, 0.42);
            else DrawMiniNest(dc, at);
            string what = i switch { 0 or 1 => "FLITTERS", 2 or 3 => "EMBERWINGS", _ => "MOTHERSHIP" };
            Text(dc, what, new Point(rx - 26, y), 24, now ? Yellow : White);
        }
        Text(dc, $"CYCLE {g.Cycle}", new Point(rx, 420), 28, Dim, center: true);
    }

    private static void DrawMiniNest(DrawingContext dc, Point at)
    {
        Poly(dc, 0xFF5A2A9A, 0xFF8A5AE0, 1, new Point(at.X - 24, at.Y + 4), new Point(at.X - 14, at.Y - 6), new Point(at.X + 14, at.Y - 6), new Point(at.X + 24, at.Y + 4));
        dc.FillRectangle(Brush(0xFF6AE0FF), new Rect(at.X - 16, at.Y + 4, 32, 3));
        dc.FillRectangle(Brush(0xFFE06A1A), new Rect(at.X - 20, at.Y + 7, 40, 5));
        dc.DrawEllipse(Brush(0xFF4AE06A), null, new Point(at.X, at.Y - 2), 3, 3);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 470;
        var (w, h) = (Math.Min(Field.Right - Field.Left, Math.Max(msg.Length * 28, (g.SubMessage?.Length ?? 0) * 15) + 60), g.SubMessage != null ? 110.0 : 74.0);
        dc.FillRectangle(Brush(0xC0100820), new Rect(cx - w / 2, cy - 48, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 16 ? 44 : 56, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 36), 28, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        DrawEmberwing(dc, new Bird { Kind = BirdKind.Emberwing, State = BirdState.Holding, T = _clock }, new Point(cx, 210), 2.2);
        Text(dc, "AVA PHOENIX", new Point(cx, 108), 110, Fire, center: true);
        Text(dc, "AFTER THE 1980 ARCADE CLASSIC", new Point(cx, 296), 28, Dim, center: true);
        string[] lines =
        [
            "SHOOT DOWN THE FLITTERS, THEN THE EMBERWINGS - THEIR WINGS GROW BACK",
            "BORE THROUGH THE MOTHERSHIP'S HULL AND BAND TO REACH THE WARDEN",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 338 + i * 30), 25, White, center: true);
        double[] xs = [cx - 330, cx - 110, cx + 110, cx + 330];
        DrawFlitter(dc, new Point(xs[0], 430), 0, _clock, false, 1.4);
        DrawEmberwing(dc, new Bird { Kind = BirdKind.Emberwing, State = BirdState.Holding, T = _clock, Variant = 1 }, new Point(xs[1], 428), 0.8);
        DrawMiniNest(dc, new Point(xs[2], 432));
        DrawShip(dc, new Point(xs[3], 430), 0.8, 1);
        (string Name, uint Colour, string A, string B)[] table =
        [
            ("FLITTER", Pink, "20-40", "DIVING 80"),
            ("EMBERWING", 0xFFFF9A4A, "100-150", "DIVING 400-600"),
            ("THE WARDEN", 0xFF4AE06A, "2000-9000", "THE QUICKER THE MORE"),
            ("SHIELD", Cyan, "STOPS ANYTHING", "BUT HOLDS YOU STILL"),
        ];
        for (int i = 0; i < table.Length; i++)
        {
            Text(dc, table[i].Name, new Point(xs[i], 472), 26, table[i].Colour, center: true);
            Text(dc, table[i].A, new Point(xs[i], 498), 24, Yellow, center: true);
            Text(dc, table[i].B, new Point(xs[i], 522), 22, Dim, center: true);
        }
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 590), 46, Yellow, center: true);
        Text(dc, "LEFT/RIGHT: FLY    SPACE: FIRE    DOWN OR X: SHIELD", new Point(cx, 660), 26, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "THE ACES", new Point(cx, 130), 72, Fire, center: true);
        Text(dc, "RANK NAME    SCORE   WAVE", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
