using System;

#nullable enable
namespace AntiSO.Infrastructure
{
    /// <summary>
    /// This is the base class for internal exceptions inside the generated code.
    /// Usually you don't have to handle them. If such exception is thrown, most probably
    /// it means there is a bug somewhere in the code generator.
    /// </summary>
    public class AntiSOException : Exception
    {
        public AntiSOException(string? message) : base(message)
        {
        }

        public AntiSOException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}