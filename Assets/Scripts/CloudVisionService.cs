using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

#region Cloud Vision Data Structures

[Serializable]
public class VisionResponseWrapper
{
    public List<VisionResponseItem> responses;
}

[Serializable]
public class VisionResponseItem
{
    public List<EntityAnnotation> textAnnotations;
    public FullTextAnnotation fullTextAnnotation;
    public VisionError error;
}

[Serializable]
public class VisionError
{
    public int code;
    public string message;
}

[Serializable]
public class EntityAnnotation
{
    public string description;
    public BoundingPoly boundingPoly;
}

[Serializable]
public class FullTextAnnotation
{
    public string text;
    public List<VisionPage> pages;
}

[Serializable]
public class VisionPage
{
    public int width;
    public int height;
    public List<VisionBlock> blocks;
}

[Serializable]
public class VisionBlock
{
    public string blockType;
    public BoundingPoly boundingBox;
    public List<VisionParagraph> paragraphs;
}

[Serializable]
public class VisionParagraph
{
    public BoundingPoly boundingBox;
    public List<VisionWord> words;
}

[Serializable]
public class VisionWord
{
    public BoundingPoly boundingBox;
    public List<VisionSymbol> symbols;
}

[Serializable]
public class VisionSymbol
{
    public string text;
    public BoundingPoly boundingBox;
    public SymbolProperty property;
}

[Serializable]
public class SymbolProperty
{
    public DetectedBreak detectedBreak;
}

[Serializable]
public class DetectedBreak
{
    public string type; // "SPACE", "SURE_SPACE", "EOL_SURE_SPACE", "LINE_BREAK", "HYPHEN"
    public bool isPrefix;
}

[Serializable]
public class BoundingPoly
{
    public List<Vertex> vertices;
}

[Serializable]
public class Vertex
{
    public float x;
    public float y;
}

#endregion

public class CloudVisionService : MonoBehaviour
{
    [Header("Backend Proxy Setup")]
    [Tooltip("URL of your secure backend proxy (e.g. https://akshar-ar.vercel.app/api/ocr)")]
    [SerializeField] private string proxyUrl = "https://akshar-ar.vercel.app/api/ocr";

    [Tooltip("Optional pre-shared authentication secret header for Vercel proxy security")]
    [SerializeField] private string proxyAuthToken = "AksharAR_Secret_Token_2026_x9k2";

    [Header("Overlay Controllers")]
    [SerializeField] private ARWorldSpaceUIToolkitController worldSpaceUIToolkitController;
    [SerializeField] private ARLineOverlayController overlayController;
    [SerializeField] private ARWorldSpaceOverlayController worldSpaceOverlayController;

    private Texture2D lastCapturedTexture;

    private void OnDestroy()
    {
        if (lastCapturedTexture != null)
        {
            Destroy(lastCapturedTexture);
            lastCapturedTexture = null;
        }
    }

    private void Awake()
    {
        if (worldSpaceUIToolkitController == null)
            worldSpaceUIToolkitController = FindFirstObjectByType<ARWorldSpaceUIToolkitController>();
        if (overlayController == null)
            overlayController = FindFirstObjectByType<ARLineOverlayController>();
        if (worldSpaceOverlayController == null)
            worldSpaceOverlayController = FindFirstObjectByType<ARWorldSpaceOverlayController>();
    }

    public string ProxyUrl
    {
        get => proxyUrl;
        set => proxyUrl = value;
    }

    public string ProxyAuthToken
    {
        get => proxyAuthToken;
        set => proxyAuthToken = value;
    }

