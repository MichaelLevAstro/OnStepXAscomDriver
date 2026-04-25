using System;
using System.Drawing;
using ASCOM.OnStepX.Config;

namespace ASCOM.OnStepX.Ui.Theming
{
    internal enum ThemeMode { Dark, Light }

    internal sealed class ThemePalette
    {
        public Color Bg;
        public Color Panel;
        public Color Panel2;
        public Color PanelInset;
        public Color Border;
        public Color BorderStrong;
        public Color Text;
        public Color TextDim;
        public Color TextFaint;
        public Color Label;
        public Color Accent;
        public Color AccentInk;
        public Color AccentSoft;
        public Color Danger;
        public Color Ok;
        public Color Warn;
        public Color Info;
        public Color InputBg;
        public Color InputBorder;
        public Color InputBorderHover;
        public Color BtnBg;
        public Color BtnBgHover;
        public Color BtnBgActive;
        public Color Chip;
        public Color ConsoleBg;
        public Color ConsoleLine;
        public Color ScrollTrack;
        public Color ScrollThumb;
        public Color ScrollThumbHover;
        public Color ColTs;
        public Color ColCmd;
        public Color ColResp;
        public Color ColMeta;
    }

    internal static class Theme
    {
        public static event EventHandler Changed;
        public static ThemeMode Mode { get; private set; }
        public static ThemePalette P { get; private set; }

        static Theme()
        {
            var saved = (DriverSettings.Theme ?? "dark").Trim().ToLowerInvariant();
            SetMode(saved == "light" ? ThemeMode.Light : ThemeMode.Dark, persist: false, notify: false);
        }

        public static void SetMode(ThemeMode mode) => SetMode(mode, persist: true, notify: true);

        private static void SetMode(ThemeMode mode, bool persist, bool notify)
        {
            Mode = mode;
            P = mode == ThemeMode.Dark ? BuildDark() : BuildLight();
            if (persist) DriverSettings.Theme = mode == ThemeMode.Dark ? "dark" : "light";
            if (notify) Changed?.Invoke(null, EventArgs.Empty);
        }

        private static Color Hex(string h)
        {
            if (h[0] == '#') h = h.Substring(1);
            int r = Convert.ToInt32(h.Substring(0, 2), 16);
            int g = Convert.ToInt32(h.Substring(2, 2), 16);
            int b = Convert.ToInt32(h.Substring(4, 2), 16);
            return Color.FromArgb(r, g, b);
        }

        private static Color Hexa(string h, int a)
        {
            var c = Hex(h);
            return Color.FromArgb(a, c);
        }

        private static ThemePalette BuildDark() => new ThemePalette
        {
            Bg               = Hex("#14171c"),
            Panel            = Hex("#1b1f26"),
            Panel2           = Hex("#1f242d"),
            PanelInset       = Hex("#10131a"),
            Border           = Hex("#2a313c"),
            BorderStrong     = Hex("#3a4250"),
            Text             = Hex("#e6ebf2"),
            TextDim          = Hex("#9aa4b2"),
            TextFaint        = Hex("#6b7582"),
            Label            = Hex("#8a94a3"),
            // oklch(0.62 0.22 25) — warm red-orange
            Accent           = Hex("#e5482d"),
            AccentInk        = Hex("#ffffff"),
            AccentSoft       = Hexa("#e5482d", 31), // ~12%
            Danger           = Hex("#e5482d"),
            Ok               = Hex("#4ac27a"),
            Warn             = Hex("#d4b34a"),
            Info             = Hex("#5f9ed4"),
            InputBg          = Hex("#0f1218"),
            InputBorder      = Hex("#323a47"),
            InputBorderHover = Hex("#4a5362"),
            BtnBg            = Hex("#242a33"),
            BtnBgHover       = Hex("#2c333e"),
            BtnBgActive      = Hex("#323a46"),
            Chip             = Hex("#242a33"),
            ConsoleBg        = Hex("#0b0d12"),
            ConsoleLine      = Hex("#141821"),
            ScrollTrack      = Hex("#181c24"),
            ScrollThumb      = Hex("#39414f"),
            ScrollThumbHover = Hex("#4a5362"),
            ColTs            = Hex("#6b98e0"),
            ColCmd           = Hex("#e8b86d"),
            ColResp          = Hex("#8bd0a0"),
            ColMeta          = Hex("#8a94a3"),
        };

        private static ThemePalette BuildLight() => new ThemePalette
        {
            Bg               = Hex("#eef0f3"),
            Panel            = Hex("#ffffff"),
            Panel2           = Hex("#f7f8fa"),
            PanelInset       = Hex("#f1f3f6"),
            Border           = Hex("#dde1e7"),
            BorderStrong     = Hex("#c6ccd5"),
            Text             = Hex("#1a1d22"),
            TextDim          = Hex("#51585f"),
            TextFaint        = Hex("#7a818a"),
            Label            = Hex("#5a626c"),
            Accent           = Hex("#e5482d"),
            AccentInk        = Hex("#ffffff"),
            AccentSoft       = Hexa("#e5482d", 31),
            Danger           = Hex("#e5482d"),
            Ok               = Hex("#3aa26a"),
            Warn             = Hex("#b9983a"),
            Info             = Hex("#4a86b8"),
            InputBg          = Hex("#ffffff"),
            InputBorder      = Hex("#c8cdd5"),
            InputBorderHover = Hex("#9aa2ad"),
            BtnBg            = Hex("#f3f5f8"),
            BtnBgHover       = Hex("#e8ebf0"),
            BtnBgActive      = Hex("#dde1e7"),
            Chip             = Hex("#eef1f5"),
            ConsoleBg        = Hex("#f5f7fa"),
            ConsoleLine      = Hex("#e6eaef"),
            ScrollTrack      = Hex("#e3e6eb"),
            ScrollThumb      = Hex("#b6bcc6"),
            ScrollThumbHover = Hex("#9aa2ad"),
            ColTs            = Hex("#2b5aaa"),
            ColCmd           = Hex("#9a5f00"),
            ColResp          = Hex("#1d7a44"),
            ColMeta          = Hex("#5a626c"),
        };
    }
}
