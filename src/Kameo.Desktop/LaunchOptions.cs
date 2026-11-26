namespace Kameo.Desktop
{
    /// <summary>The choices made in the launch options dialog.</summary>
    internal sealed class LaunchOptions
    {
        /// <summary>The existing folder the session starts in.</summary>
        internal string WorkingDirectory { get; init; } = "";
        /// <summary>The chosen <see cref="Kameo.Core.LaunchPermission"/> identifier.</summary>
        internal string PermissionId { get; init; } = Kameo.Core.LaunchPermissions.DefaultId;
        /// <summary>True to also make the folder the saved launch folder.</summary>
        internal bool SaveFolder { get; init; }
    }
}
