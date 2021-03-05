using System.Text;

namespace SyncClipboard.Utility
{
    public static class ClipboardHtmlBuilder
    {
        private const string Header = @"Version:0.9
StartHTML:<<<<<<<<1
EndHTML:<<<<<<<<2
StartFragment:<<<<<<<<3
EndFragment:<<<<<<<<4";

        private const string StartFragment = @"<!--StartFragment-->";
        private const string EndFragment = @"<!--EndFragment-->";

        public static string GetClipboardHtml(string html)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Header);

            sb.Append("<html><body>");
            sb.Append(StartFragment);
            int fragmentStart = GetByteCount(sb);
            sb.Append(html);
            int fragmentEnd = GetByteCount(sb);
            sb.Append(EndFragment);
            sb.Append("</body></html>");

            sb.Replace("<<<<<<<<1", Header.Length.ToString("D9"), 0, Header.Length);
            sb.Replace("<<<<<<<<2", GetByteCount(sb).ToString("D9"), 0, Header.Length);
            sb.Replace("<<<<<<<<3", fragmentStart.ToString("D9"), 0, Header.Length);
            sb.Replace("<<<<<<<<4", fragmentEnd.ToString("D9"), 0, Header.Length);

            return sb.ToString();
        }

        private static int GetByteCount(StringBuilder sb, int start = 0, int end = -1)
        {
            int count = 0;
            end = end > -1 ? end : sb.Length;

            string str = sb.ToString().Substring(start, end);
            count = Encoding.UTF8.GetByteCount(str);
            return count;
        }
    }
}
