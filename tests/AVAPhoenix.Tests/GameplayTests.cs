using AVAPhoenix.Engine;

namespace AVAPhoenix.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaphoenix-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A game in play in a wave with no birds unless a test puts them there, and nothing firing or swooping on its own.</summary>
    private static GameEngine Game(int wave = 1, bool keepBirds = false)
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(wave);
        if (!keepBirds) g.ClearBirds();
        return g;
    }

    /// <summary>A real game in play, with plenty of ships.</summary>
    private static GameEngine Real(int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(wave);
        g.SetLives(99);
        return g;
    }

    private static readonly Controls FireButton = new(Fire: true);
    private static readonly Controls ShieldButton = new(Shield: true);

    // ───────────────────────────── the waves ─────────────────────────────

    [Fact]
    public void The_waves_go_flitters_emberwings_and_the_mothership_then_round_again_faster()
    {
        var kinds = new List<string>();
        for (int w = 1; w <= 6; w++)
        {
            var g = Game(w, keepBirds: true);
            kinds.Add(g.Nest != null ? "M" : g.Birds[0].Kind == BirdKind.Flitter ? $"F{g.Birds[0].Variant}" : $"E{g.Birds[0].Variant}");
        }
        Assert.Equal(["F0", "F1", "E0", "E1", "M", "F0"], kinds);
        Assert.Equal("THE MOTHERSHIP", GameEngine.NameOf(5));
        Assert.True(Game(6).Pace > Game(1).Pace);
        Assert.Equal(16, Game(1, keepBirds: true).Birds.Count);
        Assert.Equal(8, Game(3, keepBirds: true).Birds.Count);
    }

    // ───────────────────────────── the ship ─────────────────────────────

    [Fact]
    public void The_ship_flies_left_and_right_and_no_further_than_the_edges()
    {
        var g = Game();
        g.Run(30, new Controls(Dx: 1));
        Assert.Equal(Field.CentreX + GameEngine.ShipSpeed * 0.5, g.Ship.X, 1);
        g.Run(300, new Controls(Dx: -1));
        Assert.Equal(Field.Left + 24, g.Ship.X, 1);
    }

    [Fact]
    public void Shots_fly_up_two_at_a_time_and_holding_the_button_keeps_firing()
    {
        var g = Game();
        g.Run(1, FireButton);
        Assert.Single(g.Shots);
        g.Run(60, FireButton);
        Assert.Equal(GameEngine.MaxShots, g.Shots.Count);
        g.Idle(60);
        Assert.Empty(g.Shots);
    }

    // ───────────────────────────── the birds ─────────────────────────────

    [Fact]
    public void A_flitter_is_worth_20_in_its_place_and_80_diving()
    {
        var g = Game();
        var b = g.PutBird(BirdKind.Flitter, Field.CentreX, 600);
        g.Run(1, FireButton);
        g.Idle(20);
        Assert.True(b.Dead);
        Assert.Equal(20, g.Score);
        var d = g.PutBird(BirdKind.Flitter, Field.CentreX, 520, BirdState.Swooping);
        g.Run(1, FireButton);
        g.Idle(15);
        Assert.True(d.Dead);
        Assert.Equal(100, g.Score);
    }

    [Fact]
    public void Shooting_an_emberwing_s_wing_cuts_it_back_it_grows_again_and_only_the_body_kills_it()
    {
        var g = Game();
        var b = g.PutBird(BirdKind.Emberwing, Field.CentreX, 500);
        g.Idle(2);
        g.PutShot(b.X + 30, b.Y + 8);
        g.Idle(1);
        Assert.Equal([Bird.WingSegments, 1], b.Wings);
        Assert.False(b.Dead);
        Assert.Equal(GameEngine.WingPoints, g.Score);
        g.Idle(120);
        Assert.True(b.Wings[1] >= 2);
        g.PutShot(b.X, b.Y + 8);
        g.Idle(1);
        Assert.True(b.Dead);
        Assert.Equal(GameEngine.WingPoints + 100, g.Score);
    }

    [Fact]
    public void Emberwings_hatch_from_eggs_that_shots_pass_through()
    {
        var g = Game(3, keepBirds: true);
        Assert.All(g.Birds, b => Assert.Equal(BirdState.Hatching, b.State));
        var b = g.Birds[0];
        g.PutShot(b.X, b.Y + 8);
        g.Idle(3);
        Assert.False(b.Dead);
        g.Idle((int)(60 * (GameEngine.HatchSeconds + 1.5)));
        Assert.All(g.Birds, b => Assert.Equal(BirdState.Holding, b.State));
    }

    [Fact]
    public void Birds_swoop_at_the_ship_and_fire_at_it()
    {
        var g = Real();
        bool swooped = false, fired = false;
        for (int t = 0; t < 60 * 20; t++)
        {
            g.Tick(default);
            swooped |= g.Birds.Any(b => b.State == BirdState.Swooping);
            fired |= g.Bullets.Count > 0;
        }
        Assert.True(swooped);
        Assert.True(fired);
    }

    [Fact]
    public void A_bird_that_dives_off_the_bottom_comes_round_from_the_top_to_its_place()
    {
        var g = Game();
        g.PutShip(Field.Right - 30);
        var b = g.PutBird(BirdKind.Flitter, 300, 600, BirdState.Swooping);
        b.HomeY = 150;
        bool returning = false;
        for (int t = 0; t < 60 * 6; t++)
        {
            g.Tick(default);
            returning |= b.State == BirdState.Returning;
        }
        Assert.True(returning);
        Assert.Equal(BirdState.Holding, b.State);
        Assert.InRange(b.Y, 140, 160);
        Assert.True(g.Ship.Alive);
    }

    // ───────────────────────────── the shield, and losing ships ─────────────────────────────

    [Fact]
    public void The_shield_holds_the_ship_still_stops_bullets_and_destroys_birds_then_must_recharge()
    {
        var g = Game();
        double x = g.Ship.X;
        g.Run(1, ShieldButton);
        Assert.True(g.Ship.Shielded);
        g.Run(20, new Controls(Dx: 1));
        Assert.Equal(x, g.Ship.X);
        g.PutBullet(x, Field.ShipY - 40);
        var b = g.PutBird(BirdKind.Flitter, x, Field.ShipY - 4, BirdState.Swooping);
        g.Idle(15);
        Assert.True(g.Ship.Alive);
        Assert.Empty(g.Bullets);
        Assert.True(b.Dead);
        Assert.Equal(80, g.Score);
        g.Idle((int)(60 * GameEngine.ShieldSeconds));
        Assert.False(g.Ship.Shielded);
        g.Run(1, ShieldButton);
        Assert.False(g.Ship.Shielded);
        g.Idle((int)(60 * GameEngine.ShieldRecharge) + 5);
        g.Run(1, ShieldButton);
        Assert.True(g.Ship.Shielded);
    }

    [Fact]
    public void A_bullet_or_a_bird_ends_the_ship_and_the_next_starts_in_the_middle()
    {
        var g = Game();
        g.SetLives(3);
        g.Run(20, new Controls(Dx: -1));
        g.PutBullet(g.Ship.X, Field.ShipY - 30);
        g.Idle(20);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("SHOT", g.Cause);
        Assert.Equal(2, g.Lives);
        g.Idle((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(Phase.Start, g.Phase);
        Assert.True(g.Ship.Alive);
        Assert.Equal(Field.CentreX, g.Ship.X);
        g.Idle((int)(60 * GameEngine.ReadySeconds) + 5);
        g.PutBird(BirdKind.Emberwing, g.Ship.X, Field.ShipY - 6, BirdState.Swooping);
        g.Idle(2);
        Assert.Equal("RAMMED", g.Cause);
    }

    [Fact]
    public void Losing_the_last_ship_ends_the_game()
    {
        var g = Game();
        g.SetLives(1);
        g.PutBullet(g.Ship.X, Field.ShipY - 30);
        g.Idle((int)(60 * GameEngine.DyingSeconds) + 20);
        Assert.Equal(GameState.GameOver, g.State);
    }

    // ───────────────────────────── the mothership ─────────────────────────────

    [Fact]
    public void Shots_chip_the_hull_from_below_then_the_band_and_the_shell_stops_the_rest()
    {
        var g = Game(5);
        var n = g.Nest!;
        double x = n.X - 100;
        int col = n.HullAt(x);
        g.PutShip(x);
        for (int k = 1; k <= Mothership.HullRows; k++)
        {
            g.Run(1, FireButton);
            g.Idle(60);
            Assert.False(n.Hull[Mothership.HullRows - k, col]);
            Assert.Equal(Mothership.HullRows * Mothership.HullCols - k, n.Hull.Cast<bool>().Count(h => h));
        }
        g.Run(1, FireButton);
        g.Idle(60);
        Assert.Equal(Mothership.BandCells - 1, n.Band.Count(b => b));
        Array.Fill(n.Band, false);
        g.Run(1, FireButton);
        g.Idle(60);
        Assert.True(n.WardenAlive);
        Assert.Empty(g.Shots);
        Assert.Equal(0, g.Score);
    }

    [Fact]
    public void The_band_turns()
    {
        var g = Game(5);
        double shift = g.Nest!.BandShift;
        g.Idle(30);
        Assert.NotEqual(shift, g.Nest.BandShift);
    }

    [Fact]
    public void A_shot_through_the_hole_brings_down_the_warden_and_the_mothership()
    {
        var g = Game(5);
        var n = g.Nest!;
        for (int r = 0; r < Mothership.HullRows; r++) n.Hull[r, n.HullAt(n.X)] = false;
        Array.Fill(n.Band, false);
        g.Run(1, FireButton);
        g.Idle(60);
        Assert.False(n.WardenAlive);
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.Equal(GameEngine.WardenPoints(0), g.Score);
        g.Idle((int)(60 * GameEngine.NestDownSeconds) + 5);
        Assert.Equal(6, g.Wave);
    }

    [Fact]
    public void The_warden_is_worth_more_the_quicker_it_falls()
    {
        Assert.Equal(9000, GameEngine.WardenPoints(5));
        Assert.Equal(7000, GameEngine.WardenPoints(25));
        Assert.Equal(2000, GameEngine.WardenPoints(200));
    }

    // ───────────────────────────── waves, points and lives ─────────────────────────────

    [Fact]
    public void Clearing_a_wave_brings_the_next()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        g.ClearBirds();
        g.PutBird(BirdKind.Flitter, Field.CentreX, 640);
        g.Run(1, FireButton);
        g.Idle(10);
        Assert.Equal(Phase.Clear, g.Phase);
        g.Idle((int)(60 * GameEngine.ClearSeconds) + 5);
        Assert.Equal(2, g.Wave);
    }

    [Fact]
    public void Extra_ships_come_at_20000_and_every_60000_after()
    {
        var g = Game();
        int lives = g.Lives;
        g.SetScore(GameEngine.FirstExtraLife - 10);
        g.PutBird(BirdKind.Flitter, Field.CentreX, 640);
        g.Run(1, FireButton);
        g.Idle(10);
        Assert.Equal(lives + 1, g.Lives);
    }

    // ───────────────────────────── around the game ─────────────────────────────

    [Fact]
    public void The_computer_pilot_clears_the_first_waves()
    {
        var g = new GameEngine(1, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        g.SetLives(9);
        for (int t = 0; t < 60 * 120 && g.State == GameState.Playing && g.Wave < 4; t++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(GameState.Playing, g.State);
        Assert.True(g.Wave >= 4, $"wave {g.Wave}");
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_then_back()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 25);
        Assert.True(g.Score > 0);
        g.Idle((int)(60 * (GameEngine.AttractSeconds - 25)) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        static (int, double, int) Play(int seed)
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            for (int t = 0; t < 60 * 40; t++) g.Tick(g.Pilot.Decide(g));
            return (g.Score, g.Ship.X, g.Wave);
        }
        Assert.Equal(Play(12), Play(12));
    }

    [Fact]
    public void A_high_score_takes_initials_and_is_kept_in_the_table_file()
    {
        string folder = TempFolder();
        var g = new GameEngine(7, HighScoreTable.Load(folder)) { Silent = true, Calm = true };
        g.Go(3);
        g.ClearBirds();
        g.SetScore(99999);
        g.SetLives(1);
        g.PutBullet(g.Ship.X, Field.ShipY - 30);
        g.Idle((int)(60 * (GameEngine.DyingSeconds + 5)));
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "PHX") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(2);
        Assert.Equal(GameState.HighScores, g.State);
        var again = HighScoreTable.Load(folder);
        Assert.Equal(("PHX", 99999, 3), (again.Entries[0].Initials, again.Entries[0].Score, again.Entries[0].Stage));
        Directory.Delete(folder, true);
    }
}
