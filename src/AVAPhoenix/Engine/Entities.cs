namespace AVAPhoenix.Engine;

/// <summary>Left and right, the fire button and the shield button.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false, bool Shield = false);

/// <summary>
/// The screen, in pixels: the battle is fought in the column between <see cref="Left"/> and <see cref="Right"/>,
/// with the ship sliding along the bottom at <see cref="ShipY"/>.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Left = 192, Right = 832, Top = 70, Bottom = 768;
    public const double CentreX = (Left + Right) / 2;
    public const double ShipY = 712;
}

public sealed class Ship
{
    public double X = Field.CentreX;
    public bool Alive = true;
    /// <summary>How much longer the shield is up, and how long until it can be raised again.</summary>
    public double ShieldTime, ShieldCool;
    public double FireCool;
    public int Dx;
    public bool Shielded => ShieldTime > 0;
}

/// <summary>The ship's shot, flying straight up.</summary>
public sealed class Shot
{
    public double X, Y;
    public bool Dead;
}

/// <summary>A bird's (or the mothership's) bullet, falling.</summary>
public sealed class Bullet
{
    public double X, Y, Vx, Vy;
    public bool Dead;
}

public enum BirdKind
{
    /// <summary>The small birds of the first two waves: they flutter in formation, and swoop.</summary>
    Flitter,
    /// <summary>The great birds of the next two: they hatch from eggs, and their wings grow back when shot.</summary>
    Emberwing,
}

public enum BirdState
{
    /// <summary>An Emberwing's egg, cracking open: harmless, and it can't be shot.</summary>
    Hatching,
    /// <summary>In its place in the formation (or, for an Emberwing, wheeling about its place).</summary>
    Holding,
    /// <summary>Diving at the ship.</summary>
    Swooping,
    /// <summary>On its way back to its place.</summary>
    Returning,
}

public sealed class Bird
{
    public const int WingSegments = 4;
    public const double BodyRadius = 13, Segment = 11;

    public BirdKind Kind;
    /// <summary>The first wave of each kind (0) or the second, fiercer one (1).</summary>
    public int Variant;
    public BirdState State = BirdState.Holding;
    public double X, Y, Vx, Vy;
    public double HomeX, HomeY;
    /// <summary>How long it's been in its state, and its own place in the flapping and bobbing.</summary>
    public double T, Phase;
    public double FireIn = 2, TargetX;
    public bool Dead;
    /// <summary>An Emberwing's wings, left and right: how many segments are left of each.</summary>
    public readonly int[] Wings = [WingSegments, WingSegments];
    public double RegrowIn;
}

/// <summary>
/// The mothership. Its pilot, the Warden, sits under a dome behind a turning band and a thick hull, which
/// have to be shot through first. Positions of its parts are given from its centre (<see cref="X"/>) and the
/// top of its shell (<see cref="Y"/>).
/// </summary>
public sealed class Mothership
{
    public const int HullRows = 4, HullCols = 44, BandCells = 36;
    public const double CellW = 8, RowH = 10;
    public const double ShellBottom = 56, BandTop = 56, HullTop = 66, HullBottom = HullTop + HullRows * RowH;
    public const double ShellHalfWidth = 200, WardenY = 34, WardenRadius = 14, Window = 14;
    public const double HullLeft = -HullCols * CellW / 2, BandLeft = -BandCells * CellW / 2;

    public double X = Field.CentreX, Y = 92;
    public readonly bool[,] Hull = new bool[HullRows, HullCols];
    public readonly bool[] Band = new bool[BandCells];
    /// <summary>How far the band has turned, in pixels (it wraps round at its width).</summary>
    public double BandShift;
    public bool WardenAlive = true;
    public double Age, FireIn = 1.5, Sway;

    public Mothership()
    {
        for (int r = 0; r < HullRows; r++)
            for (int c = 0; c < HullCols; c++)
                Hull[r, c] = true;
        Array.Fill(Band, true);
    }

    public double BandWidth => BandCells * CellW;

    /// <summary>Which band cell is over a point on the screen (or -1 off the band).</summary>
    public int BandAt(double x)
    {
        double local = x - (X + BandLeft);
        if (local < 0 || local >= BandWidth) return -1;
        double along = ((local - BandShift) % BandWidth + BandWidth) % BandWidth;
        return (int)(along / CellW);
    }

    /// <summary>Which hull column is under a point on the screen (or -1 off the hull).</summary>
    public int HullAt(double x)
    {
        double local = x - (X + HullLeft);
        return local < 0 || local >= HullCols * CellW ? -1 : (int)(local / CellW);
    }
}

public enum EffectKind { Score, Burst, Word, Feathers, Blast }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.7, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
