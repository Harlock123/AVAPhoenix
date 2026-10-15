namespace AVAPhoenix.Engine;

public sealed partial class GameEngine
{
    /// <summary>The waves go round in fives: two of Flitters, two of Emberwings, then the mothership.</summary>
    public const int WavesPerCycle = 5;
    public const double ShipSpeed = 330, ShotSpeed = 760, FireRepeat = 0.22;
    public const double ShieldSeconds = 1.4, ShieldRecharge = 5.5;
    public const double StartSeconds = 2.4, ReadySeconds = 1.6, DyingSeconds = 2.2, ClearSeconds = 2.0, NestDownSeconds = 3.2;
    public const double HatchSeconds = 1.2;
    public const int MaxShots = 2;
    public const int FirstExtraLife = 20000, ExtraLifeEvery = 60000;
    public const int WingPoints = 10;
    /// <summary>How low the mothership and the Emberwings come before they come no lower.</summary>
    public const double NestLowest = 300, EmberLowest = 430;

    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Ship Ship { get; } = new();
    public List<Shot> Shots { get; } = [];
    public List<Bullet> Bullets { get; } = [];
    public List<Bird> Birds { get; } = [];
    public Mothership? Nest { get; private set; }
    public ShipPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public double WaveTime { get; private set; }
    /// <summary>For tests: nothing fires, swoops or comes down, so a test can set up just what it wants.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra;
    private double _breakIn;
    private bool _firePrev, _shieldPrev, _ready;

    public int Cycle => (Wave - 1) / WavesPerCycle + 1;
    /// <summary>Which of the five waves of a cycle this is: 0 and 1 Flitters, 2 and 3 Emberwings, 4 the mothership.</summary>
    public int Stage => (Wave - 1) % WavesPerCycle;
    public double Pace => Math.Min(1.8, 1 + 0.18 * (Cycle - 1));
    /// <summary>The whole formation drifts slowly from side to side.</summary>
    public double Sway => Math.Sin(WaveTime * 0.5) * 40;
    private int MaxBullets => Math.Min(10, 4 + 2 * Cycle);

    public static string NameOf(int wave) => ((wave - 1) % WavesPerCycle) switch
    {
        0 => "THE FLITTERS", 1 => "THE FLITTERS RETURN", 2 => "THE EMBERWINGS HATCH", 3 => "THE EMBERWINGS RETURN", _ => "THE MOTHERSHIP",
    };

    /// <summary>What a bird is worth: more on the wing than in its place, and more for the fiercer ones.</summary>
    public static int Points(Bird b)
    {
        bool flying = b.State is BirdState.Swooping or BirdState.Returning;
        return b.Kind == BirdKind.Flitter
            ? (flying ? 80 : 20 + 20 * b.Variant)
            : (flying ? 400 + 200 * b.Variant : 100 + 50 * b.Variant);
    }

