namespace Kameo.Core
{
    using System;

    /// <summary>The Codex app-server ended or answered a usage request with an error. Messages are safe to display.</summary>
    internal sealed class CodexProtocolException : Exception
    {
        /// <summary>Creates the exception with a display-safe message.</summary>
        public CodexProtocolException(string message) : base(message) { }
    }
}
