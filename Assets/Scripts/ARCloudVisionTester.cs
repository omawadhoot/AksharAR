using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ARCloudVisionTester : MonoBehaviour
{
    [Header("Controller Reference")]
    [SerializeField] private ARLineOverlayController overlayController;

    [Header("Cloud Vision API Config")]
    [SerializeField] private string apiKey = "YOUR_GOOGLE_CLOUD_VISION_API_KEY";
    [SerializeField] private bool testInEditorOnStart = true;

    private void Start()
    {
        if (overlayController == null)
            overlayController = GetComponent<ARLineOverlayController>();

        if (testInEditorOnStart)
        {
            RunMockVisionTest();
        }
    }

    private void Update()
    {
        // Press SPACE in Editor Play Mode to re-trigger test
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Debug.Log("[ARCloudVisionTester] Triggering Mock Cloud Vision Test...");
            RunMockVisionTest();
        }
    }

    [ContextMenu("Run Mock Vision Test")]
    public void RunMockVisionTest()
    {
        Debug.Log("==================================================");
        Debug.Log("[Cloud Vision OCR Test] Simulated API Response Received");
        Debug.Log("==================================================");

        Vector2 visionImageSize = new Vector2(1080, 1920);

        List<DetectedTextLine> mockLines = new List<DetectedTextLine>
        {
            new DetectedTextLine {
                text = "दिन रात विकास कुमार किताब लिख रहा है।",
                boundingBox = new Rect(80, 150, 920, 80)
            },
            new DetectedTextLine {
                text = "उसकी स्थिति दिल्ली के क्लिष्ट नियमों जैसी विचित्र है।",
                boundingBox = new Rect(80, 270, 920, 80)
            },
            new DetectedTextLine {
                text = "क्षत्रिय वैज्ञानिक ने अपने ज्ञान और श्रम से नया क्षेत्र बनाया।",
                boundingBox = new Rect(80, 390, 920, 80)
            },
            new DetectedTextLine {
                text = "प्रकाश ने धर्म, कर्म और वर्षा के प्राकृतिक चक्र को समझा।",
                boundingBox = new Rect(80, 510, 920, 80)
            },
            new DetectedTextLine {
                text = "विद्यालय का खट्टा-मीठा अनुभव अद्भुत और बुड्ढा कर देने वाला था।",
                boundingBox = new Rect(80, 630, 920, 80)
            },
            new DetectedTextLine {
                text = "फ़िल्म का ज़ीरो डार्क थर्टी दृश्य साफ़ तौर पर बड़ा और टेढ़ा था।",
                boundingBox = new Rect(80, 750, 920, 80)
            }
        };

        if (overlayController != null)
        {
            overlayController.DisplayDetectedLines(mockLines, visionImageSize);
        }
        else
        {
            Debug.LogError("[ARCloudVisionTester] ARLineOverlayController reference is missing!");
        }
    }
}
