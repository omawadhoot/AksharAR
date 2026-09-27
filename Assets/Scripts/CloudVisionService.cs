using System;
using System.Collections;
using System.Collections.Generic;
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
    [SerializeField] private string proxyUrl = "http://localhost:3000/api/ocr";

    [Tooltip("Optional pre-shared authentication secret header for Vercel proxy security")]
    [SerializeField] private string proxyAuthToken = "AksharAR_Secret_Token_2026_x9k2";

    [Header("Controller Reference")]
    [SerializeField] private ARLineOverlayController overlayController;

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

        byte[] imageBytes = inputTexture.EncodeToJPG(85);
        string base64Image = Convert.ToBase64String(imageBytes);

        StartCoroutine(SendProxyApiRequest(base64Image, inputTexture.width, inputTexture.height));
    }

    private IEnumerator SendProxyApiRequest(string base64Image, int imageWidth, int imageHeight)
    {
        string jsonPayload = $"{{\"image\":\"{base64Image}\"}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);

        using (UnityWebRequest webRequest = new UnityWebRequest(proxyUrl, "POST"))
        {
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");

            // Inject security header if configured
            if (!string.IsNullOrEmpty(proxyAuthToken))
            {
                webRequest.SetRequestHeader("X-Proxy-Auth-Token", proxyAuthToken);
            }

            Debug.Log($"[CloudVisionService] Sending request to Secure Backend Proxy: {proxyUrl}");
            yield return webRequest.SendWebRequest();

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[CloudVisionService] Proxy Error: {webRequest.error}\nResponse: {webRequest.downloadHandler.text}");
                yield break;
            }

            string responseJson = webRequest.downloadHandler.text;
            Debug.Log($"==================================================");
            Debug.Log($"[CloudVisionService] SUCCESS Response Received from Backend Proxy:\n{responseJson}");
            Debug.Log($"==================================================");

            ProcessVisionResponse(responseJson, imageWidth, imageHeight);
        }
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

            if (firstResponse.textAnnotations == null || firstResponse.textAnnotations.Count == 0)
            {
                Debug.LogWarning("[CloudVisionService] No text detected on image.");
                return;
            }

            string fullPageText = firstResponse.textAnnotations[0].description;
            Debug.Log($"[CloudVisionService] Full Detected Page Text:\n{fullPageText}");

            List<DetectedTextLine> detectedLines = new List<DetectedTextLine>();

            for (int i = 1; i < firstResponse.textAnnotations.Count; i++)
            {
                EntityAnnotation annotation = firstResponse.textAnnotations[i];
                if (annotation.boundingPoly == null || annotation.boundingPoly.vertices == null || annotation.boundingPoly.vertices.Count < 4)
                    continue;

                float minX = annotation.boundingPoly.vertices[0].x;
                float minY = annotation.boundingPoly.vertices[0].y;
                float maxX = annotation.boundingPoly.vertices[2].x;
                float maxY = annotation.boundingPoly.vertices[2].y;

                float width = Math.Max(10f, maxX - minX);
                float height = Math.Max(10f, maxY - minY);

                detectedLines.Add(new DetectedTextLine
                {
                    text = annotation.description,
                    boundingBox = new Rect(minX, minY, width, height)
                });
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
