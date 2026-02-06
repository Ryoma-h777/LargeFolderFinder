using System;
using System.Globalization;

namespace LargeFolderFinder
{
    public static class TextMeasurer
    {
        public static int GetStringWidth(string str)
        {
            int width = 0;
            foreach (char c in str)
            {
                width += GetCharWidth_MSGothic(c);
            }
            return width;
        }

        private static int GetCharWidth_MSGothic(char c)
        {
            if (c < 0x81) return 1; // ASCII
            if (0xff61 <= c && c < 0xffa0) return 1; // 半角カナ

            switch (c)
            {
                case (char)0x00B7: // ·
                case (char)0x2011: // - non-breaking hyphen
                case (char)0x2017: // ‗ DOUBLE LOW LINE
                    return 1;
                    //case (char)0x2010: // ‐ Hyphen
                    //case (char)0x00B1: // ±
                    //case (char)0x00D7: // ×
                    //case (char)0x00F7: // ÷
                    //case (char)0x2018: // ‘ LEFT SINGLE QUOTATION MARK
                    //case (char)0x2019: // ’ RIGHT SINGLE QUOTATION MARK
                    //case (char)0x201C: // “ LEFT DOUBLE QUOTATION MARK
                    //case (char)0x201D: // ” RIGHT DOUBLE QUOTATION MARK
                    //case (char)0x2026: // …
                    //return 2;
            }
            var category = Char.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark
                || category == UnicodeCategory.EnclosingMark
                || category == UnicodeCategory.Format)
                return 0;
            return 2;
        }
    }
}
