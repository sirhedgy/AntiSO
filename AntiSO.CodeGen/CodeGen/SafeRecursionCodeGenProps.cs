using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AntiSO.CodeGen
{
    /// <summary>
    /// Mathces the structure of <see cref="GenerateSafeRecursionAttribute"/>
    /// </summary>
    internal class SafeRecursionCodeGenProps
    {
        internal AccessLevel AccessLevel { get; private set; }

        internal ExtensionMethod ExtensionMethod { get; private set; }
        internal string GeneratedMethodName { get; private set; }

        private SafeRecursionCodeGenProps()
        {
        }

        internal static SafeRecursionCodeGenProps ParseFromAttribute(GeneratorExecutionContext context, MethodDeclarationSyntax methodSyntax, AttributeData attributeData)
        {
            var props = new SafeRecursionCodeGenProps();
            foreach (var (name, value) in attributeData.NamedArguments)
            {
                switch (name)
                {
                    case nameof(GenerateSafeRecursionAttribute.AccessLevel):
                        props.AccessLevel = (AccessLevel) value.Value;
                        break;
                    case nameof(GenerateSafeRecursionAttribute.GeneratedMethodName):
                        props.GeneratedMethodName = (string) value.Value;
                        break;
                    case nameof(GenerateSafeRecursionAttribute.ExtensionMethod):
                        props.ExtensionMethod = (ExtensionMethod) value.Value;
                        break;
                    default:
                        context.LogInternalError(methodSyntax.GetLocation(), $"Unexpected GenerateSafeRecursionAttribute property '{name}' = '{value.Value}'");
                        break;
                }
            }

            return props;
        }

        internal string GetAccessLevelModifierString()
        {
            switch (AccessLevel)
            {
                case AccessLevel.Public:
                    return "public";
                case AccessLevel.Protected:
                    return "protected";
                case AccessLevel.Internal:
                    return "internal";
                case AccessLevel.ProtectedInternal:
                    return "protected internal";
                case AccessLevel.Private:
                    return "private";

                case AccessLevel.CopyExisting:
                    throw new AntiSOGenException($"GetAccessLevelModifierString is called for {nameof(AccessLevel.CopyExisting)}");

                default:
                    throw new AntiSOGenException($"GetAccessLevelModifierString is called for unknown value {AccessLevel}");
            }
        }
    }
}