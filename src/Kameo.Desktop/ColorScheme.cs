namespace Kameo.Desktop
{
    internal sealed class ColorScheme
    {
        internal string Id { get; }
        internal string Name { get; }
        internal string LightAccent { get; }
        internal string DarkAccent { get; }
        internal string LightTint { get; }
        internal string DarkTint { get; }
        internal string LightBackground { get; }
        internal string LightSurface { get; }
        internal string DarkBackground { get; }
        internal string DarkSurface { get; }

        internal ColorScheme(string id, string name, string lightAccent, string darkAccent, string lightTint, string darkTint,
            string lightBackground, string lightSurface, string darkBackground, string darkSurface)
        {
            Id = id; Name = name; LightAccent = lightAccent; DarkAccent = darkAccent; LightTint = lightTint; DarkTint = darkTint;
            LightBackground = lightBackground; LightSurface = lightSurface; DarkBackground = darkBackground; DarkSurface = darkSurface;
        }
    }
}
