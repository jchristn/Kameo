namespace Test.Shared
{
    using System;

    /// <summary>A failed test assertion. Distinct from product exceptions so a test never swallows its own failure.</summary>
    internal sealed class TestFailureException : Exception
    {
        /// <summary>Creates a failure with no message.</summary>
        public TestFailureException() { }

        /// <summary>Creates a failure that explains what was expected.</summary>
        /// <param name="message">What the test expected. May be null.</param>
        public TestFailureException(string? message) : base(message) { }

        /// <summary>Creates a failure caused by another exception.</summary>
        /// <param name="message">What the test expected. May be null.</param>
        /// <param name="innerException">The exception that caused the failure. May be null.</param>
        public TestFailureException(string? message, Exception? innerException) : base(message, innerException) { }
    }
}
