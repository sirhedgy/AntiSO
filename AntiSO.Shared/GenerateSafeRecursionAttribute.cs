using System;

namespace AntiSO
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class GenerateSafeRecursionAttribute : Attribute
    {
        public const string DefaultNameSuffix = "_GenSafeRec";

        /// <summary>
        /// Null by default which means the generated method name will be
        /// OriginalMethodName + <see cref="DefaultNameSuffix"/>
        /// </summary>
        public string GeneratedMethodName { get; set; } = null;

        public AccessLevel AccessLevel { get; set; } = AccessLevel.CopyExisting;

        public ExtensionMethod ExtensionMethod { get; set; } = ExtensionMethod.CopyExisting;
    }

    public enum AccessLevel
    {
        CopyExisting,
        Public,
        Protected,
        Internal,
        ProtectedInternal,
        Private
    }

    public enum ExtensionMethod
    {
        CopyExisting,
        Extenstion,
        Usual
    }
}