    /// <summary>
    /// Sends a Texture2D to your secure backend proxy for Hindi text detection.
    /// Safely clamps resolution (max 1280px) and uncompresses before encoding to lightweight JPG.
    /// </summary>
    public void DetectTextFromTexture(Texture2D inputTexture)
    {
        if (string.IsNullOrEmpty(proxyUrl))
        {
            Debug.LogError("[CloudVisionService] Backend Proxy URL is empty!");
            return;
        }

        if (inputTexture == null)
        {
            Debug.LogError("[CloudVisionService] Input texture is null!");
            return;
        }

        // Clamp resolution so JPG payload stays compact (~150-300KB) and avoids API timeouts / Code 14
        int maxDim = 1280;
        int targetWidth = inputTexture.width;
        int targetHeight = inputTexture.height;

        if (targetWidth > maxDim || targetHeight > maxDim)
        {
            float scale = Mathf.Min((float)maxDim / targetWidth, (float)maxDim / targetHeight);
            targetWidth = Mathf.Max(1, Mathf.RoundToInt(targetWidth * scale));
            targetHeight = Mathf.Max(1, Mathf.RoundToInt(targetHeight * scale));
        }

        if (lastCapturedTexture != null)
        {
            Destroy(lastCapturedTexture);
            lastCapturedTexture = null;
        }
        Texture2D readableTexture = GetUncompressedResizedTexture(inputTexture, targetWidth, targetHeight);
        lastCapturedTexture = readableTexture;

        byte[] imageBytes = readableTexture.EncodeToJPG(78);
        string base64Image = Convert.ToBase64String(imageBytes);

        StartCoroutine(SendProxyApiRequest(base64Image, targetWidth, targetHeight));
    }

    private Texture2D GetUncompressedResizedTexture(Texture2D source, int targetWidth, int targetHeight)
    {
        RenderTexture rt = RenderTexture.GetTemporary(
            targetWidth, 
            targetHeight, 
            0, 
            RenderTextureFormat.Default, 
            RenderTextureReadWrite.Linear);

        Graphics.Blit(source, rt);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D readableTexture = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
        readableTexture.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
        readableTexture.Apply();

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        return readableTexture;
    }

