namespace Kameo.Core
{
    using System;
    using System.Collections.Generic;

    /// <summary>A harness permission level offered when launching a session, and the harness arguments that select it.</summary>
    public sealed class LaunchPermission
    {
        /// <summary>Stable identifier saved as a preference, such as <c>default</c> or <c>bypass</c>.</summary>
        public string Id { get; }
        /// <summary>Short display label.</summary>
        public string Label { get; }
        /// <summary>One sentence describing what the harness will do without asking.</summary>
        public string Description { get; }
        /// <summary>Arguments appended to the harness command line. Empty for the harness's own default.</summary>
        public IReadOnlyList<string> Arguments { get; }
        /// <summary>True when the harness runs tools without any confirmation.</summary>
        public bool IsDangerous { get; }

        /// <summary>Creates a permission level.</summary>
        public LaunchPermission(string id, string label, string description, IReadOnlyList<string> arguments, bool isDangerous)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
            Id = id;
            Label = label;
            Description = description ?? throw new ArgumentNullException(nameof(description));
            Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
            IsDangerous = isDangerous;
        }
    }
}
