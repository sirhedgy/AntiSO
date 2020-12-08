using System;

#nullable enable
namespace AntiSO.Infrastructure
{
    
    /// <summary>
    /// Typically this exception means that somehow the "default:" branch was hit
    /// in a generated "switch" that is supposed to be exhaustive
    /// </summary>
    public class AntiSOBadSwitchException : AntiSOException
    {
        public AntiSOBadSwitchException(string? message) : base(message)
        {
        }

        public AntiSOBadSwitchException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}