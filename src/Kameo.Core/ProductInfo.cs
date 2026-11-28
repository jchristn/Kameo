namespace Kameo.Core
{
    using System;
    using System.Reflection;

    /// <summary>
    /// Product identity shared by the CLI, the desktop, and outbound requests. The version comes from the assembly, which
    /// MSBuild stamps from the single <c>Version</c> element in Directory.Build.props, so no other file repeats it.
    /// </summary>
    /// <remarks>Immutable and thread safe.</remarks>
    public static class ProductInfo
    {
        private static readonly string _Version = ReadVersion();

        /// <summary>The product name, <c>Kameo</c>. Never null.</summary>
        public static string Name => "Kameo";

        /// <summary>The release version as <c>MAJOR.MINOR.PATCH</c>, for example <c>0.2.1</c>. Never null.</summary>
        public static string Version => _Version;

        /// <summary>The release stage shown beside the version, <c>alpha</c> for every 0.x release. Never null.</summary>
        public static string Stage => _Version.StartsWith("0.", StringComparison.Ordinal) ? "alpha" : "";

        private static string ReadVersion()
        {
            Version? version = typeof(ProductInfo).Assembly.GetName().Version;
            return version == null ? "0.0.0" : version.ToString(3);
        }
    }
}
