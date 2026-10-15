using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAPhoenix.Engine;
using AVAPhoenix.Views;

namespace AVAPhoenix.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set PHOENIX_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaphoenix-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("PHOENIX_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAPhoenix-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 84250, 12);
        g.HighScores.Insert("AVA", 52800, 8);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void The_flitters()
    {
        var (w, c, g) = Open();
        g.StartGame();
        g.SetLives(9);
        g.Idle(60);
        c.Advance(0.5);
        Shot(w, c, "wave-start");
        Assert.True(PlayUntil(g, c, g => g.WaveTime > 4 && g.Ship.Alive && g.Birds.Count(b => b.State == BirdState.Swooping && b.Y > 300) >= 2 && g.Shots.Count > 0 && g.Bullets.Count >= 2));
        Shot(w, c, "flitters");
    }

    [AvaloniaFact]
    public void The_emberwings()
    {
        var (w, c, g) = Open(3);
        g.Go(3);
        g.SetLives(9);
        g.Idle(50);
        c.Advance(0.3);
        Shot(w, c, "hatching");
        Assert.True(PlayUntil(g, c, g => g.WaveTime > 6 && g.Ship.Alive && g.Birds.Count(b => !b.Dead) >= 5
                                       && g.Birds.Count(b => b.Wings[0] + b.Wings[1] < 2 * Bird.WingSegments) >= 2 && g.Shots.Count > 0));
        Shot(w, c, "emberwings");
        g.Go(4);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.WaveTime > 5 && g.Ship.Alive && g.Birds.Any(b => b.State == BirdState.Swooping && b.Y > 380)));
        Shot(w, c, "diving");
    }

    [AvaloniaFact]
    public void The_shield()
    {
        var (w, c, g) = Open(4);
        g.Go(2);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.WaveTime > 4 && g.Ship.Alive && g.Bullets.Any(b => b.Y > 560)));
        g.Run(1, new AVAPhoenix.Engine.Controls(Shield: true));
        for (int t = 0; t < 20; t++) { g.Tick(default); c.Advance(1 / 60.0); }
        Shot(w, c, "shield");
    }

    [AvaloniaFact]
    public void The_mothership()
    {
        var (w, c, g) = Open(5);
        g.Go(5);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.Nest is { } n && n.Hull.Cast<bool>().Count(h => !h) >= 14 && n.Band.Count(b => !b) >= 4 && g.Ship.Alive && g.Shots.Count > 0, 120));
        Shot(w, c, "mothership");
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Clear && g.PhaseTime > 0.5, 120));
        Shot(w, c, "warden-down");
    }

    [AvaloniaFact]
    public void A_ship_lost()
    {
        var (w, c, g) = Open();
        g.Go(2);
        g.SetLives(3);
        Assert.True(PlayUntil(g, c, g => g.WaveTime > 5 && g.Ship.Alive));
        g.PutBullet(g.Ship.X, Field.ShipY - 20);
        g.Idle(30);
        c.Advance(0.4);
        Shot(w, c, "lost");
    }
}
