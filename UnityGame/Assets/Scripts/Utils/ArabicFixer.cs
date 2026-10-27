using System;
using System.Text;
using System.Collections.Generic;

namespace MonsterCardGame.Utils
{
    /// <summary>
    /// يقوم بتشكيل الحروف العربية وربطها وعكس ترتيبها لتعمل داخل يونيتي بشكل طبيعي من اليمين لليسار.
    /// Solves disconnected and reversed Arabic text in Unity UI Text.
    /// </summary>
    public static class ArabicFixer
    {
        private struct LetterForms
        {
            public int isolated;
            public int final;
            public int initial;
            public int medial;
            public bool connectsForward;

            public LetterForms(int iso, int fin, int ini, int med, bool fwd)
            {
                isolated = iso;
                final = fin;
                initial = ini;
                medial = med;
                connectsForward = fwd;
            }
        }

        private static readonly Dictionary<int, LetterForms> Letters = new Dictionary<int, LetterForms>
        {
            { 0x0621, new LetterForms(0xFE80, 0xFE80, 0xFE80, 0xFE80, false) }, // ء
            { 0x0622, new LetterForms(0xFE81, 0xFE82, 0xFE81, 0xFE82, false) }, // آ
            { 0x0623, new LetterForms(0xFE83, 0xFE84, 0xFE83, 0xFE84, false) }, // أ
            { 0x0624, new LetterForms(0xFE85, 0xFE86, 0xFE85, 0xFE86, false) }, // ؤ
            { 0x0625, new LetterForms(0xFE87, 0xFE88, 0xFE87, 0xFE88, false) }, // إ
            { 0x0626, new LetterForms(0xFE89, 0xFE8A, 0xFE8B, 0xFE8C, true)  }, // ئ
            { 0x0627, new LetterForms(0xFE8D, 0xFE8E, 0xFE8D, 0xFE8E, false) }, // ا
            { 0x0628, new LetterForms(0xFE8F, 0xFE90, 0xFE91, 0xFE92, true)  }, // ب
            { 0x0629, new LetterForms(0xFE93, 0xFE94, 0xFE93, 0xFE94, false) }, // ة
            { 0x062A, new LetterForms(0xFE95, 0xFE96, 0xFE97, 0xFE98, true)  }, // ت
            { 0x062B, new LetterForms(0xFE99, 0xFE9A, 0xFE9B, 0xFE9C, true)  }, // ث
            { 0x062C, new LetterForms(0xFE9D, 0xFE9E, 0xFE9F, 0xFEA0, true)  }, // ج
            { 0x062D, new LetterForms(0xFEA1, 0xFEA2, 0xFEA3, 0xFEA4, true)  }, // ح
            { 0x062E, new LetterForms(0xFEA5, 0xFEA6, 0xFEA7, 0xFEA8, true)  }, // خ
            { 0x062F, new LetterForms(0xFEA9, 0xFEAA, 0xFEA9, 0xFEAA, false) }, // د
            { 0x0630, new LetterForms(0xFEAB, 0xFEAC, 0xFEAB, 0xFEAC, false) }, // ذ
            { 0x0631, new LetterForms(0xFEAD, 0xFEAE, 0xFEAD, 0xFEAE, false) }, // ر
            { 0x0632, new LetterForms(0xFEAF, 0xFEB0, 0xFEAF, 0xFEB0, false) }, // ز
            { 0x0633, new LetterForms(0xFEB1, 0xFEB2, 0xFEB3, 0xFEB4, true)  }, // س
            { 0x0634, new LetterForms(0xFEB5, 0xFEB6, 0xFEB7, 0xFEB8, true)  }, // ش
            { 0x0635, new LetterForms(0xFEB9, 0xFEBA, 0xFEBB, 0xFEBC, true)  }, // ص
            { 0x0636, new LetterForms(0xFEBD, 0xFEBE, 0xFEBF, 0xFEC0, true)  }, // ض
            { 0x0637, new LetterForms(0xFEC1, 0xFEC2, 0xFEC3, 0xFEC4, true)  }, // ط
            { 0x0638, new LetterForms(0xFEC5, 0xFEC6, 0xFEC7, 0xFEC8, true)  }, // ظ
            { 0x0639, new LetterForms(0xFEC9, 0xFECA, 0xFECB, 0xFECC, true)  }, // ع
            { 0x063A, new LetterForms(0xFECD, 0xFECE, 0xFECF, 0xFED0, true)  }, // غ
            { 0x0641, new LetterForms(0xFED1, 0xFED2, 0xFED3, 0xFED4, true)  }, // ف
            { 0x0642, new LetterForms(0xFED5, 0xFED6, 0xFED7, 0xFED8, true)  }, // ق
            { 0x0643, new LetterForms(0xFED9, 0xFEDA, 0xFEDB, 0xFEDC, true)  }, // ك
            { 0x0644, new LetterForms(0xFEDD, 0xFEDE, 0xFEDF, 0xFEE0, true)  }, // ل
            { 0x0645, new LetterForms(0xFEE1, 0xFEE2, 0xFEE3, 0xFEE4, true)  }, // م
            { 0x0646, new LetterForms(0xFEE5, 0xFEE6, 0xFEE7, 0xFEE8, true)  }, // ن
            { 0x0647, new LetterForms(0xFEE9, 0xFEEA, 0xFEEB, 0xFEEC, true)  }, // ه
            { 0x0648, new LetterForms(0xFEED, 0xFEEE, 0xFEED, 0xFEEE, false) }, // و
            { 0x0649, new LetterForms(0xFEEF, 0xFEF0, 0xFEEF, 0xFEF0, false) }, // ى
            { 0x064A, new LetterForms(0xFEF1, 0xFEF2, 0xFEF3, 0xFEF4, true)  }  // ي
        };

