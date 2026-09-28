using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class ARCloudVisionTester : MonoBehaviour
{
    [Header("Script References")]
    [SerializeField] private CloudVisionService visionService;
    [SerializeField] private ARLineOverlayController overlayController;

    [Header("Live API Test Config")]
    [Tooltip("Assign a test image (e.g. Assets/Images/Page1.png) to test live OCR API requests")]
    [SerializeField] private Texture2D testImage;

    [Tooltip("If true, automatically sends testImage to Vercel API on Start()")]
    [SerializeField] private bool testLiveApiOnStart = false; // DISABLED BY DEFAULT

    private void Start()
    {
        if (visionService == null)
            visionService = GetComponent<CloudVisionService>();

        if (overlayController == null)
            overlayController = GetComponent<ARLineOverlayController>();

        if (testLiveApiOnStart && testImage != null)
        {
            RunLiveApiTest();
        }
    }

    private void Update()
    {
        bool spacePressed = false;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            spacePressed = true;
        }
#else
        if (Input.GetKeyDown(KeyCode.Space))
        {
            spacePressed = true;
        }
#endif

        if (spacePressed)
        {
            if (testImage != null)
            {
                Debug.Log("[ARCloudVisionTester] SPACE pressed: Triggering LIVE API Test...");
                RunLiveApiTest();
            }
            else
            {
                Debug.Log("[ARCloudVisionTester] SPACE pressed: Triggering Mock Test (No testImage assigned)...");
                RunMockVisionTest();
            }
        }
    }

    [ContextMenu("Run Live API Test")]
    public void RunLiveApiTest()
    {
        if (visionService == null)
        {
            Debug.LogError("[ARCloudVisionTester] CloudVisionService reference is missing!");
            return;
        }

        if (testImage == null)
        {
            Debug.LogError("[ARCloudVisionTester] Please assign a test Texture2D (e.g. Page1.png) in the Inspector!");
            return;
        }

        Debug.Log($"==================================================");
        Debug.Log($"[ARCloudVisionTester] Sending '{testImage.name}' ({testImage.width}x{testImage.height}) to Live Vercel Proxy...");
        Debug.Log($"==================================================");

        visionService.DetectTextFromTexture(testImage);
    }

    [ContextMenu("Run Mock Vision Test")]
    public void RunMockVisionTest()
    {
        Debug.Log("==================================================");
        Debug.Log("[ARCloudVisionTester] Running Mock Offline Test");
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
