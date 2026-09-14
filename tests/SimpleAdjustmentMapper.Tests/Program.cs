using DisplayPresetPrototype;

static class Program
{
    private static int failures;

    private static StylePresetData Base() => new StylePresetData
    {
        Name = "base",
        Shadows = 20,
        Highlights = 10,
        Midtones = 4,
        BlackPoint = 8,
        WhitePoint = -6,
        ContrastPivot = 5,
        Temperature = -3,
        Tint = 2,
        Vibrance = 7,
        Saturation = 50,
        Exposure = 6,
        ShadowRange = 12,
        HighlightRange = -9,
        TransitionSoftness = 4
    };

    private static void Check(string name, bool condition)
    {
        if (!condition)
        {
            failures++;
            Console.Error.WriteLine("FAIL: " + name);
        }
    }

    private static void Main()
    {
        var baseline = Base();
        var neutral = SimpleAdjustmentMapper.Apply(SimpleAdjustmentState.Neutral, baseline);
        Check("neutral mapping preserves every existing field", Same(neutral, baseline));

        var bright = SimpleAdjustmentMapper.Apply(new SimpleAdjustmentState { Brightness = 100 }, baseline);
        Check("brightness drives exposure", bright.Exposure > baseline.Exposure);

        var dark = SimpleAdjustmentMapper.Apply(new SimpleAdjustmentState { DarkArea = 100 }, baseline);
        Check("dark area drives shadows", dark.Shadows > baseline.Shadows);
        Check("dark area lifts black point", dark.BlackPoint > baseline.BlackPoint);

        var clearNeutral = SimpleAdjustmentMapper.Apply(new SimpleAdjustmentState { Clarity = 50 }, baseline);
        var clear = SimpleAdjustmentMapper.Apply(new SimpleAdjustmentState { Clarity = 100 }, baseline);
        Check("clarity midpoint is neutral", Same(clearNeutral, baseline));
        Check("clarity uses existing contrast curve", clear.ContrastPivot > baseline.ContrastPivot);

        var color = SimpleAdjustmentMapper.Apply(new SimpleAdjustmentState { Color = 100 }, baseline);
        Check("color drives vibrance", color.Vibrance > baseline.Vibrance);
        Check("color drives normalized saturation", color.Saturation > baseline.Saturation);

        var depth = SimpleAdjustmentMapper.Apply(new SimpleAdjustmentState { TonalDepth = 100 }, baseline);
        Check("tonal depth expands black point", depth.BlackPoint < baseline.BlackPoint);
        Check("tonal depth expands white point", depth.WhitePoint > baseline.WhitePoint);
        Check("tonal depth drives contrast", depth.ContrastPivot > baseline.ContrastPivot);

        var warm = SimpleAdjustmentMapper.Apply(new SimpleAdjustmentState { Temperature = 100 }, baseline);
        var cool = SimpleAdjustmentMapper.Apply(new SimpleAdjustmentState { Temperature = -100 }, baseline);
        Check("positive temperature is warmer", warm.Temperature > baseline.Temperature);
        Check("negative temperature is cooler", cool.Temperature < baseline.Temperature);

        var clamped = SimpleAdjustmentMapper.Apply(new SimpleAdjustmentState
        {
            Brightness = 100,
            DarkArea = 100,
            Clarity = 100,
            Color = 100,
            TonalDepth = 100,
            Temperature = 100
        }, new StylePresetData { Name = "empty", Saturation = 50 });
        Check("all mapped fields stay in the real model ranges",
            clamped.Shadows is >= 0 and <= 200 &&
            clamped.Midtones is >= -100 and <= 100 &&
            clamped.BlackPoint is >= -100 and <= 100 &&
            clamped.WhitePoint is >= -100 and <= 100 &&
            clamped.ContrastPivot is >= -100 and <= 100 &&
            clamped.Temperature is >= -100 and <= 100 &&
            clamped.Vibrance is >= -100 and <= 100 &&
            clamped.Saturation is >= 0 and <= 100 &&
            clamped.Exposure is >= -100 and <= 100);

        var brightAndDark = SimpleAdjustmentMapper.Apply(
            new SimpleAdjustmentState { Brightness = 40, DarkArea = 60 }, baseline);
        var brightOnly = SimpleAdjustmentMapper.Apply(
            new SimpleAdjustmentState { Brightness = 40 }, baseline);
        var darkOnly = SimpleAdjustmentMapper.Apply(
            new SimpleAdjustmentState { DarkArea = 60 }, baseline);
        Check("changing brightness does not erase dark-area shadow contribution",
            brightAndDark.Shadows == darkOnly.Shadows &&
            brightAndDark.BlackPoint == darkOnly.BlackPoint &&
            brightAndDark.Exposure == brightOnly.Exposure);

        var darkAndDepth = SimpleAdjustmentMapper.Apply(
            new SimpleAdjustmentState { DarkArea = 30, TonalDepth = 30 }, baseline);
        var dark30 = SimpleAdjustmentMapper.Apply(
            new SimpleAdjustmentState { DarkArea = 30 }, baseline);
        var depth30 = SimpleAdjustmentMapper.Apply(
            new SimpleAdjustmentState { TonalDepth = 30 }, baseline);
        Check("overlapping black-point contributions are added",
            darkAndDepth.BlackPoint == dark30.BlackPoint + depth30.BlackPoint - baseline.BlackPoint);
        Check("overlapping contrast contributions are added",
            darkAndDepth.ContrastPivot == dark30.ContrastPivot + depth30.ContrastPivot - baseline.ContrastPivot);

        var composed = SimpleAdjustmentMapper.Compose(
            baseline, SimpleAdjustmentMapper.BuildContributions(
                new SimpleAdjustmentState { Brightness = 40, DarkArea = 60, Color = 20 }));
        Check("Apply and explicit composition produce the same result", Same(
            composed,
            SimpleAdjustmentMapper.Apply(
                new SimpleAdjustmentState { Brightness = 40, DarkArea = 60, Color = 20 }, baseline)));
        Check("controlled parameter ownership is explicit",
            SimpleAdjustmentMapper.ControlledParameters[SimpleAdjustmentControl.Brightness].Length == 1 &&
            Array.Exists(SimpleAdjustmentMapper.ControlledParameters[SimpleAdjustmentControl.DarkArea],
                x => x == nameof(StylePresetData.ContrastPivot)) &&
            Array.Exists(SimpleAdjustmentMapper.ControlledParameters[SimpleAdjustmentControl.Color],
                x => x == nameof(StylePresetData.Saturation)));

        if (failures != 0)
            Environment.ExitCode = 1;
        Console.WriteLine($"SimpleAdjustmentMapper tests: {(failures == 0 ? "PASS" : "FAIL")} ({failures} failures)");
    }

    private static bool Same(StylePresetData a, StylePresetData b) =>
        a.Shadows == b.Shadows && a.Highlights == b.Highlights && a.Midtones == b.Midtones &&
        a.BlackPoint == b.BlackPoint && a.WhitePoint == b.WhitePoint && a.ContrastPivot == b.ContrastPivot &&
        a.Temperature == b.Temperature && a.Tint == b.Tint && a.Vibrance == b.Vibrance &&
        a.Saturation == b.Saturation && a.Exposure == b.Exposure && a.ShadowRange == b.ShadowRange &&
        a.HighlightRange == b.HighlightRange && a.TransitionSoftness == b.TransitionSoftness;
}
