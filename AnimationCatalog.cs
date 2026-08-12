namespace GuguPet;

public enum SpriteSheetKind { Main, IdleActions }

public readonly record struct SpriteFrame(
    int Row,
    int Column,
    int DurationMs,
    SpriteSheetKind Sheet = SpriteSheetKind.Main);

public sealed record AnimationSequence(IReadOnlyList<SpriteFrame> Frames, int LoopStartIndex);

public static class AnimationCatalog
{
    public const int CellWidth = 192;
    public const int CellHeight = 208;
    public const int SheetColumns = 8;
    public const int SheetRows = 11;

    public static readonly string[] StateNames =
    {
        "idle", "running-right", "running-left", "waving", "jumping",
        "failed", "waiting", "running", "review"
    };

    public static readonly string[] IdleActionNames =
    {
        "guitar", "cookie", "sleep-side", "sleep-prone", "sleep-supine",
        "needs-input", "drink", "stretch", "sit-think", "head-pat", "belly-poke",
        "celebrate-cheer", "celebrate-clap", "celebrate-dance"
    };

    private static readonly Dictionary<string, SpriteFrame[]> StateFrames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["idle"] = new SpriteFrame[]
        {
            new(0, 0, 280), new(0, 1, 110), new(0, 2, 110),
            new(0, 3, 140), new(0, 4, 140), new(0, 5, 320)
        },
        ["running-right"] = Row(1, 8, 120, 120),
        ["running-left"] = Row(2, 8, 120, 120),
        ["waving"] = Row(3, 4, 140, 280),
        ["jumping"] = Row(4, 5, 140, 280),
        ["failed"] = Row(5, 8, 140, 240),
        ["waiting"] = Row(6, 6, 150, 260),
        ["running"] = Row(7, 6, 120, 220),
        ["review"] = Row(8, 6, 150, 280),
        ["guitar"] = IdleActionRow(0, 155, 240),
        ["cookie"] = IdleActionRow(1, 145, 220),
        ["sleep-side"] = IdleActionRow(2, 260, 420),
        ["sleep-prone"] = IdleActionRow(3, 260, 420),
        ["sleep-supine"] = IdleActionRow(4, 260, 420),
        ["thinking-star"] = IdleActionRow(5, 150, 180),
        ["thinking-spiral"] = IdleActionRow(6, 145, 175),
        ["thinking-chin"] = IdleActionRow(7, 170, 210),
        ["needs-input"] = IdleActionRow(8, 155, 240),
        ["drink"] = IdleActionRow(9, 170, 260),
        ["stretch"] = IdleActionRow(10, 165, 250),
        ["sit-think"] = IdleActionRow(11, 180, 260),
        ["head-pat"] = IdleActionRow(12, 150, 230),
        ["belly-poke"] = IdleActionRow(13, 145, 230),
        ["celebrate-cheer"] = IdleActionRow(14, 135, 260),
        ["celebrate-clap"] = IdleActionRow(15, 115, 180),
        ["celebrate-dance"] = IdleActionRow(16, 140, 220)
    };

    public static bool IsValidState(string? state) =>
        state is not null && StateFrames.ContainsKey(state);

    public static bool IsIdleAction(string? state) =>
        state is not null && IdleActionNames.Contains(state, StringComparer.OrdinalIgnoreCase);

    public static AnimationSequence GetSequence(string state, bool reducedMotion)
    {
        if (!StateFrames.TryGetValue(state, out var frames))
            frames = StateFrames["idle"];

        if (reducedMotion)
            return new AnimationSequence(new[] { frames[0] }, 0);

        if (state.Equals("idle", StringComparison.OrdinalIgnoreCase))
            return new AnimationSequence(frames, 0);

        // Codex plays a non-idle action three times, then settles into its idle loop.
        var sequence = new List<SpriteFrame>(frames.Length * 3 + StateFrames["idle"].Length);
        sequence.AddRange(frames);
        sequence.AddRange(frames);
        sequence.AddRange(frames);
        var loopStart = sequence.Count;
        sequence.AddRange(StateFrames["idle"]);
        return new AnimationSequence(sequence, loopStart);
    }

    public static AnimationSequence GetLoopingSequence(string state, bool reducedMotion)
    {
        if (!StateFrames.TryGetValue(state, out var frames))
            frames = StateFrames["idle"];
        if (reducedMotion)
            return new AnimationSequence(new[] { frames[0] }, 0);
        return new AnimationSequence(frames, 0);
    }

    public static SpriteFrame NeutralFrame => new(0, 6, 0);

    public static SpriteFrame LookFrame(double cursorX, double cursorY, double centerX, double centerY)
    {
        var dx = cursorX - centerX;
        var dy = cursorY - centerY;
        if (Math.Sqrt(dx * dx + dy * dy) <= 1)
            return NeutralFrame;

        var degrees = (Math.Atan2(dx, -dy) * (180.0 / Math.PI) + 360.0) % 360.0;
        var direction = ((int)Math.Round(degrees / 22.5)) % 16;
        return new SpriteFrame(9 + direction / 8, direction % 8, 0);
    }

    private static SpriteFrame[] Row(int row, int count, int duration, int finalDuration) =>
        Enumerable.Range(0, count)
            .Select(column => new SpriteFrame(row, column, column == count - 1 ? finalDuration : duration))
            .ToArray();

    private static SpriteFrame[] IdleActionRow(int row, int duration, int finalDuration) =>
        Enumerable.Range(0, 8)
            .Select(column => new SpriteFrame(
                row,
                column,
                column == 7 ? finalDuration : duration,
                SpriteSheetKind.IdleActions))
            .ToArray();
}