    /// <summary>The Warden is worth more the quicker it's brought down.</summary>
    public static int WardenPoints(double seconds) => Math.Max(2000, 9000 - 1000 * (int)(seconds / 10));

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        _nextExtra = FirstExtraLife;
        LayOut();
        SetPhase(Phase.Start);
    }

    private void BeginWave(int wave)
    {
        Wave = wave;
        LayOut();
        SetPhase(Phase.Start);
        _ready = false;
        Say($"WAVE {wave}", NameOf(wave), StartSeconds);
        Play(Sfx.WaveStart);
    }

    private void LayOut()
    {
        Shots.Clear();
        Bullets.Clear();
        Birds.Clear();
        Effects.Clear();
        Nest = null;
        WaveTime = 0;
        _breakIn = 2;
        ResetShip();
        switch (Stage)
        {
            case 0 or 1:
                for (int r = 0; r < 2; r++)
                    for (int i = 0; i < 8; i++)
                        AddBird(BirdKind.Flitter, Stage, Field.CentreX + (i - 3.5) * 58, 160 + r * 52);
                break;
            case 2 or 3:
                for (int r = 0; r < 2; r++)
                    for (int i = 0; i < 4; i++)
                    {
                        var b = AddBird(BirdKind.Emberwing, Stage - 2, Field.CentreX + (i - 1.5) * 130 + (r == 1 ? 35 : -35), 170 + r * 90);
                        b.State = BirdState.Hatching;
                        b.T = -(i * 0.25 + r * 0.6);
                    }
                break;
            default:
                Nest = new Mothership();
                for (int i = 0; i < 6; i++) AddBird(BirdKind.Flitter, 1, Field.CentreX + (i - 2.5) * 80, Nest.Y + 160);
                break;
        }
    }

    private Bird AddBird(BirdKind kind, int variant, double x, double y)
    {
        var b = new Bird
        {
            Kind = kind, Variant = variant, X = x, Y = y, HomeX = x, HomeY = y, Phase = _rng.NextDouble() * Math.PI * 2,
            FireIn = 1 + _rng.NextDouble() * 2, RegrowIn = 1.5,
        };
        Birds.Add(b);
        return b;
    }

    private void ResetShip()
    {
        var s = Ship;
        s.X = Field.CentreX;
        s.Alive = true;
        s.ShieldTime = s.ShieldCool = s.FireCool = 0;
        s.Dx = 0;
    }

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        switch (Phase)
        {
            case Phase.Start:
                MoveShip(c, dt, canFire: false);
                MoveBirds(dt, active: false);
                MoveNest(dt, active: false);
                if (PhaseTime > (_ready ? ReadySeconds : StartSeconds))
                {
                    SetPhase(Phase.Play);
                    if (Nest != null && State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Play:
                WaveTime += dt;
                MoveShip(c, dt, canFire: true);
                MoveShots(dt);
                MoveBirds(dt, active: true);
                MoveNest(dt, active: true);
                MoveBullets(dt);
                Hits();
                if (Phase == Phase.Play) Progress();
                break;
            case Phase.Dying:
                MoveShots(dt);
                MoveBirds(dt, active: false);
                MoveNest(dt, active: false);
                MoveBullets(dt);
                if (PhaseTime > DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Respawn();
                }
                break;
            case Phase.Clear:
                MoveShots(dt);
                MoveBirds(dt, active: false);
                if (PhaseTime > (Nest != null ? NestDownSeconds : ClearSeconds)) BeginWave(Wave + 1);
                break;
        }
    }

    // ───────────────────────────── the ship ─────────────────────────────

    private void MoveShip(Controls c, double dt, bool canFire)
    {
        var s = Ship;
        bool fresh = c.Fire && !_firePrev, raise = c.Shield && !_shieldPrev;
        _firePrev = c.Fire;
        _shieldPrev = c.Shield;
        if (!s.Alive) return;
        if (s.ShieldTime > 0) s.ShieldTime = Math.Max(0, s.ShieldTime - dt);
        else if (s.ShieldCool > 0) s.ShieldCool = Math.Max(0, s.ShieldCool - dt);
        if (raise && canFire && s.ShieldTime <= 0 && s.ShieldCool <= 0)
        {
            s.ShieldTime = ShieldSeconds;
            s.ShieldCool = ShieldRecharge;
            Play(Sfx.Shield);
        }
        // The shield holds the ship still.
        s.Dx = s.Shielded ? 0 : c.Dx;
        s.X = Math.Clamp(s.X + s.Dx * ShipSpeed * dt, Field.Left + 24, Field.Right - 24);
        s.FireCool -= dt;
        if (canFire && c.Fire && (fresh || s.FireCool <= 0) && !s.Shielded && Shots.Count < MaxShots)
        {
            Shots.Add(new Shot { X = s.X, Y = Field.ShipY - 22 });
            s.FireCool = FireRepeat;
            Play(Sfx.Fire);
        }
    }

    private void MoveShots(double dt)
    {
        foreach (var s in Shots)
        {
            // In small steps, so a shot can't jump over a row of the mothership's hull.
            for (int k = 0; k < 4 && !s.Dead; k++)
            {
                s.Y -= ShotSpeed * dt / 4;
                if (s.Y < Field.Top) s.Dead = true;
                else if (Phase == Phase.Play && ShotHits(s)) s.Dead = true;
            }
        }
        Shots.RemoveAll(s => s.Dead);
    }

    /// <summary>Whether a shot hits something: the mothership's hull, band, shell or Warden, or a bird (or a bird's wing).</summary>
    private bool ShotHits(Shot s)
    {
        if (Nest is { WardenAlive: true } n && Math.Abs(s.X - n.X) <= Mothership.ShellHalfWidth)
        {
            double ly = s.Y - n.Y;
            if (ly <= Mothership.HullBottom && ly > -20)
            {
                int col = n.HullAt(s.X);
                if (col >= 0)
                    for (int r = Mothership.HullRows - 1; r >= 0; r--)
                        if (n.Hull[r, col] && ly <= Mothership.HullTop + (r + 1) * Mothership.RowH)
                        {
                            n.Hull[r, col] = false;
                            Spark(s.X, n.Y + Mothership.HullTop + (r + 0.5) * Mothership.RowH, 0xFFFF9A3A);
                            Play(Sfx.HullChip);
                            return true;
                        }
                if (ly <= Mothership.HullTop)
                {
                    int b = n.BandAt(s.X);
                    if (b >= 0 && n.Band[b])
                    {
                        n.Band[b] = false;
                        Spark(s.X, n.Y + Mothership.BandTop + 5, 0xFF6AE0FF);
                        Play(Sfx.BandChip);
                        return true;
                    }
                }
                if (ly <= Mothership.ShellBottom)
                {
                    if (Math.Abs(s.X - n.X) >= Mothership.Window) return true;
                    if (ly <= Mothership.WardenY + Mothership.WardenRadius) { KillWarden(); return true; }
                }
            }
        }
        foreach (var b in Birds)
        {
            if (b.Dead || b.State == BirdState.Hatching) continue;
            double dx = s.X - b.X, dy = s.Y - b.Y;
            if (b.Kind == BirdKind.Flitter)
            {
                if (dx * dx + dy * dy < 15 * 15) { KillBird(b); return true; }
                continue;
            }
            if (Math.Abs(dx) < Bird.BodyRadius && Math.Abs(dy) < 15) { KillBird(b); return true; }
            if (Math.Abs(dy) < 12)
            {
                int side = dx < 0 ? 0 : 1;
                int seg = (int)((Math.Abs(dx) - Bird.BodyRadius) / Bird.Segment);
                if (seg >= 0 && seg < b.Wings[side])
                {
                    // The wing is shot through there, and loses everything beyond.
                    b.Wings[side] = seg;
                    b.RegrowIn = Regrow(b);
                    AddScore(WingPoints);
                    Effects.Add(new Effect { Kind = EffectKind.Feathers, X = s.X, Y = s.Y, Colour = EmberColour(b), Life = 0.6 });
                    Play(Sfx.WingChip);
                    return true;
                }
            }
        }
        return false;
    }

    public static uint EmberColour(Bird b) => b.Variant == 0 ? 0xFFFF7A2A : 0xFFB070FF;

    private void Spark(double x, double y, uint colour) =>
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = x, Y = y, Colour = colour, Life = 0.3, Size = 0.35 });

    private void KillBird(Bird b)
    {
        b.Dead = true;
        int points = Points(b);
        AddScore(points);
        uint colour = b.Kind == BirdKind.Flitter ? (b.Variant == 0 ? 0xFFFF6AD0 : 0xFF6AE0FF) : EmberColour(b);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = b.X, Y = b.Y, Colour = colour, Size = b.Kind == BirdKind.Flitter ? 0.8 : 1.4 });
        Effects.Add(new Effect { Kind = EffectKind.Score, X = b.X, Y = b.Y, Text = points.ToString(), Colour = points >= 400 ? 0xFFFFE14A : 0xFFFFFFFF, Life = 0.9 });
        Play(b.Kind == BirdKind.Flitter ? Sfx.FlitterHit : Sfx.EmberHit);
    }

    private void KillWarden()
    {
        var n = Nest!;
        n.WardenAlive = false;
        int points = WardenPoints(WaveTime);
        AddScore(points);
        foreach (var b in Birds.Where(b => !b.Dead))
        {
            b.Dead = true;
            Effects.Add(new Effect { Kind = EffectKind.Burst, X = b.X, Y = b.Y, Colour = 0xFF6AE0FF, Size = 0.8 });
        }
        Effects.Add(new Effect { Kind = EffectKind.Blast, X = n.X, Y = n.Y + 50, Life = NestDownSeconds, Size = 2.5 });
        Bullets.Clear();
        SetPhase(Phase.Clear);
        Music(false);
        Say("THE MOTHERSHIP IS DOWN", $"THE WARDEN - {points} POINTS", NestDownSeconds);
        Play(Sfx.WardenHit);
    }

    // ───────────────────────────── the birds ─────────────────────────────

    private double Regrow(Bird b) => (1.6 - 0.45 * b.Variant) / Pace;

    private double SwoopSpeed(Bird b) => (b.Kind == BirdKind.Flitter ? 230 : 190) * Pace * (b.Variant == 1 ? 1.15 : 1);

    private void MoveBirds(double dt, bool active)
    {
        if (Nest != null)
            foreach (var b in Birds) b.HomeY = Nest.Y + 160;
        if (active && !Calm)
        {
            _breakIn -= dt;
            if (_breakIn <= 0)
            {
                var kind = Birds.FirstOrDefault(b => !b.Dead)?.Kind ?? BirdKind.Flitter;
                int variant = Birds.FirstOrDefault(b => !b.Dead)?.Variant ?? 0;
                int maxSwoop = kind == BirdKind.Flitter ? 2 + variant + Cycle : 1 + variant + Cycle / 2;
                var ready = Birds.Where(b => !b.Dead && b.State == BirdState.Holding && b.T > 0.5).ToList();
                if (ready.Count > 0 && Birds.Count(b => !b.Dead && b.State == BirdState.Swooping) < maxSwoop)
                    StartSwoop(ready[_rng.Next(ready.Count)]);
                double every = kind == BirdKind.Flitter ? 2.0 - 0.5 * variant : 3.2 - 0.8 * variant;
                if (Nest != null) every = 2.4;
                _breakIn = every / Pace * (0.6 + 0.8 * _rng.NextDouble());
            }
        }
        foreach (var b in Birds)
        {
            if (b.Dead) continue;
            b.T += dt;
            switch (b.State)
            {
                case BirdState.Hatching:
                    if (b.T >= HatchSeconds)
                    {
                        b.State = BirdState.Holding;
                        b.T = 0;
                        Play(Sfx.Hatch);
                    }
                    break;
                case BirdState.Holding:
                    Hold(b, dt, active);
                    if (active && !Calm)
                    {
                        b.FireIn -= dt;
                        if (b.FireIn <= 0)
                        {
                            if (Math.Abs(b.X - Ship.X) < 140 && _rng.NextDouble() < 0.6) Fire(b.X, b.Y + 12);
                            b.FireIn = (b.Kind == BirdKind.Flitter ? 2.2 : 1.8) / Pace * (0.5 + _rng.NextDouble());
                        }
                    }
                    break;
                case BirdState.Swooping:
                {
                    double speed = SwoopSpeed(b);
                    // The swoop follows the ship loosely, weaving as it comes.
                    b.TargetX += Math.Clamp(Ship.X - b.TargetX, -120 * dt, 120 * dt);
                    double weave = b.Kind == BirdKind.Flitter ? 120 : 60;
                    b.Vx = Math.Clamp((b.TargetX - b.X) * 2.2, -220, 220) + Math.Sin(b.T * 5 + b.Phase) * weave;
                    b.Vy = speed;
                    b.X = Math.Clamp(b.X + b.Vx * dt, Field.Left + 14, Field.Right - 14);
                    b.Y += b.Vy * dt;
                    if (active && !Calm)
                    {
                        b.FireIn -= dt;
                        if (b.FireIn <= 0 && b.Y < Field.ShipY - 150)
                        {
                            Fire(b.X, b.Y + 12);
                            b.FireIn = (0.7 + 0.7 * _rng.NextDouble()) / Pace;
                        }
                    }
                    // Off the bottom: round again from the top, back to its place.
                    if (b.Y > Field.Bottom + 30)
                    {
                        b.Y = Field.Top - 30;
                        b.State = BirdState.Returning;
                        b.T = 0;
                    }
                    break;
                }
                case BirdState.Returning:
                {
                    double speed = SwoopSpeed(b) * 0.9;
                    double tx = b.HomeX + Sway, ty = b.HomeY;
                    double dx = tx - b.X, dy = ty - b.Y, d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < speed * dt + 1)
                    {
                        (b.X, b.Y) = (tx, ty);
                        b.State = BirdState.Holding;
                        b.T = 0;
                    }
                    else
                    {
                        (b.Vx, b.Vy) = (dx / d * speed, dy / d * speed);
                        b.X += b.Vx * dt;
                        b.Y += b.Vy * dt;
                    }
                    break;
                }
            }
            if (b.Kind == BirdKind.Emberwing && b.State != BirdState.Hatching && (b.Wings[0] < Bird.WingSegments || b.Wings[1] < Bird.WingSegments))
            {
                b.RegrowIn -= dt;
                if (b.RegrowIn <= 0)
                {
                    int side = b.Wings[0] <= b.Wings[1] ? 0 : 1;
                    b.Wings[side]++;
                    b.RegrowIn = Regrow(b);
                }
            }
        }
    }

    /// <summary>A bird in its place: Flitters flutter and bob; Emberwings wheel about it, coming slowly lower.</summary>
    private void Hold(Bird b, double dt, bool active)
    {
        if (b.Kind == BirdKind.Flitter)
        {
            b.X = b.HomeX + Sway + Math.Sin(b.T * 1.7 + b.Phase) * 8;
            b.Y = b.HomeY + Math.Sin(b.T * 3.1 + b.Phase) * 5;
            return;
        }
        if (active && !Calm) b.HomeY = Math.Min(EmberLowest, b.HomeY + 2.5 * Pace * dt);
        b.X = b.HomeX + Sway * 0.5 + Math.Sin(b.T * 0.9 + b.Phase) * 55;
        b.Y = b.HomeY + Math.Sin(b.T * 1.8 + b.Phase) * 22;
    }

    private void StartSwoop(Bird b)
    {
        b.State = BirdState.Swooping;
        b.T = 0;
        b.TargetX = Ship.X;
        b.FireIn = 0.3 + _rng.NextDouble() * 0.5;
        Play(Sfx.Swoop);
    }

    /// <summary>A bullet dropped towards the ship, leaning a little its way.</summary>
    private void Fire(double x, double y)
    {
        if (Bullets.Count >= MaxBullets || !Ship.Alive) return;
        double vy = 230 * Pace;
        double t = Math.Max(0.3, (Field.ShipY - y) / vy);
        double vx = Math.Clamp((Ship.X - x) / t, -70, 70) * 0.7;
        Bullets.Add(new Bullet { X = x, Y = y, Vx = vx, Vy = vy });
        Play(Sfx.BirdFire);
    }

    private void MoveNest(double dt, bool active)
    {
        if (Nest is not { } n) return;
        n.Age += dt;
        n.BandShift = (n.BandShift + 36 * Pace * dt) % n.BandWidth;
        if (!active || Calm || !n.WardenAlive) return;
        // It drifts slowly from side to side as it comes down, so the hole under the Warden has to be followed.
        n.Sway += dt;
        n.X = Field.CentreX + Math.Sin(n.Sway * 0.35) * 60;
        n.Y = Math.Min(NestLowest, n.Y + 2.2 * Pace * dt);
        n.FireIn -= dt;
        if (n.FireIn <= 0)
        {
            double x = n.X + Mothership.HullLeft + _rng.NextDouble() * Mothership.HullCols * Mothership.CellW;
            Fire(x, n.Y + Mothership.HullBottom + 4);
            n.FireIn = 1.2 / Pace * (0.6 + 0.8 * _rng.NextDouble());
        }
    }

    private void MoveBullets(double dt)
    {
        foreach (var b in Bullets)
        {
            b.X += b.Vx * dt;
            b.Y += b.Vy * dt;
            if (b.Y > Field.Bottom + 10) b.Dead = true;
        }
        Bullets.RemoveAll(b => b.Dead);
    }

    // ───────────────────────────── meetings, and the wave's progress ─────────────────────────────

    private void Hits()
    {
        var s = Ship;
        if (!s.Alive) return;
        foreach (var b in Bullets)
        {
            if (Math.Abs(b.X - s.X) >= 18 || Math.Abs(b.Y - Field.ShipY) >= 16) continue;
            b.Dead = true;
            if (!s.Shielded) { Die("SHOT"); return; }
        }
        foreach (var b in Birds)
        {
            if (b.Dead || b.State == BirdState.Hatching) continue;
            double dx = Math.Abs(b.X - s.X), dy = Math.Abs(b.Y - Field.ShipY);
            bool touching = b.Kind == BirdKind.Flitter
                ? dx < 26 && dy < 22
                : (dx < Bird.BodyRadius + 16 && dy < 24) || (dy < 14 && dx < Bird.BodyRadius + b.Wings[b.X > s.X ? 0 : 1] * Bird.Segment + 12);
            if (!touching) continue;
            if (s.Shielded) KillBird(b);
            else { Die("RAMMED"); return; }
        }
    }

    private void Die(string cause)
    {
        Cause = cause;
        Ship.Alive = false;
        Lives--;
        Shots.Clear();
        Effects.Add(new Effect { Kind = EffectKind.Blast, X = Ship.X, Y = Field.ShipY, Life = 1.4, Size = 1 });
        SetPhase(Phase.Dying);
        Music(false);
        Play(Sfx.Death);
    }

    /// <summary>The next ship, in the middle, with the birds called back to their places.</summary>
    private void Respawn()
    {
        ResetShip();
        Bullets.Clear();
        Shots.Clear();
        foreach (var b in Birds.Where(b => !b.Dead && b.State == BirdState.Swooping))
        {
            b.State = BirdState.Returning;
            b.T = 0;
        }
        SetPhase(Phase.Start);
        _ready = true;
        Say("READY", null, ReadySeconds);
    }

    private void Progress()
    {
        Birds.RemoveAll(b => b.Dead);
        if (Nest == null && Birds.Count == 0 && !Calm)
        {
            SetPhase(Phase.Clear);
            Bullets.Clear();
            Say("WAVE CLEARED", null, ClearSeconds);
            Play(Sfx.Clear);
        }
    }

    private void AddScore(int points)
    {
        Score += points;
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into the play of a wave (the Emberwings still in their eggs).</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        BeginWave(wave);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    internal void ClearBirds() => Birds.Clear();

    internal Bird PutBird(BirdKind kind, double x, double y, BirdState state = BirdState.Holding, int variant = 0)
    {
        var b = AddBird(kind, variant, x, y);
        b.State = state;
        b.TargetX = x;
        return b;
    }

    internal Bullet PutBullet(double x, double y, double vx = 0, double vy = 230)
    {
        var b = new Bullet { X = x, Y = y, Vx = vx, Vy = vy };
        Bullets.Add(b);
        return b;
    }

    internal Shot PutShot(double x, double y)
    {
        var s = new Shot { X = x, Y = y };
        Shots.Add(s);
        return s;
    }

    internal void PutShip(double x)
    {
        ResetShip();
        Ship.X = x;
    }

    internal void SetScore(int s) => Score = s;
    internal void SetLives(int n) => Lives = n;
    internal void SetWaveTime(double t) => WaveTime = t;
}
