using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// High-performance Devanagari Akshar (orthographic syllable) tokenizer and colorizer.
/// Accurately preserves conjuncts (जोडशब्द), nuktas, matras, halant clusters, and nasal markers (anusvara/chandrabindu)
/// while applying CVD-safe dual-tone colors for dyslexic reading ease.
/// </summary>
public static class DevanagariSyllableParser
{
    // Devanagari Unicode Range Constants
    private const char VIRAMA = '\u094D'; // Halant ्
    private const char NUKTA = '\u093C';  // Nukta ़
    private const char ANUSVARA = '\u0902'; // Anusvara ं
    private const char CHANDRABINDU = '\u0901'; // Chandrabindu ँ
    private const char VISARGA = '\u0903'; // Visarga ः

    /// <summary>
    /// Checks if a character is a Devanagari dependent vowel sign (Matra) or modifier.
    /// </summary>
    private static bool IsDevanagariMatraOrModifier(char c)
    {
        // 0x093E to 0x094C (ा, ि, ी, ु, ू, ृ, ॄ, ॅ, ॆ, े, ै, ॉ, ॊ, ो, ौ)
        // 0x0962, 0x0963 (Vocalic L/LL signs)
        return (c >= '\u093E' && c <= '\u094C') ||
               (c >= '\u094E' && c <= '\u094F') ||
               (c >= '\u0955' && c <= '\u0957') ||
               (c == '\u0962' || c == '\u0963') ||
               (c == ANUSVARA || c == CHANDRABINDU || c == VISARGA || c == NUKTA);
    }

    /// <summary>
    /// Checks if a character is a Devanagari consonant (क to ह, plus additional signs).
    /// </summary>
    private static bool IsDevanagariConsonant(char c)
    {
        return (c >= '\u0915' && c <= '\u0939') || (c >= '\u0958' && c <= '\u095F');
    }

    /// <summary>
    /// Checks if a character is a Devanagari independent vowel (अ to औ).
    /// </summary>
    private static bool IsDevanagariIndependentVowel(char c)
    {
        return (c >= '\u0904' && c <= '\u0914') || (c >= '\u0960' && c <= '\u0961') || (c == '\u0972');
    }

    /// <summary>
    /// Splits a Devanagari word into linguistically sound Akshar (syllable) clusters.
    /// Preserves unbroken ligatures, halants, and conjunct clusters.
    /// </summary>
    public static List<string> SplitIntoAkshars(string word)
    {
        List<string> syllables = new List<string>();
        if (string.IsNullOrEmpty(word)) return syllables;

        int i = 0;
        int len = word.Length;

        while (i < len)
        {
            char current = word[i];

            // If it's punctuation, whitespace, or non-Devanagari, keep single char
            if (char.IsWhiteSpace(current) || char.IsPunctuation(current) || !IsDevanagariChar(current))
            {
                syllables.Add(current.ToString());
                i++;
                continue;
            }

            int clusterStart = i;

            // 1. Independent Vowel Cluster
            if (IsDevanagariIndependentVowel(current))
            {
                i++;
                // Consume any attached anusvara, visarga, or chandrabindu
                while (i < len && (word[i] == ANUSVARA || word[i] == VISARGA || word[i] == CHANDRABINDU))
                {
                    i++;
                }
                syllables.Add(word.Substring(clusterStart, i - clusterStart));
                continue;
            }

            // 2. Consonant / Conjunct Cluster (e.g. क् + य = क्य, स + ् + त = स्त)
            if (IsDevanagariConsonant(current))
            {
                i++;
                if (i < len && word[i] == NUKTA) i++;

                while (i < len)
                {
                    if (word[i] == VIRAMA)
                    {
                        // Halant detected: checks if followed by another consonant or ZWJ/ZWNJ
                        i++;
                        // Check for Zero-Width Joiner/Non-Joiner
                        if (i < len && (word[i] == '\u200D' || word[i] == '\u200C'))
                        {
                            i++;
                        }

                        if (i < len && IsDevanagariConsonant(word[i]))
                        {
                            i++;
                            if (i < len && word[i] == NUKTA) i++;
                        }
                        else
                        {
                            // Standalone halant at end of syllable/word (e.g., 'त्')
                            break;
                        }
                    }
                    else if (IsDevanagariMatraOrModifier(word[i]))
                    {
                        i++;
                    }
                    else
                    {
                        // Reached next consonant or non-modifier
                        break;
                    }
                }

                syllables.Add(word.Substring(clusterStart, i - clusterStart));
                continue;
            }

            // Fallback for any other glyphs
            syllables.Add(current.ToString());
            i++;
        }

        return syllables;
    }