        public static string Fix(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var lines = input.Split('\n');
            var resultLines = new string[lines.Length];

            for (int i = 0; i < lines.Length; i++)
            {
                resultLines[i] = FixLine(lines[i]);
            }

            return string.Join("\n", resultLines);
        }

        private static string FixLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return line;

            var tokens = Tokenize(line);
            bool hasArabic = false;
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].isArabic) { hasArabic = true; break; }
            }

            if (!hasArabic) return line;

            var sb = new StringBuilder();

            // Reverse tokens order for Right-to-Left line orientation
            for (int i = tokens.Count - 1; i >= 0; i--)
            {
                var tok = tokens[i];
                if (tok.isArabic)
                {
                    sb.Append(ShapeAndReverseArabic(tok.text));
                }
                else
                {
                    sb.Append(MirrorPunctuation(tok.text));
                }
            }

            return sb.ToString();
        }

        private static string MirrorPunctuation(string text)
        {
            char[] chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] == '(') chars[i] = ')';
                else if (chars[i] == ')') chars[i] = '(';
                else if (chars[i] == '[') chars[i] = ']';
                else if (chars[i] == ']') chars[i] = '[';
                else if (chars[i] == '{') chars[i] = '}';
                else if (chars[i] == '}') chars[i] = '{';
                else if (chars[i] == '<') chars[i] = '>';
                else if (chars[i] == '>') chars[i] = '<';
            }
            return new string(chars);
        }

        private struct Token
        {
            public string text;
            public bool isArabic;
            public Token(string t, bool a) { text = t; isArabic = a; }
        }

        private static List<Token> Tokenize(string text)
        {
            var list = new List<Token>();
            if (string.IsNullOrEmpty(text)) return list;

            int len = text.Length;
            int idx = 0;

            while (idx < len)
            {
                bool isAr = IsArabicChar(text[idx]);
                int start = idx;

                while (idx < len && IsArabicChar(text[idx]) == isAr)
                {
                    idx++;
                }

                list.Add(new Token(text.Substring(start, idx - start), isAr));
            }

            return list;
        }

        private static bool IsArabicChar(char c)
        {
            int code = (int)c;
            return Letters.ContainsKey(code) || (code >= 0x064B && code <= 0x0652); // Tashkeel
        }

        private static string ShapeAndReverseArabic(string word)
        {
            // 1. Filter out tashkeel and handle Lam-Alef ligatures
            var rawList = new List<char>();
            for (int i = 0; i < word.Length; i++)
            {
                char c = word[i];
                int code = (int)c;
                // Ignore tashkeel for clean display
                if (code >= 0x064B && code <= 0x0652) continue;
                rawList.Add(c);
            }

            // 2. Identify Lam-Alef pairs
            var intermediate = new List<int>();
            for (int i = 0; i < rawList.Count; i++)
            {
                int code = (int)rawList[i];
                if (code == 0x0644 && i + 1 < rawList.Count)
                {
                    int next = (int)rawList[i + 1];
                    int laIso = 0, laFin = 0;

                    if (next == 0x0622) { laIso = 0xFEF5; laFin = 0xFEF6; }
                    else if (next == 0x0623) { laIso = 0xFEF7; laFin = 0xFEF8; }
                    else if (next == 0x0625) { laIso = 0xFEF9; laFin = 0xFEFA; }
                    else if (next == 0x0627) { laIso = 0xFEFB; laFin = 0xFEFC; }

                    if (laIso != 0)
                    {
                        // Check if previous connects
                        bool prevConnects = false;
                        if (intermediate.Count > 0)
                        {
                            int prevCode = rawList[i - 1];
                            if (Letters.TryGetValue(prevCode, out var pForms) && pForms.connectsForward)
                                prevConnects = true;
                        }

                        intermediate.Add(prevConnects ? laFin : laIso);
                        i++; // skip alef
                        continue;
                    }
                }

                intermediate.Add(code);
            }

            // 3. Shape characters
            var shaped = new int[intermediate.Count];
            for (int i = 0; i < intermediate.Count; i++)
            {
                int code = intermediate[i];

                // If it's already a shaped ligature
                if (code >= 0xFEF5 && code <= 0xFEFC)
                {
                    shaped[i] = code;
                    continue;
                }

                if (!Letters.TryGetValue(code, out var forms))
                {
                    shaped[i] = code;
                    continue;
                }

                bool prevConnects = false;
                if (i > 0)
                {
                    int prevCode = intermediate[i - 1];
                    if (Letters.TryGetValue(prevCode, out var pForms) && pForms.connectsForward)
                        prevConnects = true;
                }

                bool nextConnects = false;
                if (i + 1 < intermediate.Count && forms.connectsForward)
                {
                    int nextCode = intermediate[i + 1];
                    if (Letters.ContainsKey(nextCode) || (nextCode >= 0xFEF5 && nextCode <= 0xFEFC))
                        nextConnects = true;
                }

                if (prevConnects && nextConnects)
                    shaped[i] = forms.medial;
                else if (prevConnects)
                    shaped[i] = forms.final;
                else if (nextConnects)
                    shaped[i] = forms.initial;
                else
                    shaped[i] = forms.isolated;
            }

            // 4. Reverse the shaped Arabic characters so Unity's LTR engine displays them RTL
            var sb = new StringBuilder(shaped.Length);
            for (int i = shaped.Length - 1; i >= 0; i--)
            {
                sb.Append((char)shaped[i]);
            }

            return sb.ToString();
        }
    }
}
