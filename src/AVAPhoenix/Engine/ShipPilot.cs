namespace AVAPhoenix.Engine;

/// <summary>
/// The computer pilot, for the demonstration (and the tests). It looks a moment ahead for bullets and
/// diving birds coming its way and slips aside from them, raising the shield when there's nowhere to go.
/// Otherwise it lines up under the best target (a diving bird first, then the lowest and nearest, allowing
/// for where it will be when the shot gets there; on the mothership, the Warden) and holds the trigger down.
/// </summary>
public sealed class ShipPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    private int _tick;

    /// <summary>How soon something will hit the ship if it sits at <paramref name="x"/> (a large number for nothing soon).</summary>
    private static double Danger(GameEngine g, double x)
    {
        double soonest = 99;
        foreach (var b in g.Bullets)
        {
            if (b.Vy <= 0) continue;
            double t = (Field.ShipY - b.Y) / b.Vy;
            if (t < -0.06 || t > 1.3) continue;
            if (Math.Abs(b.X + b.Vx * Math.Max(0, t) - x) < 26) soonest = Math.Min(soonest, Math.Max(0, t));
        }
        foreach (var b in g.Birds)
        {
            if (b.Dead || b.State is BirdState.Hatching or BirdState.Holding) continue;
            double reach = b.Kind == BirdKind.Flitter ? 40 : Bird.BodyRadius + Math.Max(b.Wings[0], b.Wings[1]) * Bird.Segment + 26;
            for (double t = 0; t <= 0.8; t += 0.1)
            {
                double bx = b.X + b.Vx * t, by = b.Y + b.Vy * t;
                if (Math.Abs(by - Field.ShipY) < 34 && Math.Abs(bx - x) < reach) { soonest = Math.Min(soonest, t); break; }
            }
        }
        return soonest;
    }

    public Controls Decide(GameEngine g)
    {
        _tick++;
        var s = g.Ship;
        if (!s.Alive || g.Phase is not (Phase.Play or Phase.Start)) return default;
        bool shieldReady = s.ShieldTime <= 0 && s.ShieldCool <= 0;
        double now = Danger(g, s.X);
        if (now < 0.7)
        {
            // Somewhere safe it can get to in time, as near as possible.
            double best = double.MaxValue, goal = s.X;
            for (double dx = -240; dx <= 240; dx += 12)
            {
                double x = s.X + dx;
                if (x < Field.Left + 24 || x > Field.Right - 24) continue;
                double time = Math.Abs(dx) / GameEngine.ShipSpeed;
                if (time > now - 0.04 && dx != 0) continue;
                if (Danger(g, x) < 1.0) continue;
                // Nothing on the way there either.
                bool clear = true;
                for (double k = 12; k < Math.Abs(dx) && clear; k += 12)
                    if (Danger(g, s.X + Math.Sign(dx) * k) < k / GameEngine.ShipSpeed + 0.05) clear = false;
                if (clear && Math.Abs(dx) < best) { best = Math.Abs(dx); goal = x; }
            }
            if (best < double.MaxValue && Math.Abs(goal - s.X) > 2)
            {
                Doing = "dodge";
                return new Controls(Math.Sign(goal - s.X), 0, Fire: true);
            }
            if (now < 0.3 && shieldReady)
            {
                Doing = "shield";
                return new Controls(Shield: true);
            }
            if (s.Shielded) { Doing = "shielded"; return new Controls(Fire: false); }
        }

        double aim;
        if (g.Nest is { WardenAlive: true } n && !g.Birds.Any(b => !b.Dead && b.State == BirdState.Swooping && b.Y > 420))
        {
            // Bore a hole under the Warden, a little to either side so it's wide enough.
            aim = n.X + ((_tick / 30) % 3 - 1) * 7;
            Doing = "nest";
        }
        else
        {
            Bird? target = null;
            double cost = double.MaxValue;
            aim = s.X;
            foreach (var b in g.Birds)
            {
                if (b.Dead || b.State == BirdState.Hatching || b.Y > Field.ShipY - 30) continue;
                double tShot = Math.Max(0, (Field.ShipY - 22 - b.Y) / GameEngine.ShotSpeed);
                double px = b.X + (b.State == BirdState.Holding ? 0 : b.Vx * tShot);
                double c = Math.Abs(px - s.X) / GameEngine.ShipSpeed - (b.State == BirdState.Swooping ? 0.6 : 0) - b.Y / 1500;
                if (c < cost) { cost = c; target = b; aim = px; }
            }
            Doing = target == null ? "wait" : target.State == BirdState.Swooping ? "diver" : "target";
        }
        aim = Math.Clamp(aim, Field.Left + 24, Field.Right - 24);
        int dir = Math.Abs(aim - s.X) > 5 ? Math.Sign(aim - s.X) : 0;
        // Don't walk into trouble on the way.
        if (dir != 0 && Danger(g, s.X + dir * 24) < 0.6) dir = 0;
        bool fire = Math.Abs(aim - s.X) < 16;
        return new Controls(dir, 0, fire);
    }
}