    private static bool IsDevanagariChar(char c)
    {
        return c >= '\u0900' && c <= '\u097F';
    }

    /// <summary>
    /// Formats a complete line of Devanagari text with alternating CVD-safe syllable colors,
    /// background-aware non-breaking syllable separators (narrow space + dot), and expanded word gaps.
    /// Preserves unbroken akshara clusters (consonant + virama + consonant + matras + anusvara/visarga).
    /// </summary>
    public static string ColorizeSyllablesRichText(
        string rawLine,
        Color colorA,
        Color colorB,
        bool alternatePerSyllable = true,
        bool insertSeparators = false,
        float letterSpacingEm = 0f,
        float wordSpacingMultiplier = 1.0f,
        bool onLightBackground = true)
    {
        if (string.IsNullOrEmpty(rawLine)) return rawLine;

        string hexA = ColorUtility.ToHtmlStringRGBA(colorA);
        string hexB = ColorUtility.ToHtmlStringRGBA(colorB);

        // Background-contrast aware separator: #64748B for light cards (4.8:1 on cream), #94A3B8 for dark panels
        string sepColorHex = onLightBackground ? "64748B" : "94A3B8";
        // Narrow non-breaking space (\u202F) prevents line wrap and word-gap confusion
        string separatorTag = $"\u202F<color=#{sepColorHex}>·</color>\u202F";

        // Determine inter-word spacing string
        string wordGap = " ";
        if (wordSpacingMultiplier >= 1.4f)
        {
            wordGap = "   "; // Wide word gap for Intensive Spacing
        }
        else if (wordSpacingMultiplier >= 1.15f)
        {
            wordGap = "  ";  // Moderate word gap
        }

        StringBuilder sb = new StringBuilder(rawLine.Length * 36);

        // Open character tracking tag if specified (TextCore SDF tag)
        bool hasCspace = letterSpacingEm > 0.005f;
        if (hasCspace)
        {
            sb.Append($"<cspace={letterSpacingEm:0.00}em>");
        }

        // If colors are nearly identical or alternating is disabled, return monochrome text with spacing
        if (!alternatePerSyllable || hexA == hexB)
        {
            string[] rawWords = rawLine.Split(' ');
            for (int w = 0; w < rawWords.Length; w++)
            {
                sb.Append($"<color=#{hexA}>{rawWords[w]}</color>");
                if (w < rawWords.Length - 1) sb.Append(wordGap);
            }
            if (hasCspace) sb.Append("</cspace>");
            return sb.ToString();
        }

        string[] words = rawLine.Split(' ');
        bool useColorA = true;

        for (int w = 0; w < words.Length; w++)
        {
            string word = words[w];
            if (string.IsNullOrEmpty(word))
            {
                sb.Append(wordGap);
                continue;
            }

            List<string> akshars = SplitIntoAkshars(word);
            for (int k = 0; k < akshars.Count; k++)
            {
                string akshar = akshars[k];
                if (string.IsNullOrWhiteSpace(akshar))
                {
                    sb.Append(akshar);
                    continue;
                }

                string currentHex = useColorA ? hexA : hexB;
                sb.Append($"<color=#{currentHex}>{akshar}</color>");

                // Add non-breaking subtle separator between syllables inside the same word (never after last syllable)
                if (insertSeparators && k < akshars.Count - 1 && !string.IsNullOrWhiteSpace(akshars[k + 1]))
                {
                    sb.Append(separatorTag);
                }

                useColorA = !useColorA; // Alternate on next syllable
            }

            if (w < words.Length - 1)
            {
                sb.Append(wordGap);
            }
        }

        if (hasCspace)
        {
            sb.Append("</cspace>");
        }

        return sb.ToString();
    }
}
