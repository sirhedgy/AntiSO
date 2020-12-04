using System;

namespace AntiSO.CodeGen
{
    /// <summary>
    /// This is the exception for various internal errors of the <see cref="SafeRecursionGenerator"/>
    /// </summary>
    class AntiSOGenException : Exception
    {
        public AntiSOGenException(string? message) : base(message)
        {
        }

        public AntiSOGenException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}