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

        public string MutualRecursionId { get; set; }

        /// <summary>
        /// This property makes sense only if <see cref="MutualRecursionId"/> is set.
        /// It allows you to control which of the mutual recursion methods will be exposed as entry
        /// points and which will remain as inaccessible implementation details.
        /// </summary>
        public bool ExposeAsEntryPoint { get; set; } = true;
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