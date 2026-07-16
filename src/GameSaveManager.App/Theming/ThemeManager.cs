using System.Windows;
using System.Windows.Media;

namespace GameSaveManager.App.Theming;

/// <summary>在运行期替换应用级画刷，确保所有页面共享同一套主题。</summary>
public static class ThemeManager
{
    public static void Apply(bool useLightTheme)
    {
        (string Key, string Color)[] palette = useLightTheme
            ? [
                ("AppBackground", "#F4F7FB"), ("SidebarBackground", "#FFFFFF"), ("Panel", "#FFFFFF"),
                ("ItemBackground", "#F7F9FC"), ("Text", "#172033"), ("Muted", "#64748B"),
                ("Border", "#D7E0EC"), ("TimelineLine", "#7897BC"), ("ButtonBackground", "#EEF3F9"), ("ButtonHover", "#E0ECFA"),
                ("PrimaryBrush", "#287DFF"), ("PrimaryBorder", "#1768DE"), ("InputBackground", "#FFFFFF"),
                ("NavActive", "#E5F0FF"), ("Success", "#168A51"), ("HeroStart", "#E6F1FF"),
                ("HeroMiddle", "#F3F7FC"), ("HeroEnd", "#FFF0DF"), ("ScrollTrack", "#EDF2F7"), ("ScrollThumb", "#B4C2D3"),
                ("Danger", "#C9364D"), ("DangerHover", "#E9A7B1"), ("DangerSurface", "#FFF0F2"),
                ("Warning", "#A76500"), ("WarningSurface", "#FFF6DF"),
                ("GameCardHeroStart", "#D9EAFE"), ("GameCardHeroMiddle", "#EEF3FA"), ("GameCardHeroEnd", "#F7E8F2"),
                ("GameCardAccent", "#1768DE"), ("LogoSurface", "#E1EEFF"), ("LogoAccent", "#1768DE")
              ]
            : [
                ("AppBackground", "#070C13"), ("SidebarBackground", "#05090F"), ("Panel", "#0E1927"),
                ("ItemBackground", "#101D2C"), ("Text", "#F4F8FF"), ("Muted", "#96A8C0"),
                ("Border", "#20324A"), ("TimelineLine", "#6F96C4"), ("ButtonBackground", "#17263A"), ("ButtonHover", "#25456C"),
                ("PrimaryBrush", "#287DFF"), ("PrimaryBorder", "#4894FF"), ("InputBackground", "#0A121D"),
                ("NavActive", "#123C70"), ("Success", "#41D993"), ("HeroStart", "#112E4B"),
                ("HeroMiddle", "#132239"), ("HeroEnd", "#5E4936"), ("ScrollTrack", "#101A27"), ("ScrollThumb", "#47627F"),
                ("Danger", "#E85D70"), ("DangerHover", "#B2394A"), ("DangerSurface", "#3A1720"),
                ("Warning", "#F2B84B"), ("WarningSurface", "#382B14"),
                ("GameCardHeroStart", "#1A3C60"), ("GameCardHeroMiddle", "#172338"), ("GameCardHeroEnd", "#46354B"),
                ("GameCardAccent", "#6CACFF"), ("LogoSurface", "#102C4B"), ("LogoAccent", "#58CBFF")
              ];

        foreach ((string key, string color) in palette)
        {
            System.Windows.Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }
    }
}
