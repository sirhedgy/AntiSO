using System.Collections.Generic;
using System.Text;

namespace AntiSO.CodeGen
{
    /// <summary>
    /// Helper class to simplify generation of code strings and particularly adding
    /// some closing lines at the same time as the opening ones
    /// </summary>
    internal class CodeBuilder
    {
        private readonly List<string> _headers = new List<string>();
        private readonly List<string> _footers = new List<string>();

        internal CodeBuilder AddHeader(string text)
        {
            _headers.Add(text);
            return this;
        }

        internal CodeBuilder AddHeaderLine(string text = "")
        {
            _headers.Add(text + "\n");
            return this;
        }

        internal CodeBuilder AddBlockHeader(string text = "")
        {
            _headers.Add(text);
            int prefixLength;
            for (prefixLength = 0; prefixLength < text.Length; prefixLength++)
            {
                if (!char.IsWhiteSpace(text[prefixLength]))
                    break;
            }

            if (text[^1] == '\n')
            {
                _headers.Add(text.Substring(0, prefixLength) + "{\n");
            }
            else if (char.IsWhiteSpace(text[^1]))
            {
                _headers.Add("{\n");
            }
            else
            {
                _headers.Add(" {\n");
            }

            _footers.Add(text.Substring(0, prefixLength) + "}\n");
            return this;
        }

        internal CodeBuilder AddFooter(string text)
        {
            _footers.Add(text);
            return this;
        }

        internal CodeBuilder AddFooterLine(string text)
        {
            _footers.Add(text + "\n");
            return this;
        }

        public override string ToString()
        {
            return BuildString();
        }

        internal string BuildString()
        {
            var code = new StringBuilder();
            foreach (var text in _headers)
            {
                code.Append(text);
            }

            for (int i = _footers.Count - 1; i >= 0; i--)
            {
                code.Append(_footers[i]);
            }

            return code.ToString();
        }
    }
}