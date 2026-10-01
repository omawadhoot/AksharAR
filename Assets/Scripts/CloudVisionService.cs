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

        Texture2D readableTexture = GetUncompressedResizedTexture(inputTexture, targetWidth, targetHeight);

        byte[] imageBytes = readableTexture.EncodeToJPG(78);
        string base64Image = Convert.ToBase64String(imageBytes);

        Destroy(readableTexture);

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
                return;
            }

            if (firstResponse.textAnnotations == null || firstResponse.textAnnotations.Count <= 1)
            {
                Debug.LogWarning("[CloudVisionService] No text detected on image.");
                return;
            }

            string fullPageText = firstResponse.textAnnotations[0].description;
            Debug.Log($"[CloudVisionService] Full Detected Page Text Length: {fullPageText.Length} chars.");

            List<WordBox> rawWords = new List<WordBox>();

            // Collect all individual word boxes (skipping index 0 which is full page text)
            for (int i = 1; i < firstResponse.textAnnotations.Count; i++)
            {
                EntityAnnotation annotation = firstResponse.textAnnotations[i];
                if (annotation.boundingPoly == null || annotation.boundingPoly.vertices == null || annotation.boundingPoly.vertices.Count < 4)
                    continue;

                var verts = annotation.boundingPoly.vertices;
                // BoundingPoly Catch: Use minimum/average across top-left vertices to keep lines level on tilted snaps
                float minX = Mathf.Min(verts[0].x, verts[3].x); // Leftmost of left vertices
                float minY = Mathf.Min(verts[0].y, verts[1].y); // Topmost of top vertices (keeps lines level)
                float maxX = Mathf.Max(verts[1].x, verts[2].x); // Rightmost of right vertices
                float maxY = Mathf.Max(verts[2].y, verts[3].y); // Bottommost of bottom vertices

                rawWords.Add(new WordBox
                {
                    text = annotation.description,
                    minX = minX,
                    minY = minY,
                    maxX = maxX,
                    maxY = maxY
                });
            }

            // 1. Compute global median word height
            var heights = rawWords.Select(w => w.height).OrderBy(h => h).ToList();
            float medianHeight = heights.Count > 0 ? heights[heights.Count / 2] : 30f;
            float lineTolerance = Mathf.Clamp(medianHeight * 0.38f, 10f, 22f);

            // 2. Sort words top-to-bottom, then left-to-right
            var sortedWordsList = rawWords.OrderBy(w => w.centerY).ThenBy(w => w.minX).ToList();

            List<LineCluster> lines = new List<LineCluster>();

            foreach (var word in sortedWordsList)
            {
                // Find line whose current averageY is closest to word.centerY within tight lineTolerance
                LineCluster bestLine = null;
                float minDeltaY = float.MaxValue;

                foreach (var line in lines)
                {
                    float deltaY = Math.Abs(line.averageY - word.centerY);
                    if (deltaY <= lineTolerance && deltaY < minDeltaY)
                    {
                        minDeltaY = deltaY;
                        bestLine = line;
                    }
                }

                if (bestLine != null)
                {
                    bestLine.words.Add(word);
                    bestLine.averageY = bestLine.words.Average(w => w.centerY);
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

            List<DetectedTextLine> detectedLines = new List<DetectedTextLine>();

            foreach (var lineCluster in lines)
            {
                if (lineCluster.words.Count == 0) continue;

                // Sort words inside line strictly left-to-right
                var sortedWords = lineCluster.words.OrderBy(w => w.minX).ToList();

                string combinedLineText = string.Join(" ", sortedWords.Select(w => w.text));
                float minX = sortedWords.Min(w => w.minX);
                float maxX = sortedWords.Max(w => w.maxX);

                // Absolute ink bounds directly from Cloud Vision OCR (prevents cumulative drift!)
                float rawMinY = sortedWords.Min(w => w.minY);
                float rawMaxY = sortedWords.Max(w => w.maxY);

                // Compute robust line height using median word height in this specific line
                var lineWordHeights = sortedWords.Select(w => w.height).OrderBy(h => h).ToList();
                float lineMedianH = lineWordHeights[lineWordHeights.Count / 2];

                // Bound line height cleanly around median word height so matras fit without ballooning
                float lineH = Mathf.Clamp(rawMaxY - rawMinY, lineMedianH, lineMedianH * 1.30f);
                float minY = rawMinY; // STRICT ABSOLUTE Y-MIN BINDING (eliminates cumulative pitch drift)
                float lineW = Math.Max(40f, maxX - minX);

                detectedLines.Add(new DetectedTextLine
                {
                    text = combinedLineText,
                    boundingBox = new Rect(minX, minY, lineW, lineH)
                });
            }

            Debug.Log($"[CloudVisionService] Grouped {rawWords.Count} words into {detectedLines.Count} clean text line strips.");

            if (worldSpaceUIToolkitController != null)
            {
                worldSpaceUIToolkitController.DisplayDetectedLines(detectedLines, new Vector2(imageWidth, imageHeight));
            }

            if (overlayController != null)
            {
                overlayController.DisplayDetectedLines(detectedLines, new Vector2(imageWidth, imageHeight));
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CloudVisionService] JSON Parsing Exception: {ex.Message}");
        }
    }
}
