using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("SimpleAdjustmentMapper.Tests")]

namespace DisplayPresetPrototype
{
    /// <summary>
    /// The six user-facing controls used by the upcoming Simple Adjustment view.
    /// Values are deliberately independent from StylePresetData so the mapping
    /// can be tested without changing the existing filter or WPF pipeline.
    /// </summary>
    internal sealed class SimpleAdjustmentState
    {
        public int Brightness { get; set; }
        public int DarkArea { get; set; }
        // Clarity is a 0..100 control. 50 is the untouched/natural midpoint.
        public int Clarity { get; set; } = 50;
        public int Color { get; set; }
        public int TonalDepth { get; set; }
        public int Temperature { get; set; }

        public static SimpleAdjustmentState Neutral => new SimpleAdjustmentState();

        public SimpleAdjustmentState DeepClone() => new SimpleAdjustmentState
        {
            Brightness = Brightness,
            DarkArea = DarkArea,
            Clarity = Clarity,
            Color = Color,
            TonalDepth = TonalDepth,
            Temperature = Temperature
        };
    }

    internal enum SimpleAdjustmentControl
    {
        Brightness,
        DarkArea,
        Clarity,
        Color,
        TonalDepth,
        Temperature
    }

    /// <summary>
    /// A delta owned by one semantic control. Keeping deltas separate makes
    /// overlapping fields explicit and prevents a later control from replacing
    /// an earlier control's contribution.
    /// </summary>
    internal sealed class SimpleAdjustmentContribution
    {
        public int Shadows { get; set; }
        public int BlackPoint { get; set; }
        public int WhitePoint { get; set; }
        public int ContrastPivot { get; set; }
        public int Vibrance { get; set; }
        public int Saturation { get; set; }
        public int Exposure { get; set; }
        public int Temperature { get; set; }
    }

    /// <summary>
    /// Maps semantic controls to the existing StylePresetData model.
    /// Sharpness/IGCL is intentionally not represented here; this mapper only
    /// uses fields that the current ShadePilot filter pipeline already applies.
    /// </summary>
    internal static class SimpleAdjustmentMapper
    {
        public static StylePresetData Apply(SimpleAdjustmentState simple, StylePresetData baseState)
        {
            return Compose(baseState, BuildContributions(simple));
        }

        internal static IReadOnlyDictionary<SimpleAdjustmentControl, string[]> ControlledParameters { get; } =
            new Dictionary<SimpleAdjustmentControl, string[]>
            {
                [SimpleAdjustmentControl.Brightness] = new[] { nameof(StylePresetData.Exposure) },
                [SimpleAdjustmentControl.DarkArea] = new[] { nameof(StylePresetData.Shadows), nameof(StylePresetData.BlackPoint), nameof(StylePresetData.ContrastPivot) },
                [SimpleAdjustmentControl.Clarity] = new[] { nameof(StylePresetData.ContrastPivot) },
                [SimpleAdjustmentControl.Color] = new[] { nameof(StylePresetData.Vibrance), nameof(StylePresetData.Saturation) },
                [SimpleAdjustmentControl.TonalDepth] = new[] { nameof(StylePresetData.BlackPoint), nameof(StylePresetData.WhitePoint), nameof(StylePresetData.ContrastPivot) },
                [SimpleAdjustmentControl.Temperature] = new[] { nameof(StylePresetData.Temperature) }
            };

        internal static IReadOnlyList<SimpleAdjustmentContribution> BuildContributions(SimpleAdjustmentState simple)
        {
            var input = simple ?? SimpleAdjustmentState.Neutral;
            double brightness = SmoothSigned(input.Brightness, 100);
            double darkArea = SmoothSigned(input.DarkArea, 100);
            double clarity = SmoothSigned(input.Clarity - 50, 50);
            double color = SmoothSigned(input.Color, 100);
            double tonalDepth = SmoothSigned(input.TonalDepth, 100);
            double temperature = SmoothSigned(input.Temperature, 100);

            return new[]
            {
                // There is no independent Gamma field in the current model.
                new SimpleAdjustmentContribution { Exposure = (int)Math.Round(brightness * 32) },
                // The negative contrast delta is a restrained anti-haze guard.
                new SimpleAdjustmentContribution
                {
                    Shadows = (int)Math.Round(darkArea * 90),
                    BlackPoint = (int)Math.Round(darkArea * 24),
                    ContrastPivot = -(int)Math.Round(Math.Max(0, darkArea) * 8)
                },
                // No sharpen field exists in StylePresetData. Clarity uses the
                // existing endpoint-preserving contrast curve only.
                new SimpleAdjustmentContribution { ContrastPivot = (int)Math.Round(clarity * 38) },
                // Saturation remains normalized to 0..100 with 50 as neutral.
                new SimpleAdjustmentContribution
                {
                    Vibrance = (int)Math.Round(color * 55),
                    Saturation = (int)Math.Round(color * 18)
                },
                // Tonal depth expands separation through the existing curve.
                new SimpleAdjustmentContribution
                {
                    BlackPoint = -(int)Math.Round(tonalDepth * 22),
                    WhitePoint = (int)Math.Round(tonalDepth * 22),
                    ContrastPivot = (int)Math.Round(tonalDepth * 25)
                },
                new SimpleAdjustmentContribution { Temperature = (int)Math.Round(temperature * 100) }
            };
        }

        internal static StylePresetData Compose(StylePresetData baseState, IEnumerable<SimpleAdjustmentContribution> contributions)
        {
            var result = (baseState ?? new StylePresetData { Name = "简约调整", Saturation = 50 }).DeepClone();
            int shadows = 0, blackPoint = 0, whitePoint = 0, contrastPivot = 0;
            int vibrance = 0, saturation = 0, exposure = 0, temperature = 0;
            foreach (var contribution in contributions ?? Array.Empty<SimpleAdjustmentContribution>())
            {
                if (contribution == null) continue;
                shadows += contribution.Shadows;
                blackPoint += contribution.BlackPoint;
                whitePoint += contribution.WhitePoint;
                contrastPivot += contribution.ContrastPivot;
                vibrance += contribution.Vibrance;
                saturation += contribution.Saturation;
                exposure += contribution.Exposure;
                temperature += contribution.Temperature;
            }
            result.Shadows = Math.Clamp(result.Shadows + shadows, 0, 200);
            result.BlackPoint = ClampSigned(result.BlackPoint + blackPoint, 100);
            result.WhitePoint = ClampSigned(result.WhitePoint + whitePoint, 100);
            result.ContrastPivot = ClampSigned(result.ContrastPivot + contrastPivot, 100);
            result.Vibrance = ClampSigned(result.Vibrance + vibrance, 100);
            result.Saturation = Math.Clamp(result.Saturation + saturation, 0, 100);
            result.Exposure = ClampSigned(result.Exposure + exposure, 100);
            result.Temperature = ClampSigned(result.Temperature + temperature, 100);
            return result;
        }

        private static int ClampSigned(int value, int limit) => Math.Clamp(value, -limit, limit);

        // Smooth the middle of the slider while retaining useful movement at
        // the ends. This avoids a large visual jump around the neutral point.
        private static double SmoothSigned(int value, int limit)
        {
            double normalized = Math.Clamp(value / (double)limit, -1.0, 1.0);
            double magnitude = Math.Abs(normalized);
            double eased = magnitude * (0.30 + 0.70 * magnitude * magnitude);
            return Math.CopySign(eased, normalized);
        }
    }
}
