namespace Kameo.Publisher
{
    /// <summary>One executable built for every runtime identifier and shipped in every installer.</summary>
    public sealed class ArtifactInfo
    {
        /// <summary>Short artifact name used for staging directories, for example <c>cli</c>.</summary>
        public string Name { get; set; } = "";

        /// <summary>Repository-relative project file.</summary>
        public string Project { get; set; } = "";

        /// <summary>Executable file name without the Windows <c>.exe</c> extension.</summary>
        public string Executable { get; set; } = "";

        /// <summary>Artifact kind: <c>cli</c> (placed on PATH) or <c>tray</c> (menu entry and login startup).</summary>
        public string Kind { get; set; } = "";
    }
}
