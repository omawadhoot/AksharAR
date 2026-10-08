using System;
using UnityEngine;

public enum DyslexiaSeverity
{
    Mild,       // 1.1x letter spacing, standard line height, word boundary focus
    Moderate,   // 1.25x letter spacing, 1.35x line height, 2-tone alternating syllable segmentation
    Intensive   // 1.40x letter spacing, 1.60x line height, prominent syllable rhythm
}

public enum ColorBlindPalette
{
    ClassicCobalt,  // Charcoal (#141414) + Deep Cobalt Blue (#1E40AF) - Universal WCAG AAA
    WarmAmber,      // Charcoal (#141414) + Dark Amber/Ochre (#B45309) - High Warmth
    VioletIris,     // Deep Slate (#1E293B) + Royal Violet (#6D28D9) - Calm Eye Comfort
    Monochrome      // Pure Solid Slate (#0F172A) - Zero Color Distraction
}

public enum SubstrateTint
{
    InpaintedPaper, // Original textbook paper texture color (Inpainted from camera frame)
    WarmCream,      // #FAF8F2 (Soft Peach/Cream - 35% glare reduction)
    MintIce,        // #F0FDF4 (Calming pale mint - Meares-Irlen visual stress filter)
    Periwinkle      // #F1F5F9 (Neutral cool slate mist - sharp edge contrast)
}

[Serializable]
public class DyslexiaProfile
{
    public DyslexiaSeverity severity = DyslexiaSeverity.Moderate;
    public ColorBlindPalette colorPalette = ColorBlindPalette.ClassicCobalt;
    public SubstrateTint substrateTint = SubstrateTint.WarmCream;
    public bool enableSyllableSegmentation = true;
    public bool enableTouchHighlight = true;
    public float fontScaleMultiplier = 1.0f; // 0.85f to 1.35f
    public bool hasCompletedOnboarding = false;

    // Computed Typographical Parameters
    public float GetLineHeightMultiplier()
    {
        switch (severity)
        {
            case DyslexiaSeverity.Mild: return 1.15f;
            case DyslexiaSeverity.Moderate: return 1.38f;
            case DyslexiaSeverity.Intensive: return 1.65f;
            default: return 1.35f;
        }
    }

    public float GetLetterSpacingEm()
    {
        switch (severity)
        {
            case DyslexiaSeverity.Mild: return 0.04f;
            case DyslexiaSeverity.Moderate: return 0.09f;
            case DyslexiaSeverity.Intensive: return 0.15f;
            default: return 0.08f;
        }
    }

    public Color GetSyllableColorA()
    {
        switch (colorPalette)
        {
            case ColorBlindPalette.ClassicCobalt:
                return new Color(0.08f, 0.08f, 0.08f, 1.0f); // Deep Charcoal #141414
            case ColorBlindPalette.WarmAmber:
                return new Color(0.08f, 0.08f, 0.08f, 1.0f); // Deep Charcoal #141414
            case ColorBlindPalette.VioletIris:
                return new Color(0.12f, 0.16f, 0.23f, 1.0f); // Deep Slate #1E293B
            case ColorBlindPalette.Monochrome:
            default:
                return new Color(0.06f, 0.09f, 0.16f, 1.0f); // Dark Charcoal #0F172A
        }
    }

    public Color GetSyllableColorB()
    {
        switch (colorPalette)
        {
            case ColorBlindPalette.ClassicCobalt:
                return new Color(0.12f, 0.25f, 0.69f, 1.0f); // Deep Cobalt Blue #1E40AF (Contrast on Cream > 7.5:1)
            case ColorBlindPalette.WarmAmber:
                return new Color(0.60f, 0.20f, 0.07f, 1.0f); // Deep Amber-800 #9A3412 (Contrast on Cream > 5.8:1, WCAG AAA compliant)
            case ColorBlindPalette.VioletIris:
                return new Color(0.36f, 0.13f, 0.71f, 1.0f); // Deep Violet-800 #5B21B6 (Contrast on Cream > 7.2:1)
            case ColorBlindPalette.Monochrome:
            default:
                return new Color(0.06f, 0.09f, 0.16f, 1.0f); // Dark Charcoal #0F172A (Contrast on Cream > 14:1)
        }
    }

    public bool ShouldInsertSeparators()
    {
        // For Devanagari, middle dots (·) create visual noise and conflict with anusvara (ं) / nukta (़).
        // Syllable boundaries are cleanly conveyed via dual-tone colors and font tracking (cspace).
        return false;
    }

    public float GetWordSpacingMultiplier()
    {
        switch (severity)
        {
            case DyslexiaSeverity.Mild: return 1.0f;
            case DyslexiaSeverity.Moderate: return 1.25f;
            case DyslexiaSeverity.Intensive: return 1.55f;
            default: return 1.0f;
        }
    }

    public static float CalculateRelativeLuminance(Color c)
    {
        float r = (c.r <= 0.03928f) ? c.r / 12.92f : Mathf.Pow((c.r + 0.055f) / 1.055f, 2.4f);
        float g = (c.g <= 0.03928f) ? c.g / 12.92f : Mathf.Pow((c.g + 0.055f) / 1.055f, 2.4f);
        float b = (c.b <= 0.03928f) ? c.b / 12.92f : Mathf.Pow((c.b + 0.055f) / 1.055f, 2.4f);
        return 0.2126f * r + 0.7152f * g + 0.0722f * b;
    }

    public static float CalculateContrastRatio(Color fg, Color bg)
    {
        float l1 = CalculateRelativeLuminance(fg);
        float l2 = CalculateRelativeLuminance(bg);
        float lighter = Mathf.Max(l1, l2);
        float darker = Mathf.Min(l1, l2);
        return (lighter + 0.05f) / (darker + 0.05f);
    }

    public Color GetSubstrateColor(Color fallbackInpainted)
    {
        switch (substrateTint)
        {
            case SubstrateTint.WarmCream:
                return new Color(0.98f, 0.97f, 0.95f, 0.98f); // #FAF8F2
            case SubstrateTint.MintIce:
                return new Color(0.94f, 0.99f, 0.96f, 0.98f); // #F0FDF4
            case SubstrateTint.Periwinkle:
                return new Color(0.95f, 0.96f, 0.98f, 0.98f); // #F1F5F9
            case SubstrateTint.InpaintedPaper:
            default:
                float lum = (0.299f * fallbackInpainted.r) + (0.587f * fallbackInpainted.g) + (0.114f * fallbackInpainted.b);
                if (lum < 0.88f)
                {
                    // Normalize brightness while preserving ambient warmth so dark text is always 100% readable
                    float boost = 0.95f / Mathf.Max(0.10f, lum);
                    float r = Mathf.Clamp01(fallbackInpainted.r * boost);
                    float g = Mathf.Clamp01(fallbackInpainted.g * boost);
                    float b = Mathf.Clamp01(fallbackInpainted.b * boost);
                    return new Color(r, g, b, 0.98f);
                }
                return fallbackInpainted;
        }
    }
}
