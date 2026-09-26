require('dotenv').config();
const express = require('express');
const cors = require('cors');
const fetch = require('node-fetch');

const app = express();
const PORT = process.env.PORT || 3000;

app.use(cors());
app.use(express.json({ limit: '10mb' }));

// Health Check Endpoint
app.get('/health', (req, res) => {
    res.json({ status: 'ok', service: 'AksharAR Backend Proxy' });
});

// Secure OCR Endpoint
app.post('/api/ocr', async (req, res) => {
    try {
        const apiKey = process.env.GOOGLE_VISION_API_KEY;

        if (!apiKey || apiKey === 'YOUR_GOOGLE_CLOUD_VISION_API_KEY_HERE') {
            console.error('[Backend Proxy Error] GOOGLE_VISION_API_KEY is not configured in .env file!');
            return res.status(500).json({ error: 'Server misconfiguration: Vision API Key missing.' });
        }

        const { image } = req.body;

        if (!image) {
            return res.status(400).json({ error: 'Missing image payload.' });
        }

        console.log(`[Backend Proxy] Received OCR request. Base64 length: ${image.length}`);

        const visionUrl = `https://vision.googleapis.com/v1/images:annotate?key=${apiKey}`;

        const payload = {
            requests: [
                {
                    image: { content: image },
                    features: [{ type: 'DOCUMENT_TEXT_DETECTION' }]
                }
            ]
        };

        const visionResponse = await fetch(visionUrl, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });

        if (!visionResponse.ok) {
            const errorText = await visionResponse.text();
            console.error('[Backend Proxy Error] Cloud Vision API Error:', errorText);
            return res.status(visionResponse.status).send(errorText);
        }

        const visionData = await visionResponse.json();
        console.log('[Backend Proxy] Vision API request successful!');

        // Return raw Vision response back to Unity client
        res.json(visionData);
    } catch (err) {
        console.error('[Backend Proxy Exception]:', err.message);
        res.status(500).json({ error: err.message });
    }
});

app.listen(PORT, () => {
    console.log(`==================================================`);
    console.log(`🚀 AksharAR Secure Backend Proxy running on port ${PORT}`);
    console.log(`🔒 API Key is hidden on server (No keys in Unity C#!)`);
    console.log(`==================================================`);
});
