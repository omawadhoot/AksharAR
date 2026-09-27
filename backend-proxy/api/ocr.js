const fetch = require('node-fetch');

module.exports = async (req, res) => {
    // Enable CORS for Unity client requests
    res.setHeader('Access-Control-Allow-Credentials', true);
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'GET,OPTIONS,POST');
    res.setHeader('Access-Control-Allow-Headers', 'X-CSRF-Token, X-Requested-With, Accept, Accept-Version, Content-Length, Content-MD5, Content-Type, Date, X-Api-Version');

    if (req.method === 'OPTIONS') {
        return res.status(200).end();
    }

    // GET request (Health Check)
    if (req.method === 'GET') {
        return res.status(200).json({
            status: 'online',
            service: 'AksharAR Google Cloud Vision Backend Proxy',
            endpoint: '/api/ocr',
            method: 'POST'
        });
    }

    if (req.method !== 'POST') {
        return res.status(405).json({ error: 'Method not allowed. Use POST.' });
    }

    try {
        const apiKey = process.env.GOOGLE_VISION_API_KEY;

        if (!apiKey) {
            console.error('[Vercel Error] GOOGLE_VISION_API_KEY environment variable missing.');
            return res.status(500).json({ error: 'Server misconfiguration: GOOGLE_VISION_API_KEY missing on Vercel.' });
        }

        const { image } = req.body;

        if (!image) {
            return res.status(400).json({ error: 'Missing image payload in body.' });
        }

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
            console.error('[Vercel Cloud Vision Error]:', errorText);
            return res.status(visionResponse.status).send(errorText);
        }

        const visionData = await visionResponse.json();
        return res.status(200).json(visionData);
    } catch (err) {
        console.error('[Vercel Exception]:', err.message);
        return res.status(500).json({ error: err.message });
    }
};