    private IEnumerator SendProxyApiRequest(string base64Image, int imageWidth, int imageHeight, int retryCount = 0)
    {
        string jsonPayload = $"{{\"image\":\"{base64Image}\"}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);

        using (UnityWebRequest webRequest = new UnityWebRequest(proxyUrl, "POST"))
        {
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");

            if (!string.IsNullOrEmpty(proxyAuthToken))
            {
                webRequest.SetRequestHeader("X-Proxy-Auth-Token", proxyAuthToken);
            }

            Debug.Log($"[CloudVisionService] Sending request ({bodyRaw.Length / 1024} KB) to Secure Backend Proxy: {proxyUrl} (Attempt {retryCount + 1}/4)");
            yield return webRequest.SendWebRequest();

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[CloudVisionService] Proxy Error: {webRequest.error}\nResponse: {webRequest.downloadHandler.text}");
                if (retryCount < 3)
                {
                    float delay = 1.0f * Mathf.Pow(1.5f, retryCount);
                    Debug.LogWarning($"[CloudVisionService] Retrying API request in {delay:F1}s (Retry {retryCount + 1}/3)...");
                    yield return new WaitForSeconds(delay);
                    StartCoroutine(SendProxyApiRequest(base64Image, imageWidth, imageHeight, retryCount + 1));
                    yield break;
                }
                NotifyScanFailed();
                yield break;
            }

            string responseJson = webRequest.downloadHandler.text;

            // Check if response contains Google API error code 14 (Unavailable) or transient error
            VisionResponseWrapper wrapper = null;
            try
            {
                wrapper = JsonUtility.FromJson<VisionResponseWrapper>(responseJson);
            }
            catch { }

            if (wrapper != null && wrapper.responses != null && wrapper.responses.Count > 0 && wrapper.responses[0].error != null && wrapper.responses[0].error.code != 0)
            {
                int errCode = wrapper.responses[0].error.code;
                string errMsg = wrapper.responses[0].error.message;
                Debug.LogError($"[CloudVisionService] Vision API Error Code {errCode}: {errMsg}");

                // Code 14 is UNAVAILABLE / transient upstream load: retry automatically with backoff
                if ((errCode == 14 || errCode == 4 || errCode == 8 || errCode == 13) && retryCount < 3)
                {
                    float delay = 1.0f * Mathf.Pow(1.5f, retryCount);
                    Debug.LogWarning($"[CloudVisionService] Error {errCode} is transient. Retrying in {delay:F1}s (Retry {retryCount + 1}/3)...");
                    yield return new WaitForSeconds(delay);
                    StartCoroutine(SendProxyApiRequest(base64Image, imageWidth, imageHeight, retryCount + 1));
                    yield break;
                }

                NotifyScanFailed();
                yield break;
            }

            Debug.Log($"[CloudVisionService] SUCCESS Response Received from Backend Proxy ({responseJson.Length} bytes).");
            ProcessVisionResponse(responseJson, imageWidth, imageHeight);
        }
    }

    private void NotifyScanFailed()
    {
        if (overlayController == null)
            overlayController = FindFirstObjectByType<ARLineOverlayController>();
        if (overlayController != null)
            overlayController.ResetScanButton();
    }

    private struct WordBox
    {
        public string text;
        public float minX, minY, maxX, maxY;
        public float centerY => (minY + maxY) / 2f;
        public float height => maxY - minY;
    }

    private class LineCluster
    {
        public float averageY;
        public List<WordBox> words = new List<WordBox>();
    }

    private void ProcessVisionResponse(string responseJson, int imageWidth, int imageHeight)
    {
        try
        {
            VisionResponseWrapper responseWrapper = JsonUtility.FromJson<VisionResponseWrapper>(responseJson);

            if (responseWrapper == null || responseWrapper.responses == null || responseWrapper.responses.Count == 0)
            {
                Debug.LogWarning("[CloudVisionService] No response object returned from API.");
                return;
            }

            VisionResponseItem firstResponse = responseWrapper.responses[0];

            if (firstResponse.error != null && firstResponse.error.code != 0)
            {
                Debug.LogError($"[CloudVisionService] Proxy Returned Error Code {firstResponse.error.code}: {firstResponse.error.message}");
                if (overlayController != null) overlayController.ResetScanButton();
                return;
            }

            if (firstResponse.textAnnotations == null || firstResponse.textAnnotations.Count <= 1)
            {
                Debug.LogWarning("[CloudVisionService] No text detected on image.");
                if (overlayController != null) overlayController.ResetScanButton();
                return;
            }

            string fullPageText = firstResponse.textAnnotations[0].description;
            Debug.Log($"[CloudVisionService] Full Detected Page Text Length: {fullPageText.Length} chars.");

            List<DetectedTextLine> detectedLines = null;

            // 1. Google Lens Document Layout Engine: Try FullTextAnnotation blocks first!
            if (firstResponse.fullTextAnnotation != null && 
                firstResponse.fullTextAnnotation.pages != null && 
                firstResponse.fullTextAnnotation.pages.Count > 0)
            {
                detectedLines = ParseFullTextAnnotation(firstResponse.fullTextAnnotation, imageWidth, imageHeight);
                if (detectedLines != null && detectedLines.Count > 0)
                {
                    Debug.Log($"[CloudVisionService] Google Lens Layout Analysis extracted {detectedLines.Count} structural lines from FullTextAnnotation.");
                }
            }

            // 2. Fallback to heuristic clustering if FullTextAnnotation produced no lines
            if (detectedLines == null || detectedLines.Count == 0)
            {
                detectedLines = ParseViaHeuristicClustering(firstResponse, imageWidth, imageHeight);
            }

            if (detectedLines == null || detectedLines.Count == 0)
            {
                Debug.LogWarning("[CloudVisionService] No valid text lines extracted.");
                if (overlayController != null) overlayController.ResetScanButton();
                return;
            }

            Debug.Log($"[CloudVisionService] Successfully prepared {detectedLines.Count} clean text line strips for Freeze-Frame overlay.");

            // Clear 3D world quad so it does not linger in the background
            if (worldSpaceUIToolkitController != null)
            {
                worldSpaceUIToolkitController.ClearOverlays();
            }

            if (overlayController != null)
            {
                overlayController.DisplayDetectedLines(detectedLines, new Vector2(imageWidth, imageHeight), lastCapturedTexture);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CloudVisionService] JSON Parsing Exception: {ex.Message}");
            if (overlayController != null) overlayController.ResetScanButton();
        }
    }

    /// <summary>
    /// Google Lens Approach: Parses macro visual blocks (paragraphs, lines) directly from Google's deep learning
    /// FullTextAnnotation document layout tree, checking trailing symbol detectedBreak (Nuance 1) and
    /// filtering base consonants for empirical Shirorekha alignment (Nuance 2).
    /// </summary>
    private List<DetectedTextLine> ParseFullTextAnnotation(FullTextAnnotation fullText, int imageWidth, int imageHeight)
    {
        if (fullText == null || fullText.pages == null || fullText.pages.Count == 0) return null;

        List<DetectedTextLine> lines = new List<DetectedTextLine>();

        foreach (var page in fullText.pages)
        {
            if (page.blocks == null) continue;

            foreach (var block in page.blocks)
            {
                if (block.paragraphs == null) continue;

                foreach (var paragraph in block.paragraphs)
                {
                    if (paragraph.words == null || paragraph.words.Count == 0) continue;

                    List<VisionWord> currentPlateWords = new List<VisionWord>();

                    for (int wIdx = 0; wIdx < paragraph.words.Count; wIdx++)
                    {
                        var word = paragraph.words[wIdx];
                        currentPlateWords.Add(word);

                        // Nuance 1: detectedBreak is on the trailing symbol of the word
                        bool isLineBreak = false;
                        bool isEolSureSpace = false;

                        if (word.symbols != null && word.symbols.Count > 0)
                        {
                            var lastSymbol = word.symbols[word.symbols.Count - 1];
                            if (lastSymbol.property != null && lastSymbol.property.detectedBreak != null)
                            {
                                string bType = lastSymbol.property.detectedBreak.type;
                                if (bType == "LINE_BREAK" || bType == "HYPHEN")
                                {
                                    isLineBreak = true;
                                }
                                else if (bType == "EOL_SURE_SPACE" || bType == "SURE_SPACE")
                                {
                                    isEolSureSpace = true;
                                }
                            }
                        }

                        // EOL_SURE_SPACE / LINE_BREAK / HYPHEN all mean "last word on a printed line".
                        bool splitPlate = isLineBreak || isEolSureSpace;
                        if (!splitPlate && wIdx < paragraph.words.Count - 1)
                        {
                            var nextWord = paragraph.words[wIdx + 1];
                            float wordH = Mathf.Max(GetWordHeight(word), 20f);

                            // Geometric safety net: next word sits on a different row or wrapped back left
                            float dy = Mathf.Abs(GetWordCenterY(nextWord) - GetWordCenterY(word));
                            bool newRow = dy > wordH * 0.6f || GetWordMinX(nextWord) < GetWordMinX(word);

                            // Caesura split: large horizontal gap between hemistichs
                            float gap = GetWordMinX(nextWord) - GetWordMaxX(word);
                            bool caesura = gap > Mathf.Max(wordH * 1.8f, 40f) && !IsWordVerseMarker(nextWord);

                            splitPlate = newRow || caesura;
                        }

                        if (splitPlate || wIdx == paragraph.words.Count - 1)
                        {
                            if (currentPlateWords.Count > 0)
                            {
                                AddFullTextPlate(currentPlateWords, lines);
                                currentPlateWords.Clear();
                            }
                        }
                    }
                }
            }
        }

        return lines;
    }

    private void AddFullTextPlate(List<VisionWord> plateWords, List<DetectedTextLine> lines)
    {
        if (plateWords == null || plateWords.Count == 0) return;

        List<string> wordTexts = new List<string>();
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        // Nuance 2: Empirical Shirorekha baseline estimation from base consonants
        List<float> baseConsonantTopYs = new List<float>();

        foreach (var word in plateWords)
        {
            if (word.symbols == null || word.symbols.Count == 0) continue;

            string wStr = "";
            foreach (var sym in word.symbols)
            {
                wStr += sym.text;
                if (sym.boundingBox != null && sym.boundingBox.vertices != null && sym.boundingBox.vertices.Count >= 4)
                {
                    float sTopY = Mathf.Min(sym.boundingBox.vertices[0].y, sym.boundingBox.vertices[1].y);
                    float sBotY = Mathf.Max(sym.boundingBox.vertices[2].y, sym.boundingBox.vertices[3].y);
                    float sLeftX = Mathf.Min(sym.boundingBox.vertices[0].x, sym.boundingBox.vertices[3].x);
                    float sRightX = Mathf.Max(sym.boundingBox.vertices[1].x, sym.boundingBox.vertices[2].x);

                    minX = Mathf.Min(minX, sLeftX);
                    maxX = Mathf.Max(maxX, sRightX);
                    minY = Mathf.Min(minY, sTopY);
                    maxY = Mathf.Max(maxY, sBotY);

                    // Nuance 2: Filter to base consonants / independent vowels, excluding dependent matras
                    if (IsBaseConsonantOrVowel(sym.text))
                    {
                        baseConsonantTopYs.Add(sTopY);
                    }
                }
            }
            if (!string.IsNullOrEmpty(wStr))
                wordTexts.Add(wStr);
        }

        if (wordTexts.Count == 0 || minX >= maxX || minY >= maxY) return;

        string plateText = string.Join(" ", wordTexts).Trim();
        if (string.IsNullOrEmpty(plateText) || IsNoisePlate(plateText)) return;

        float width = maxX - minX;
        float height = maxY - minY;

        // Bound height around median word height (prevents matra/danda outliers ballooning font size)
        var wordHeights = plateWords.Select(w => GetWordHeight(w)).OrderBy(h => h).ToList();
        float medH = Mathf.Max(wordHeights[wordHeights.Count / 2], 24f);
        height = Mathf.Clamp(height, medH, medH * 1.30f);
        width = Mathf.Max(width, 30f);

        lines.Add(new DetectedTextLine
        {
            text = plateText,
            boundingBox = new Rect(minX, minY, width, height)
        });
    }

    /// <summary>
    /// Nuance 2: Complex Brahmic script filter.
    /// Excludes dependent vowel signs / matras (\u093E to \u094F) and signs (\u0901 to \u0903, \u093C)
    /// to sample only solid base consonants (\u0915 to \u0939) and independent vowels (\u0904 to \u0914).
    /// </summary>
    private static bool IsBaseConsonantOrVowel(string symbolText)
    {
        if (string.IsNullOrEmpty(symbolText)) return false;
        foreach (char c in symbolText)
        {
            if ((c >= '\u093E' && c <= '\u094F') || (c >= '\u0901' && c <= '\u0903') || c == '\u093C')
                return false;
            if ((c >= '\u0904' && c <= '\u0914') || (c >= '\u0915' && c <= '\u0939'))
                return true;
        }
        return false;
    }

    private static float GetWordMinX(VisionWord word)
    {
        if (word.boundingBox != null && word.boundingBox.vertices != null && word.boundingBox.vertices.Count >= 4)
            return Mathf.Min(word.boundingBox.vertices[0].x, word.boundingBox.vertices[3].x);
        return 0f;
    }

    private static float GetWordMaxX(VisionWord word)
    {
        if (word.boundingBox != null && word.boundingBox.vertices != null && word.boundingBox.vertices.Count >= 4)
            return Mathf.Max(word.boundingBox.vertices[1].x, word.boundingBox.vertices[2].x);
        return 0f;
    }

    private static float GetWordCenterY(VisionWord word)
    {
        if (word.boundingBox != null && word.boundingBox.vertices != null && word.boundingBox.vertices.Count >= 4)
        {
            float minY = Mathf.Min(word.boundingBox.vertices[0].y, word.boundingBox.vertices[1].y);
            float maxY = Mathf.Max(word.boundingBox.vertices[2].y, word.boundingBox.vertices[3].y);
            return (minY + maxY) * 0.5f;
        }
        return 0f;
    }

    private static float GetWordHeight(VisionWord word)
    {
        if (word.boundingBox != null && word.boundingBox.vertices != null && word.boundingBox.vertices.Count >= 4)
        {
            float minY = Mathf.Min(word.boundingBox.vertices[0].y, word.boundingBox.vertices[1].y);
            float maxY = Mathf.Max(word.boundingBox.vertices[2].y, word.boundingBox.vertices[3].y);
            return maxY - minY;
        }
        return 30f;
    }

    private static bool IsWordVerseMarker(VisionWord word)
    {
        if (word.symbols == null) return false;
        string w = string.Concat(word.symbols.Select(s => s.text));
        return IsVerseMarkerOrPunctuation(w);
    }

    private List<DetectedTextLine> ParseViaHeuristicClustering(VisionResponseItem firstResponse, int imageWidth, int imageHeight)
    {
        List<DetectedTextLine> detectedLines = new List<DetectedTextLine>();
        List<WordBox> rawWords = new List<WordBox>();

        // Collect all individual word boxes (skipping index 0 which is full page text)
        for (int i = 1; i < firstResponse.textAnnotations.Count; i++)
        {
            EntityAnnotation annotation = firstResponse.textAnnotations[i];
            if (annotation.boundingPoly == null || annotation.boundingPoly.vertices == null || annotation.boundingPoly.vertices.Count < 4)
                continue;

            var verts = annotation.boundingPoly.vertices;
            float minX = Mathf.Min(verts[0].x, verts[3].x);
            float minY = Mathf.Min(verts[0].y, verts[1].y);
            float maxX = Mathf.Max(verts[1].x, verts[2].x);
            float maxY = Mathf.Max(verts[2].y, verts[3].y);

            float tokenWidth = maxX - minX;
            float tokenHeight = maxY - minY;

            // 1. Edge-Clipped Fragment Filter:
            bool touchesEdge = (minY <= 6f || maxY >= imageHeight - 6f || minX <= 6f || maxX >= imageWidth - 6f);
            if (touchesEdge && (tokenHeight < 20f || tokenWidth < 12f))
            {
                Debug.Log($"[CloudVisionService] Sliced edge fragment filtered: '{annotation.description}' ({tokenWidth:F0}x{tokenHeight:F0})");
                continue;
            }

            // Microscopic noise filter:
            bool isMicroscopic = (tokenHeight < 10f) || (tokenHeight < 14f && tokenWidth < 6f);
            if (isMicroscopic)
            {
                continue;
            }

            rawWords.Add(new WordBox
            {
                text = annotation.description,
                minX = minX,
                minY = minY,
                maxX = maxX,
                maxY = maxY
            });
        }

        if (rawWords.Count == 0) return detectedLines;

        // 1. Compute global median word height from healthy lexical tokens
        var healthyWords = rawWords.Where(w => w.height >= 16f && !IsVerseMarkerOrPunctuation(w.text)).ToList();
        var heights = (healthyWords.Count > 0 ? healthyWords : rawWords).Select(w => w.height).OrderBy(h => h).ToList();
        float medianHeight = heights.Count > 0 ? heights[(int)(heights.Count * 0.55f)] : 32f;
        medianHeight = Mathf.Max(medianHeight, 24f);
        float lineTolerance = Mathf.Clamp(medianHeight * 0.45f, 14f, 32f);

        // 2. Sort words top-to-bottom, then left-to-right
        var sortedWordsList = rawWords.OrderBy(w => w.centerY).ThenBy(w => w.minX).ToList();

        List<LineCluster> lines = new List<LineCluster>();

        foreach (var word in sortedWordsList)
        {
            LineCluster bestLine = null;
            float minDeltaY = float.MaxValue;

            foreach (var line in lines)
            {
                float deltaY = Math.Abs(line.averageY - word.centerY);
                float clusterMinY = line.words.Min(w => w.minY);
                float clusterMaxY = line.words.Max(w => w.maxY);
                bool verticalOverlap = (word.minY <= clusterMaxY && word.maxY >= clusterMinY);

                if ((deltaY <= lineTolerance || verticalOverlap) && deltaY < minDeltaY)
                {
                    minDeltaY = deltaY;
                    bestLine = line;
                }
            }

            if (bestLine != null)
            {
                bestLine.words.Add(word);
                var lex = bestLine.words.Where(w => !IsVerseMarkerOrPunctuation(w.text)).ToList();
                bestLine.averageY = (lex.Count > 0 ? lex : bestLine.words).Average(w => w.centerY);
            }
            else
            {
                LineCluster newCluster = new LineCluster { averageY = word.centerY };
                newCluster.words.Add(word);
                lines.Add(newCluster);
            }
        }

        // 3. Sort lines top-to-bottom
        lines = lines.OrderBy(l => l.averageY).ToList();

        foreach (var lineCluster in lines)
        {
            if (lineCluster.words.Count == 0) continue;

            var sortedWords = lineCluster.words.OrderBy(w => w.minX).ToList();

            var lineWordHeights = sortedWords
                .Where(w => w.height >= 16f && !IsVerseMarkerOrPunctuation(w.text))
                .Select(w => w.height)
                .OrderBy(h => h)
                .ToList();
            float lineMedianH = lineWordHeights.Count > 0 ? lineWordHeights[lineWordHeights.Count / 2] : medianHeight;
            lineMedianH = Mathf.Max(lineMedianH, 24f);

            float caesuraGapThreshold = Mathf.Max(lineMedianH * 1.8f, 40f);

            var currentPlateWords = new List<WordBox>();

            for (int i = 0; i < sortedWords.Count; i++)
            {
                if (currentPlateWords.Count > 0)
                {
                    float horizontalGap = sortedWords[i].minX - currentPlateWords.Last().maxX;
                    if (horizontalGap > caesuraGapThreshold && !AreRemainingWordsVerseMarkers(sortedWords, i))
                    {
                        AddPlate(currentPlateWords, lineMedianH, detectedLines);
                        currentPlateWords.Clear();
                    }
                }
                currentPlateWords.Add(sortedWords[i]);
            }

            if (currentPlateWords.Count > 0)
            {
                AddPlate(currentPlateWords, lineMedianH, detectedLines);
            }
        }

        return detectedLines;
    }

    private static bool IsNoisePlate(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        string trimmed = text.Trim();

        // 1. Standalone punctuation, verse markers or danda-only tokens
        if (IsVerseMarkerOrPunctuation(trimmed)) return true;

        // 2. Filter stray non-Devanagari margin codes / publisher watermarks (e.g. "DIF", "pg", "p1")
        bool hasDevanagari = false;
        foreach (char c in trimmed)
        {
            if (c >= '\u0900' && c <= '\u097F')
            {
                hasDevanagari = true;
                break;
            }
        }

        // On a Devanagari page, if a short token has no Devanagari characters, it's printer watermark/code noise
        if (!hasDevanagari && trimmed.Length <= 4)
        {
            return true;
        }

        return false;
    }

    private static bool IsVerseMarkerOrPunctuation(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        string trimmed = text.Trim();
        foreach (char c in trimmed)
        {
            // Devanagari danda (\u0964) and double danda (\u0965)
            if (c == '\u0964' || c == '\u0965' || c == '|' || c == '/') continue;
            // Devanagari digits (\u0966 to \u096F)
            if (c >= '\u0966' && c <= '\u096F') continue;
            // ASCII digits
            if (char.IsDigit(c)) continue;
            // Punctuation, symbols, brackets, dots
            if (char.IsPunctuation(c) || char.IsSymbol(c) || char.IsWhiteSpace(c)) continue;

            // If it contains an actual Devanagari or Latin letter, it's real lexical text
            return false;
        }
        return true;
    }

    private static bool AreRemainingWordsVerseMarkers(List<WordBox> words, int startIndex)
    {
        for (int j = startIndex; j < words.Count; j++)
        {
            if (!IsVerseMarkerOrPunctuation(words[j].text))
                return false;
        }
        return true;
    }

    private static void AddPlate(List<WordBox> plateWords, float lineMedianH, List<DetectedTextLine> detectedLines)
    {
        if (plateWords == null || plateWords.Count == 0) return;

        string plateText = string.Join(" ", plateWords.Select(w => w.text)).Trim();
        if (string.IsNullOrEmpty(plateText) || IsNoisePlate(plateText)) return;

        // Base the vertical bounding box on the actual lexical words in the plate (prevents punctuation/dandas from biasing Y)
        var lexicalWords = plateWords.Where(w => !IsVerseMarkerOrPunctuation(w.text)).ToList();
        if (lexicalWords.Count == 0) lexicalWords = plateWords;

        float minX = plateWords.Min(w => w.minX);
        float maxX = plateWords.Max(w => w.maxX);
        float rawMinY = lexicalWords.Min(w => w.minY);
        float rawMaxY = lexicalWords.Max(w => w.maxY);

        // Bound line height cleanly around median word height with 15% matra margin
        float lineH = Mathf.Clamp(rawMaxY - rawMinY, lineMedianH, lineMedianH * 1.30f);
        float minY = rawMinY;
        float lineW = Math.Max(30f, maxX - minX);

        detectedLines.Add(new DetectedTextLine
        {
            text = plateText,
            boundingBox = new Rect(minX, minY, lineW, lineH)
        });
    }
}
