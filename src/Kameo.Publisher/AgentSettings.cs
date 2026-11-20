namespace Kameo.Publisher
{
    using System.Collections.Generic;

    /// <summary>Login startup registration for the tray artifact.</summary>
    public sealed class AgentSettings
    {
        /// <summary>LaunchAgent label, for example <c>ai.latticetech.kameo.agent</c>.</summary>
        public string Label { get; set; } = "";

        /// <summary>Windows Run value name, for example <c>Kameo Agent</c>.</summary>
        public string WindowsName { get; set; } = "";

        /// <summary>Arguments passed to the tray executable at login.</summary>
        public List<string> Arguments { get; set; } = new List<string>();
    }
}
