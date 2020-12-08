using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AntiSO.CodeGen
{
    static class GeneratorExecutionContextReportingExt
    {

        // doesn't affect internal error reporting, only logs
#if DEBUG
        internal static bool EmitLogs = true;
#else
        internal static bool EmitLogs = false;
#endif

        private static readonly DiagnosticDescriptor LogDesc = new DiagnosticDescriptor("RGLog", "RGLog", "{0}", "RGLog", DiagnosticSeverity.Warning, true);

        private static readonly DiagnosticDescriptor LogErrorDesc =
            new DiagnosticDescriptor("RG0", "Recursion Generator InternalError", "{0}", "RGInternalError", DiagnosticSeverity.Error, true);

        private static readonly DiagnosticDescriptor BadLanguageErrorDesc = new DiagnosticDescriptor("RG1", "Bad source language", "{0}", "RG", DiagnosticSeverity.Error, true);

        private static readonly DiagnosticDescriptor BadSyntaxErrorDesc = new DiagnosticDescriptor("RG2", "Unsupported syntax", "{0}", "RG", DiagnosticSeverity.Error, true);

        private static readonly DiagnosticDescriptor BadSyntaxWarningDesc =
            new DiagnosticDescriptor("RG3", "Potentially unsupported syntax", "{0}", "RG", DiagnosticSeverity.Warning, true);
        
        private static readonly DiagnosticDescriptor ConfigurationWarningDesc =
            new DiagnosticDescriptor("RG4", "Potentially bad configuration", "{0}", "RG", DiagnosticSeverity.Warning, true);

        private static string ProcessMessage(string msg)
        {
            return msg.Replace("\n", "\\n ");
        }

        internal static void Log(this GeneratorExecutionContext context, string message)
        {
            if (EmitLogs)
                context.ReportDiagnostic(Diagnostic.Create(LogDesc, null, ProcessMessage(message)));
        }

        internal static void Log(this GeneratorExecutionContext context, Location? location, string message)
        {
            if (EmitLogs)
                context.ReportDiagnostic(Diagnostic.Create(LogDesc, location, ProcessMessage(message)));
        }
        internal static void Log(this GeneratorExecutionContext context, CSharpSyntaxNode node, string message)
        {
            if (EmitLogs)
                context.ReportDiagnostic(Diagnostic.Create(LogDesc, node.GetLocation(), ProcessMessage(message)));
        }

        /// <summary>
        /// This means some internal error inside the generator (bug)
        /// </summary>
        internal static void LogInternalError(this GeneratorExecutionContext context, string message)
        {
            context.ReportDiagnostic(Diagnostic.Create(LogErrorDesc, null, ProcessMessage(message)));
        }

        /// <summary>
        /// This means some internal error inside the generator (bug)
        /// </summary>
        internal static void LogInternalError(this GeneratorExecutionContext context, Location? location, string message)
        {
            context.ReportDiagnostic(Diagnostic.Create(LogErrorDesc, location, ProcessMessage(message)));
        }

        internal static void ReportUnsupportedLanguageError(this GeneratorExecutionContext context, string language)
        {
            context.ReportDiagnostic(Diagnostic.Create(BadLanguageErrorDesc, null,
                $"Language '{language}' is not supported. Only C# is supported now."));
        }

        /// <summary>
        /// This means some kind of not supported code
        /// </summary>
        internal static void ReportUnsupportedSyntaxError(this GeneratorExecutionContext context, CSharpSyntaxNode node, string message)
        {
            context.ReportDiagnostic(Diagnostic.Create(BadSyntaxErrorDesc, node.GetLocation(), ProcessMessage(message)));
        }

        /// <summary>
        /// This means some kind of not supported code
        /// </summary>
        internal static void ReportUnsupportedSyntaxError(this GeneratorExecutionContext context, SyntaxNode node, string message)
        {
            context.ReportDiagnostic(Diagnostic.Create(BadSyntaxErrorDesc, node.GetLocation(), ProcessMessage(message)));
        }

        /// <summary>
        /// This means some kind of not supported code
        /// </summary>
        internal static void ReportUnsupportedSyntaxWarning(this GeneratorExecutionContext context, CSharpSyntaxNode node, string message)
        {
            context.ReportDiagnostic(Diagnostic.Create(BadSyntaxWarningDesc, node.GetLocation(), ProcessMessage(message)));
        }

        internal static void ReportConfigurationWarning(this GeneratorExecutionContext context, CSharpSyntaxNode node, string message)
        {
            context.ReportDiagnostic(Diagnostic.Create(ConfigurationWarningDesc, node.GetLocation(), ProcessMessage(message)));
        }
    }
}